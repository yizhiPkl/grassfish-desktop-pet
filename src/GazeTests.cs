using System;
using System.IO;
using System.Linq;
namespace DesktopKitten {
    public static class GazeTests {
        public static void Run(Assets art,Action<bool,string> check){
            check(art.Gaze.Count==32&&Motion.GazeDirectionCount==32,"32 directions are active, no legacy fallback");
            for(int i=0;i<32;i++){
                double angle=i*11.25*Math.PI/180;
                check(Motion.Direction(Math.Sin(angle)*100,-Math.Cos(angle)*100,-1,320)==i,"new direction mapping "+i);
                var f=art.GazeFrame(i);check(f.PixelWidth==192&&f.PixelHeight==208,"new gaze dimensions "+i);
                check(object.ReferenceEquals(f,art.GazeFrame(i)),"new gaze cache "+i);
                if(i%2==0)check(Tests.Pixels(f).SequenceEqual(Tests.Pixels(art.Frame(9+i/16,(i/2)%8,0))),"legacy atlas also contains new video frames "+i);
            }
            check(Enumerable.Range(0,32).Select(i=>Convert.ToBase64String(Tests.Pixels(art.GazeFrame(i)))).Distinct().Count()==32,"all 32 gaze frames are distinct");
            var follower=new GazeFollower();check(follower.Update(31,0)==31&&follower.Update(0,50)==0,"forward wrap uses shortest adjacent path");
            check(follower.Update(31,100)==31,"reverse wrap uses shortest adjacent path");
            follower.Reset();check(follower.Update(0,0)==0&&follower.Update(1,20)==0&&follower.Update(1,50)==1,"adjacent gaze respects bounded pacing");
            follower.Reset();follower.Update(0,0);for(int i=1;i<=8;i++)follower.Update(16,i*50);check(follower.Current==16,"opposite pointer target catches up within 400ms");
            check(follower.Update(-1,450)==-1&&follower.Update(24,500)==24,"deadzone reset never replays a full circle");
            follower.Reset();follower.Update(0,0);follower.Update(8,50);follower.Update(31,100);check(follower.Current>=0&&follower.Current<32,"pointer reversal stays valid");
            var bad=new GazeAnimation{SchemaVersion=1,Width=192,Height=208,Frames=Enumerable.Range(0,32).Select(i=>i.ToString("000")+".png").ToArray()};
            bad.Frames[0]="../bad.png";bool rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}check(rejected,"gaze paths reject traversal");
            bad.Frames=new string[16];rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}check(rejected,"16-frame gaze configuration is rejected");
        }
    }
}
