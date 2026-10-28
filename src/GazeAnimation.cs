using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace DesktopKitten {
    public sealed class GazeAnimation {
        public int SchemaVersion,Width,Height;
        public string[] Frames;
        public int Count {get{return Frames.Length;}}
        public static GazeAnimation Load(string path){
            if(!File.Exists(path))throw new InvalidDataException("缺少新版32方向转头素材，请重新安装完整程序。");
            if(new FileInfo(path).Length>32768)throw new InvalidDataException("转头素材配置过大");
            var value=new JavaScriptSerializer().Deserialize<GazeAnimation>(File.ReadAllText(path));
            if(value==null)throw new InvalidDataException("转头素材配置为空");
            value.Validate();
            foreach(var file in value.Frames)if(!File.Exists(Path.Combine(Path.GetDirectoryName(path),file)))throw new InvalidDataException("转头素材不完整："+file);
            return value;
        }
        public void Validate(){
            if(SchemaVersion!=1||Width!=192||Height!=208||Frames==null||Frames.Length!=Motion.GazeDirectionCount)throw new InvalidDataException("转头素材尺寸或方向数无效");
            for(int i=0;i<Frames.Length;i++)if(Frames[i]!=i.ToString("000")+".png")throw new InvalidDataException("转头素材顺序或文件名无效");
        }
    }
    // Follow the shortest angular path. Large pointer jumps catch up promptly;
    // nearby movements use the new intermediate frames without image blending.
    public sealed class GazeFollower {
        public int Current {get;private set;}
        double changed;
        public GazeFollower(){Reset();}
        public void Reset(){Current=-1;changed=double.NegativeInfinity;}
        public int Update(int target,double now){
            if(target<0){Reset();return Current;}
            if(target>=Motion.GazeDirectionCount)throw new ArgumentOutOfRangeException("target");
            if(Current<0){Current=target;changed=now;return Current;}
            if(Current==target||now-changed<50)return Current;
            int count=Motion.GazeDirectionCount;
            int delta=(target-Current+count+count/2)%count-count/2;
            int distance=Math.Abs(delta),step=distance>8?4:distance>4?2:1;
            Current=(Current+Math.Sign(delta)*Math.Min(step,distance)+count)%count;changed=now;return Current;
        }
    }
}
