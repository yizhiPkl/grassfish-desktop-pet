using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace DesktopKitten {
    public sealed class PettingFrame { public string id; public int ms,sourceFrame; }
    public sealed class PettingRecovery { public int sourceMin,sourceMax; public double minimumTravelMs,msPerSourceFrame,settleMs,joinMs,earlyExitMs,pauseMs; public bool interpolateAdjacentFrames; }
    public sealed class PettingAnimation {
        public int schemaVersion,width,height; public int[] catOrigin,idleDurations;
        public PettingFrame[] entry,hold; public double entryBlendMs,resumeMs;
        public PettingRecovery recovery; public string hitMask;
        public Dictionary<string,string> frameFiles;
        byte[] mask;
        public static PettingAnimation Load(string path){
            if(!File.Exists(path))return null;
            if(new FileInfo(path).Length>256*1024)throw new InvalidDataException("悬停动画配置过大");
            var data=new JavaScriptSerializer().Deserialize<PettingAnimation>(File.ReadAllText(path));
            if(data==null)throw new InvalidDataException("悬停动画配置为空");data.Validate();return data;
        }
        public void Validate(){
            if(schemaVersion!=1||width!=240||height!=232||catOrigin==null||catOrigin.Length!=2||catOrigin[0]!=24||catOrigin[1]!=8||
               idleDurations==null||!idleDurations.SequenceEqual(Motion.Idle)||entryBlendMs<=0||entryBlendMs>1000||resumeMs<=0||resumeMs>1000||
               frameFiles==null||frameFiles.Count<60||frameFiles.Count>128)throw new InvalidDataException("悬停动画配置无效");
            foreach(var pair in frameFiles){if(string.IsNullOrEmpty(pair.Value)||Path.GetFileName(pair.Value)!=pair.Value||pair.Value.IndexOfAny(new[]{'/',(char)92,':'})>=0||!pair.Value.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("悬停动画图片路径无效");}
            foreach(var track in new[]{entry,hold}){
                if(track==null||track.Length==0||track.Length>128)throw new InvalidDataException("悬停动画时序无效");
                foreach(var f in track)if(f==null||!frameFiles.ContainsKey(f.id)||f.ms<1||f.ms>1000||f.sourceFrame<1||f.sourceFrame>59)throw new InvalidDataException("悬停动画帧无效");
            }
            if(recovery==null||recovery.sourceMin!=1||recovery.sourceMax!=59||recovery.minimumTravelMs<100||recovery.minimumTravelMs>3000||recovery.msPerSourceFrame<1||recovery.msPerSourceFrame>100||recovery.settleMs<1||recovery.settleMs>1000||recovery.joinMs<1||recovery.joinMs>1000||recovery.earlyExitMs<1||recovery.earlyExitMs>1000||recovery.pauseMs<0||recovery.pauseMs>1000)throw new InvalidDataException("自然恢复配置无效");
            for(int n=1;n<=59;n++)if(!frameFiles.ContainsKey("pet-"+n.ToString("000")))throw new InvalidDataException("自然恢复连续帧缺失");
            if(!frameFiles.ContainsKey("idle-0-0"))throw new InvalidDataException("悬停待机帧缺失");
            mask=Convert.FromBase64String(hitMask??"");if(mask.Length!=width*height)throw new InvalidDataException("悬停命中区域无效");
        }
        public bool Hit(double x,double y){return x>=0&&y>=0&&x<width&&y<height&&mask[(int)y*width+(int)x]!=0;}
    }
    public sealed class PettingLayer { public string id; public double weight; }
    public sealed class PettingPose { public string state,recoveryStage; public PettingLayer[] layers; public double elapsed; public bool hovered; }
    // Faithful port of the user-approved natural-recovery preview player.
    public sealed class PettingPlayer {
        readonly PettingAnimation data;
        public string State {get;private set;} public bool Hovered {get;private set;}
        public double IdleOrigin {get;private set;} public double ExitDuration {get;private set;}
        double started,entryMs,holdMs,holdOffset,recoverySource,travelMs;
        bool resume,earlyExit;PettingLayer[] from=One("idle-0-0");
        public PettingPlayer(PettingAnimation data){this.data=data;State="idle";entryMs=data.entry.Sum(f=>f.ms);holdMs=data.hold.Sum(f=>f.ms);}
        static PettingLayer[] One(string id){return new[]{new PettingLayer{id=id,weight=1}};}
        static double Clamp(double t){return Math.Max(0,Math.Min(1,t));}
        static double Ease(double t){t=Clamp(t);return t*t*(3-2*t);}
        static int IndexAt(int[] times,double ms){double sum=times.Sum(),t=((ms%sum)+sum)%sum;for(int i=0;i<times.Length;i++){if(t<times[i])return i;t-=times[i];}return 0;}
        static PettingLayer[] Normalize(IEnumerable<PettingLayer> layers){
            var list=new List<PettingLayer>();foreach(var f in layers){if(f.weight<=0)continue;var old=list.FirstOrDefault(x=>x.id==f.id);if(old==null)list.Add(new PettingLayer{id=f.id,weight=f.weight});else old.weight+=f.weight;}
            list=list.Where(f=>f.weight>=.000001).ToList();double sum=list.Sum(f=>f.weight);foreach(var f in list)f.weight/=sum;return list.ToArray();
        }
        static PettingLayer[] Mix(PettingLayer[] a,PettingLayer[] b,double t){t=Clamp(t);return Normalize(a.Select(f=>new PettingLayer{id=f.id,weight=f.weight*(1-t)}).Concat(b.Select(f=>new PettingLayer{id=f.id,weight=f.weight*t})));}
        static double Source(PettingLayer[] layers){var a=layers.Where(f=>f.id.StartsWith("pet-",StringComparison.Ordinal)).ToArray();double sum=a.Sum(f=>f.weight);return sum>0?a.Sum(f=>int.Parse(f.id.Substring(4))*f.weight)/sum:1;}
        PettingLayer[] Idle(double now){double elapsed=Math.Max(0,now-IdleOrigin);int col=IndexAt(data.idleDurations,elapsed),phase=col==0?0:Motion.BlinkPhase(elapsed);return One("idle-"+col+"-"+phase);}
        static PettingLayer[] Pose(PettingFrame[] track,double ms,bool repeat){int duration=track.Sum(f=>f.ms);double elapsed=repeat?Math.Max(0,ms):Math.Max(0,Math.Min(duration-.0001,ms));return One(track[IndexAt(track.Select(f=>f.ms).ToArray(),elapsed)].id);}
        static PettingLayer[] Adjacent(double n){n=Math.Max(1,Math.Min(59,n));int lo=(int)Math.Floor(n),hi=(int)Math.Ceiling(n);var a=One("pet-"+lo.ToString("000"));return lo==hi?a:Mix(a,One("pet-"+hi.ToString("000")),n-lo);}
        public PettingPose Sample(double now){
            if(double.IsNaN(now)||double.IsInfinity(now)||now<0)throw new ArgumentOutOfRangeException("now");
            double elapsed=Math.Max(0,now-started);
            if(State=="enter"){double duration=resume?data.resumeMs:entryMs;if(elapsed>=duration){State="hold";started+=duration;elapsed=now-started;}}
            if(State=="exit"&&elapsed>=ExitDuration){State="idle";started+=ExitDuration;IdleOrigin=started;elapsed=now-started;}
            PettingLayer[] layers;string stage=State;
            if(State=="idle")layers=Idle(now);
            else if(State=="hold")layers=Pose(data.hold,elapsed+holdOffset,true);
            else if(State=="enter")layers=resume?Mix(from,Pose(data.hold,holdOffset,true),Ease(elapsed/data.resumeMs)):Mix(from,Pose(data.entry,elapsed,false),elapsed/data.entryBlendMs);
            else {var r=data.recovery;
                if(earlyExit){layers=Mix(from,One("idle-0-0"),Ease(elapsed/r.earlyExitMs));stage="earlyReturn";}
                else if(elapsed<r.pauseMs){layers=from;stage="settling";}
                else if(elapsed<r.pauseMs+travelMs){double local=elapsed-r.pauseMs,n=recoverySource+(1-recoverySource)*Ease(local/travelMs);layers=Mix(from,Adjacent(n),Ease(local/r.joinMs));stage="continuousReturn";}
                else {double local=elapsed-r.pauseMs-travelMs;layers=Mix(One("pet-001"),One("idle-0-0"),Ease(local/r.settleMs));stage="idleJoin";}
            }
            return new PettingPose{state=State,layers=layers,elapsed=elapsed,hovered=Hovered,recoveryStage=stage};
        }
        public bool SetHover(bool value,double now){
            if(value==Hovered)return false;var prior=Sample(now);string old=prior.state;double oldElapsed=prior.elapsed;bool oldResume=resume;
            Hovered=value;from=prior.layers;started=now;
            if(value){State="enter";resume=old=="exit";earlyExit=false;
                if(resume){double n=Source(prior.layers);int nearest=0;for(int i=1;i<data.hold.Length;i++)if(Math.Abs(data.hold[i].sourceFrame-n)<Math.Abs(data.hold[nearest].sourceFrame-n))nearest=i;holdOffset=data.hold.Take(nearest).Sum(f=>f.ms);}else holdOffset=0;
            }else if(old!="idle"){State="exit";earlyExit=old=="enter"&&!oldResume&&oldElapsed<entryMs*.5;resume=false;recoverySource=Math.Max(1,Math.Min(59,Source(prior.layers)));travelMs=Math.Max(data.recovery.minimumTravelMs,(recoverySource-1)*data.recovery.msPerSourceFrame);ExitDuration=earlyExit?data.recovery.earlyExitMs:data.recovery.pauseMs+travelMs+data.recovery.settleMs;}else State="idle";
            return true;
        }
        public void EnterFromCurrent(double now){SetHover(true,now);from=One("captured");}
        public void Cancel(double now){State="idle";Hovered=false;resume=false;earlyExit=false;started=now;IdleOrigin=now;from=One("idle-0-0");}
    }
    public static class PettingRules {
        public static bool Allowed(string task,bool completion,bool animations,bool dragging,bool mouseDown){return !completion&&animations&&!dragging&&!mouseDown&&(task=="idle"||task=="unknown");}
    }
}
