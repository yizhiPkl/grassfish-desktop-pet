using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace DesktopKitten {
    public static class CompletionTests {
        static Snapshot Status(string state,int seconds){return new Snapshot{State=state,Connected=true,EndAt=DateTimeOffset.Parse("2026-10-14T00:00:00Z").AddSeconds(seconds)};}
        public static void Run(Assets art,Action<bool,string> check){
            var boot=new CompletionSession();boot.Update(Status("review",5),100,true);
            check(!boot.Active,"bootstrap historical completion not replayed");
            var done=new CompletionSession();done.Update(Status("running",0),100,true);done.Update(Status("review",5),500,true);
            check(done.Active&&done.AwaitingHandoff,"fresh completion awaits keyboard handoff");
            check(!done.PromptReady&&!done.TryPresentPrompt(new WorkPose{IsEntry=false,Index=0}),"prompt cannot precede the keyboard handoff");
            done.Update(Status("review",5),600,true);check(done.Elapsed(650)==150,"duplicate completion does not restart clock");
            check(!done.TryHandoff(new WorkPose{IsEntry=true,Index=26},26,true,700),"working entrance cannot hand off completion");
            check(!done.TryHandoff(new WorkPose{IsEntry=false,Index=25},26,true,700),"wrong work pose waits for aligned handoff");
            check(done.TryHandoff(new WorkPose{IsEntry=false,Index=26},26,true,800)&&!done.AwaitingHandoff,"aligned handoff occurs once");
            check(!done.TryPresentPrompt(null),"missing frame cannot reveal a completion prompt");
            check(!done.PromptReady&&done.TryPresentPrompt(art.Complete.Sample(0,true))&&done.PromptReady,"first displayed keyboard exit frame reveals completion text");
            check(!done.TryPresentPrompt(art.Complete.Sample(art.Complete.EntryDurationMs-1,true)),"keyboard exit does not reveal completion text twice");
            check(!done.TryPresentPrompt(art.Complete.Sample(art.Complete.EntryDurationMs,true))&&done.PromptReady,"waving keeps the already visible completion prompt");
            check(!done.TryPresentPrompt(art.Complete.Sample(art.Complete.EntryDurationMs+500,true)),"later loop frames never repeat the prompt reveal");
            check(!done.TryHandoff(new WorkPose{Index=26},26,true,900)&&done.Elapsed(1000)==200,"duplicate handoff does not restart entry");
            done.Update(Status("idle",5),9000,true);check(done.Active&&done.PromptReady&&done.Elapsed(10000)==9200,"completion survives review timeout");
            done.Dismiss();check(!done.Active&&!done.PromptReady&&done.SuppressReview(Status("review",5)),"explicit close acknowledges current completion");
            check(!done.TryPresentPrompt(new WorkPose{IsEntry=false}),"late frame cannot resurrect a dismissed prompt");
            done.Update(Status("review",5),10000,true);check(!done.Active,"closed prompt never reappears on same event");
            done.Update(Status("running",5),11000,true);done.Update(Status("review",8),12000,true);
            check(done.Active&&done.AwaitingHandoff&&!done.PromptReady,"next completed task creates new completion");
            done.Update(Status("running",8),13000,true);check(!done.Active&&!done.PromptReady&&!done.TryPresentPrompt(new WorkPose{IsEntry=false}),"new work immediately supersedes completion");
            done.Update(Status("review",9),14000,true);done.Update(Status("waiting",9),15000,true);check(!done.Active,"approval waiting supersedes completion");
            done.Update(Status("review",10),16000,true);done.Update(Status("failed",10),17000,true);check(!done.Active,"failure supersedes completion");
            done.Update(Status("review",11),18000,true);done.Update(Status("idle",11),19000,false);check(!done.Active,"turning off monitoring clears task notification");
            done.Update(Status("running",11),20000,true);done.Update(Status("review",12),21000,true);
            check(done.TryHandoff(new WorkPose{IsEntry=true,Index=0},26,false,22000),"reduced motion bypasses animated keyboard handoff");
            check(done.TryPresentPrompt(art.Complete.Sample(0,false))&&done.PromptReady,"reduced motion presents static waving pose without a timed delay");
            done.Dismiss();done.Preview(23000);check(done.Active&&!done.AwaitingHandoff&&done.Elapsed(23100)==100,"manual preview is independent from working clock");
            check(!done.PromptReady&&done.TryPresentPrompt(art.Complete.Sample(0,true)),"manual preview announces its first displayed keyboard exit frame");
            check(!done.TryPresentPrompt(art.Complete.Sample(art.Complete.EntryDurationMs,true)),"manual preview does not repeat its prompt at the waving pose");
            done.Preview(23150);check(!done.PromptReady,"restarting a preview clears previously presented text");
            done.Dismiss();check(!done.Active,"manual preview can be closed");
            done.Preview(24000);done.Update(Status("idle",12),25000,false);check(done.Active&&done.Previewing,"manual preview works with Codex monitoring disabled");done.Dismiss();
            var instant=new CompletionSession();instant.Update(Status("idle",0),0,true);instant.Update(Status("review",1),100,true);
            check(instant.Active&&!instant.AwaitingHandoff&&!instant.PromptReady,"completion first seen outside running still waits for its displayed completion frame");
            instant.Update(Status("review",1),10000,true);check(!instant.PromptReady,"elapsed wall time alone cannot make an unrendered prompt ready");
            instant.Update(Status("idle",1),11000,false);check(!instant.Active&&!instant.PromptReady&&!instant.TryPresentPrompt(new WorkPose{IsEntry=false}),"disabling integration cannot leave a delayed prompt");
            var clip=art.Complete;check(clip!=null,"completion clip installed");
            check(clip.Width==288&&clip.Height==272&&clip.OriginX==-40&&clip.OriginY==-12,"left tail padding preserves body registration and both ears");
            check(clip.HandoffWorkIndex==art.Work.Loop.Length-1,"handoff matches reference keyboard pose");
            check(clip.Entry.Length>10&&clip.Loop.Length>12,"continuous transition and waving loop retained");
            check(clip.Sample(0,true).IsEntry&&!clip.Sample(clip.EntryDurationMs,true).IsEntry,"completion transition is one shot");
            check(!clip.Sample(clip.EntryDurationMs+clip.LoopDurationMs*10000,true).IsEntry,"long completion never reintroduces keyboard");
            check(clip.Sample(clip.EntryDurationMs+111,true).Key==clip.Sample(clip.EntryDurationMs+clip.LoopDurationMs*42+111,true).Key,"completion loop cadence stable indefinitely");
            check(!clip.Sample(0,false).IsEntry&&clip.Sample(100000,false).Index==0,"reduced motion uses no-keyboard completion pose");
            foreach(var item in clip.Entry.Concat(clip.Loop)){
                bool entry=clip.Entry.Contains(item);var pose=new WorkPose{IsEntry=entry,Index=Array.IndexOf(entry?clip.Entry:clip.Loop,item),File=item.File};
                var bitmap=art.CompletionFrame(pose);
                check(bitmap.PixelWidth==clip.Width&&bitmap.PixelHeight==clip.Height,"completion dimensions "+item.File);
                check(object.ReferenceEquals(bitmap,art.CompletionFrame(pose)),"completion cache "+item.File);
            }
            var initial=new CroppedBitmap(art.CompletionFrame(clip.Sample(0,true)),new System.Windows.Int32Rect(16,12,272,248));
            var workPose=new WorkPose{IsEntry=false,Index=clip.HandoffWorkIndex,File=art.Work.Loop[clip.HandoffWorkIndex].File};
            check(Tests.Pixels(initial).SequenceEqual(Tests.Pixels(art.WorkFrame(workPose))),"handoff initial pose pixel-exact to installed work");
            check(!object.ReferenceEquals(art.CompletionFrame(clip.Sample(0,true)),art.WorkFrame(art.Work.Sample(0,true))),"work and completion caches do not collide");
            check(clip.Recovery!=null&&clip.Recovery.Length>clip.Loop.Length,"completion has a continuous bounded forward recovery track");
            var neutral=new WriteableBitmap(clip.Width,clip.Height,96,96,System.Windows.Media.PixelFormats.Bgra32,null);
            var idlePixels=new FormatConvertedBitmap(art.Frame(0,0,0),System.Windows.Media.PixelFormats.Bgra32,null,0);var small=new byte[192*208*4];idlePixels.CopyPixels(small,192*4,0);
            neutral.WritePixels(new System.Windows.Int32Rect(40,12,192,208),small,192*4,0);
            var final=art.CompletionFrame(new WorkPose{File=clip.Recovery.Last().File,Index=clip.Recovery.Length-1});
            check(Tests.Pixels(final).SequenceEqual(Tests.Pixels(neutral)),"return track ends pixel-exactly at real idle with identical canvas registration");
            check(clip.Loop.Last().File=="native-086.png"&&clip.Loop.First().File=="native-087.png","waiting loop boundary follows adjacent native source frames");
            check(clip.Loop.All(f=>f.DurationMs>=41&&f.DurationMs<=42),"native tail cadence has no artificial endpoint pause");
            check(Enumerable.Range(63,54).All(n=>clip.Loop.Any(f=>f.File=="native-"+n.ToString("000")+".png")),"native right swing and left swing are both retained");
            check(!clip.Entry.Concat(clip.Loop).Any(f=>f.File.Contains("tail-return")||f.File.Contains("tail-reset")),"no reversed tail aliases in entry or waiting loop");
            var original=Tests.Pixels(neutral);
            foreach(var f in clip.Loop.Concat(clip.Recovery).GroupBy(f=>f.File).Select(g=>g.First())){
                var actual=Tests.Pixels(art.CompletionFrame(new WorkPose{File=f.File}));bool intact=true;
                for(int y=0;y<112&&intact;y++)for(int x=98;x<250;x++){
                    int p=(y*clip.Width+x)*4;
                    if(original[p+3]>=220&&(actual[p]!=original[p]||actual[p+1]!=original[p+1]||actual[p+2]!=original[p+2]||actual[p+3]!=original[p+3])){intact=false;break;}
                    if(original[p+3]>0&&actual[p+3]<original[p+3]){intact=false;break;}
                }
                check(intact,"original head and BOTH ears remain in front of moving tail: "+f.File);
            }
            foreach(var f in clip.Entry.Concat(clip.Loop)){
                var player=new CompletionReturn(clip);bool entry=clip.Entry.Contains(f);var displayed=new WorkPose{File=f.File,IsEntry=entry};
                check(player.Begin(displayed,100,true),"return accepts every visible completion phase: "+f.File);
                check(player.Sample(100).File==f.File,"close begins on the exact displayed frame, not a new loop: "+f.File);
                int before=-1;for(int step=0;step<=20;step++){var p=player.Sample(100+player.Duration*step/20);check(p.Index>=before,"recovery source index is forward-only: "+f.File);before=p.Index;}
                check(!player.Active&&before==(entry?clip.EntryRecovery:clip.Recovery).Length-1,"every return finishes at exact idle: "+f.File);
                check(player.Duration<=(entry?2800:1600),"return duration bounded for phase: "+f.File);
            }
            var cancelled=new CompletionReturn(clip);check(!cancelled.Begin(clip.Sample(0,false),0,false),"reduced motion does not add a recovery delay");
            cancelled.Begin(clip.Sample(clip.EntryDurationMs+100,true),0,true);cancelled.Cancel();check(!cancelled.Active&&cancelled.Sample(10)==null,"new work or dragging can cancel completion recovery immediately");
        }
    }
}
