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
            check(clip.Width==272&&clip.Height==272&&clip.OriginX==-24&&clip.OriginY==-12,"square completion padding preserves registration and ears");
            check(clip.HandoffWorkIndex==art.Work.Loop.Length-1,"handoff matches reference keyboard pose");
            check(clip.Entry.Length>10&&clip.Loop.Length>12,"continuous transition and waving loop retained");
            check(clip.Sample(0,true).IsEntry&&!clip.Sample(clip.EntryDurationMs,true).IsEntry,"completion transition is one shot");
            check(!clip.Sample(clip.EntryDurationMs+clip.LoopDurationMs*10000,true).IsEntry,"long completion never reintroduces keyboard");
            check(clip.Sample(clip.EntryDurationMs+111,true).Key==clip.Sample(clip.EntryDurationMs+clip.LoopDurationMs*42+111,true).Key,"completion loop cadence stable indefinitely");
            check(!clip.Sample(0,false).IsEntry&&clip.Sample(100000,false).Index==0,"reduced motion uses no-keyboard completion pose");
            foreach(var item in clip.Entry.Concat(clip.Loop)){
                bool entry=clip.Entry.Contains(item);var pose=new WorkPose{IsEntry=entry,Index=Array.IndexOf(entry?clip.Entry:clip.Loop,item),File=item.File};
                var bitmap=art.CompletionFrame(pose);
                check(bitmap.PixelWidth==272&&bitmap.PixelHeight==272,"completion dimensions "+item.File);
                check(object.ReferenceEquals(bitmap,art.CompletionFrame(pose)),"completion cache "+item.File);
            }
            var initial=new CroppedBitmap(art.CompletionFrame(clip.Sample(0,true)),new System.Windows.Int32Rect(0,12,272,248));
            var workPose=new WorkPose{IsEntry=false,Index=clip.HandoffWorkIndex,File=art.Work.Loop[clip.HandoffWorkIndex].File};
            check(Tests.Pixels(initial).SequenceEqual(Tests.Pixels(art.WorkFrame(workPose))),"handoff initial pose pixel-exact to installed work");
            check(!object.ReferenceEquals(art.CompletionFrame(clip.Sample(0,true)),art.WorkFrame(art.Work.Sample(0,true))),"work and completion caches do not collide");
        }
    }
}
