using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopKitten {
    public sealed class PettingTestStep { public double now; public bool? hover; public PettingPose pose; }
    public sealed class PettingTestScenario { public string name; public PettingTestStep[] steps; }
    public static class PettingTests {
        // Compare display-equivalent premultiplied bytes: converting low-alpha
        // pixels back to straight RGB introduces rounding that is not a visual
        // change. Source PNG bytes/pixels are checked separately before install.
        static byte[] DisplayPixels(BitmapSource source){var bitmap=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);return data;}
        public static void Run(Assets art,Action<bool,string> check){
            var config=art.Petting;check(config!=null,"approved calibrated petting installed");
            check(config.frameFiles.Count==80&&config.width==240&&config.height==232,"80 preserved fixed-canvas petting frames");
            check(config.entry.Sum(f=>f.ms)==620&&config.hold.Sum(f=>f.ms)==2250,"approved entry and hold timing preserved");
            foreach(var task in new[]{"running","waiting","review","failed"})check(!PettingRules.Allowed(task,false,true,false,false),"task priority prevents petting "+task);
            check(PettingRules.Allowed("idle",false,true,false,false),"idle permits body hover");
            check(!PettingRules.Allowed("idle",true,true,false,false),"completion wave has priority over petting");
            check(!PettingRules.Allowed("idle",false,false,false,false),"reduced motion disables petting");
            check(!PettingRules.Allowed("idle",false,true,true,false)&&!PettingRules.Allowed("idle",false,true,false,true),"drag/click do not trigger petting");
            check(!config.Hit(-1,100)&&!config.Hit(240,100)&&!config.Hit(100,232)&&!config.Hit(0,0),"transparent padding never triggers enjoyment");
            for(int col=0;col<6;col++)for(int phase=0;phase<(col==0?1:4);phase++){
                var image=art.PettingFrame(new[]{new PettingLayer{id="idle-"+col+"-"+phase,weight=1}});
                var cropped=new CroppedBitmap(image,new Int32Rect(24,8,192,208));
                check(DisplayPixels(cropped).SequenceEqual(DisplayPixels(art.Frame(0,col,phase))),"padded idle display is pixel-exact "+col+":"+phase);
            }
            foreach(var id in config.frameFiles.Keys){var image=art.PettingFrame(new[]{new PettingLayer{id=id,weight=1}});check(image.PixelWidth==240&&image.PixelHeight==232,"petting picture loads "+id);}
            art.CapturePetting(art.Frame(9,0,0));var captured=art.PettingFrame(new[]{new PettingLayer{id="captured",weight=1}});
            check(DisplayPixels(new CroppedBitmap(captured,new Int32Rect(24,8,192,208))).SequenceEqual(DisplayPixels(art.Frame(9,0,0))),"entry captures current gaze without an initial snap");
            var player=new PettingPlayer(config);player.EnterFromCurrent(100);check(player.Sample(100).layers.Single().id=="captured","runtime entry starts at captured pose");
            check(player.Sample(720).state=="hold"&&player.Sample(3600000).state=="hold","hover enjoyment lasts indefinitely");
            player.SetHover(false,3600000);check(player.ExitDuration>=1420&&player.ExitDuration<=2500,"leave keeps natural recovery duration");
            player.Cancel(3600100);check(player.State=="idle"&&!player.Hovered,"task interruption cancels enjoyment immediately");
            string fixtures=Environment.GetEnvironmentVariable("KITTEN_PETTING_FIXTURES");
            if(!string.IsNullOrEmpty(fixtures)){
                if(new FileInfo(fixtures).Length>8*1024*1024)throw new InvalidDataException("petting test fixture too large");
                var cases=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<PettingTestScenario[]>(File.ReadAllText(fixtures));
                foreach(var scenario in cases){var p=new PettingPlayer(config);foreach(var step in scenario.steps){
                    if(step.hover.HasValue)p.SetHover(step.hover.Value,step.now);var pose=p.Sample(step.now);var expected=step.pose;
                    string label=scenario.name+"@"+step.now;
                    check(pose.state==expected.state&&pose.recoveryStage==expected.recoveryStage&&pose.hovered==expected.hovered,"approved player state parity "+label);
                    check(Math.Abs(pose.elapsed-expected.elapsed)<1e-6,"approved player clock parity "+label);
                    var a=pose.layers.ToDictionary(f=>f.id,f=>f.weight);var b=expected.layers.ToDictionary(f=>f.id,f=>f.weight);
                    check(a.Count==b.Count&&a.All(f=>b.ContainsKey(f.Key)&&Math.Abs(f.Value-b[f.Key])<1e-8),"approved blend and recovery parity "+label);
                    check(Math.Abs(pose.layers.Sum(f=>f.weight)-1)<1e-8&&pose.layers.All(f=>f.weight>=0&&f.weight<=1),"blend weights bounded "+label);
                }}
            }
        }
    }
}
