using System;
using System.IO;
using System.Linq;

namespace DesktopKitten {
    public static class PopupPreferencesTests {
        public static void Run(Action<bool,string> check){
            var local=new DateTime(2026,10,29,12,0,0);var utc=local.ToUniversalTime();
            string folder=Path.Combine(Path.GetTempPath(),"kitten-popup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try{
                var store=new Store(folder);
                File.WriteAllText(store.FilePath,"{\"SchemaVersion\":1,\"WaterMinutes\":47,\"MonitorCodex\":false}");
                var legacy=store.Load();
                check(legacy.ShowCompletionPopup&&legacy.ShowTaskPopup&&legacy.ShowReminderPopup&&legacy.WaterMinutes==47&&!legacy.MonitorCodex&&store.Warning==null,"old settings default all popup switches on without resetting existing preferences");
                for(int mask=0;mask<8;mask++){
                    var s=new Settings{ShowCompletionPopup=(mask&1)!=0,ShowTaskPopup=(mask&2)!=0,ShowReminderPopup=(mask&4)!=0,WaterIntervalEnabled=false,MealEnabled=false};
                    var scheduler=new Scheduler(s,local,utc);var completion=new CompletionSession();completion.Preview(0);completion.TryPresentPrompt(new WorkPose());
                    var ordinary=new Reminder{Key="ordinary",Kind="custom",Title="ordinary",Due=local.AddMinutes(-1)};
                    var important=new Reminder{Key="important",Kind="custom",Title="important",Due=local,AllowDuringQuiet=true};
                    s.Pending.Add(ordinary);s.Pending.Add(important);
                    var notice=NoticePresentation.Select(scheduler,completion,local,utc,s);
                    var expected=s.ShowReminderPopup?NoticeKind.Reminder:s.ShowCompletionPopup?NoticeKind.Completion:NoticeKind.None;
                    check(notice.Kind==expected&&(!s.ShowReminderPopup||notice.Reminder==important),"independent arbitration for all popup combinations "+mask);
                    check(scheduler.Current(local,utc)==important&&s.Pending.Count==2&&s.ReminderHistory.Count==0&&scheduler.Today(local,utc).Count==2&&completion.Active,"hidden popups never consume important/ordinary tasks or completion "+mask);
                    s.Pending.Remove(important);s.QuietSummaryPending=true;
                    check(NoticePresentation.Select(scheduler,completion,local,utc,s).Kind==(s.ShowReminderPopup?NoticeKind.Summary:s.ShowCompletionPopup?NoticeKind.Completion:NoticeKind.None),"hidden quiet summary cannot starve enabled completion "+mask);
                    s.Pending.Clear();check(NoticePresentation.Select(scheduler,completion,local,utc,s).Kind==(s.ShowCompletionPopup?NoticeKind.Completion:NoticeKind.None),"completion toggle is independent of task and calendar toggles "+mask);
                    store.Save(s);var loaded=store.Load();
                    check(loaded.ShowCompletionPopup==s.ShowCompletionPopup&&loaded.ShowTaskPopup==s.ShowTaskPopup&&loaded.ShowReminderPopup==s.ShowReminderPopup,"every preference combination survives restart "+mask);
                    completion.Dismiss();s.ShowCompletionPopup=true;check(NoticePresentation.Select(scheduler,completion,local,utc,s).Kind==NoticeKind.None,"re-enabling completion never resurrects acknowledged turns "+mask);
                }
            }finally{foreach(var path in Directory.GetFiles(folder))File.Delete(path);Directory.Delete(folder);}
            var attention=new CompletionAttention();
            check(!attention.Observe(1,false,true,false,0)&&!attention.Observe(1,true,true,false,1),"pointer already over cat at completion never acknowledges automatically");
            check(!attention.Observe(1,true,true,false,60000),"pre-existing hover never starts an automatic completion timeout");
            attention.Observe(1,true,false,false,60001);
            check(!attention.Observe(1,true,true,false,60002),"fresh entry starts 300ms dwell without dismissing immediately");
            check(!attention.Observe(1,true,true,false,60301),"299ms hover is not enough to acknowledge completion");
            check(attention.Observe(1,true,true,false,60302),"exactly 300ms fresh hover acknowledges the hidden completion note");
            check(!attention.Observe(1,true,true,false,62002),"one completed hover produces only one acknowledgement");
            attention.Observe(2,true,false,false,0);attention.Observe(2,true,true,false,1);
            check(!attention.Observe(3,true,true,false,1001)&&!attention.Observe(3,true,true,false,2001),"a fresh completion cancels an old hover deadline and resets existing-hover baseline");
            attention.Observe(3,true,false,false,2002);attention.Observe(3,true,true,false,2003);attention.Observe(3,true,false,false,2150);
            check(!attention.Observe(3,true,true,false,2303)&&!attention.Observe(3,true,true,false,2453)&&attention.Observe(3,true,true,false,2603),"leaving before 300ms cancels and a later entry starts a full new dwell");
            attention.Observe(4,true,false,true,0);attention.Observe(4,true,true,true,1);
            check(!attention.Observe(4,true,true,true,1001),"enabled completion popup retains explicit acknowledgement behavior beyond hover delay");
            attention.Observe(4,true,false,false,1100);attention.Observe(4,true,true,false,1200);attention.Observe(4,true,true,true,1300);
            check(!attention.Observe(4,true,true,false,2300),"enabling popup during hover cancels the deferred hidden acknowledgement");
            attention.Observe(5,true,false,false,0);attention.Observe(5,true,true,false,1,false);
            check(!attention.Observe(5,true,true,false,1001,false),"moving tail under a stationary cursor never starts hover dwell");
            attention.Observe(6,false,false,false,0);attention.Observe(6,false,true,false,1);
            check(!attention.Observe(6,true,true,false,1001),"entry before first displayed completion frame does not arm a delayed acknowledgement");
        }
    }
}
