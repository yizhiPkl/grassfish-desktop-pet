using System;
using System.IO;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Controls;

namespace DesktopKitten {
    public static class ReminderWorkflowTests {
        static Settings Empty(){return new Settings{WaterIntervalEnabled=false,WaterTimesEnabled=false,MealEnabled=false};}
        static Reminder Item(string key,DateTime due){return new Reminder{Key=key,Kind="custom",Title="测试小事",Message="保留原提醒内容",Due=due};}
        public static void Run(Action<bool,string> check){
            var local=new DateTime(2026,10,15,12,0,0);var utc=new DateTime(2026,10,15,4,0,0,DateTimeKind.Utc);
            var s=Empty();s.Pending.Add(Item("A",local));s.Pending.Add(Item("B",local));var scheduler=new Scheduler(s,local,utc);
            check(scheduler.Snooze("A",10,utc)&&s.Pending.Count==2,"snooze retains original occurrence");
            check(scheduler.Current(local,utc).Key=="B","snoozed first item does not block ready reminders");
            check(!scheduler.Snooze("A",0,utc)&&!scheduler.Snooze("A",1441,utc)&&!scheduler.Snooze("missing",10,utc),"invalid and stale snooze rejected");
            check(scheduler.Resolve("B","done",utc)&&s.ReminderHistory.Single().Outcome=="done","complete records only selected item");
            check(!scheduler.Resolve("B","done",utc)&&!scheduler.Resolve("A","invalid",utc),"duplicate and unsupported outcomes rejected");
            check(scheduler.Current(local,utc)==null,"all snoozed items remain silent");
            scheduler.Tick(local.AddMinutes(1),utc.AddMinutes(1));
            check(scheduler.Tick(local.AddMinutes(10),utc.AddMinutes(10))&&scheduler.Current(local.AddMinutes(10),utc.AddMinutes(10)).Key=="A","snooze expiry refreshes presentation without a new scheduled event");
            check(scheduler.Resolve("A","closed",utc.AddMinutes(10))&&s.ReminderHistory.Last().Outcome=="closed","close is not completion or skip");
            check(scheduler.Restore("A")&&s.Pending.Single().Message=="保留原提醒内容","closed reminder can be restored with exact original body");
            check(scheduler.Resolve("A","skipped",utc.AddMinutes(11))&&!scheduler.Restore("A"),"skip is one occurrence and is not automatically restored");

            s=Empty();s.MealEnabled=true;s.MealTimes="12:30";s.CustomSchedules.Add(new ScheduleRule{Title="提前完成的约定",Mode="once",DueLocal=local.AddHours(1)});scheduler=new Scheduler(s,local,utc);
            var planned=scheduler.Today(local,utc);check(scheduler.ResolvePlanned(planned.Single(x=>x.Reminder.Kind=="meal").Reminder.Key,"done",local,utc),"upcoming meal can be completed early in today list");
            check(scheduler.ResolvePlanned(planned.Single(x=>x.Reminder.Kind=="custom").Reminder.Key,"skipped",local,utc)&&s.CustomSchedules.Single().Completed,"upcoming one-shot skip affects only that occurrence");
            scheduler.Tick(local.AddHours(2),utc.AddHours(2));check(s.Pending.Count==0&&s.ReminderHistory.Count==2,"early agenda actions suppress their eventual duplicate popup");
            check(!scheduler.ResolvePlanned("stale","done",local,utc),"stale upcoming row cannot create or complete a different item");

            s=Empty();s.WaterIntervalEnabled=true;s.WaterMinutes=30;scheduler=new Scheduler(s,local,utc);var next=s.WaterNextUtc;
            check(scheduler.ResolvePlanned(scheduler.Today(local,utc).Single().Reminder.Key,"done",local,utc)&&s.WaterNextUtc==next.AddMinutes(30),"early interval completion advances to following scheduled occurrence");

            s=Empty();s.WaterTimesEnabled=true;s.WaterTimes="12:01";s.MealEnabled=true;s.MealTimes="12:02";scheduler=new Scheduler(s,local,utc);
            check(scheduler.Today(local,utc).Count==2&&scheduler.Today(local,utc).All(x=>x.Upcoming),"today includes upcoming fixed reminders");
            scheduler.Tick(local.AddMinutes(1),utc.AddMinutes(1));check(s.Pending.Single().Title=="该喝水了喵","water wording exact");
            var water=s.Pending[0];scheduler.Snooze(water.Key,30,utc.AddMinutes(1));scheduler.Tick(local.AddMinutes(2),utc.AddMinutes(2));check(scheduler.Current(local.AddMinutes(2),utc.AddMinutes(2)).Title=="该吃饭了喵","meal wording exact and skips deferred water");
            check(s.WaterTimes=="12:01"&&s.MealTimes=="12:02","snooze does not edit recurring rules");
            scheduler.Resolve(s.Pending.Last().Key,"skipped",utc.AddMinutes(2));scheduler.Tick(local.AddDays(1).AddMinutes(2),utc.AddDays(1).AddMinutes(2));check(s.Pending.Any(r=>r.Kind=="meal"),"skipping one meal does not disable tomorrow");

            s=Empty();var rule=new ScheduleRule{Title="会议",Time="12:01"};s.CustomSchedules.Add(rule);scheduler=new Scheduler(s,local,utc);scheduler.Tick(local.AddMinutes(1),utc.AddMinutes(1));var key=s.Pending.Single().Key;
            scheduler.Snooze(key,60,utc.AddMinutes(1));scheduler.Tick(local.AddDays(1),utc.AddDays(1));check(s.Pending.Count==1,"custom snooze does not create duplicate outstanding occurrences");
            scheduler.Resolve(key,"done",utc.AddDays(1));check(rule.Enabled&&!rule.Completed,"completing an occurrence does not disable repeating rule");scheduler.Tick(local.AddDays(1).AddMinutes(2),utc.AddDays(1).AddMinutes(2));check(s.Pending.Count==1,"next recurring custom occurrence still fires");

            s=Empty();s.Pending.Add(Item("ordinary",local));var important=Item("important",local);important.AllowDuringQuiet=true;s.Pending.Add(important);scheduler=new Scheduler(s,local,utc);
            QuietPolicy.Start(s,utc.AddMinutes(30),utc);check(scheduler.Current(local,utc).Key=="important","important exception can pass manual quiet policy");
            scheduler.Resolve("important","done",utc);check(scheduler.Current(local,utc)==null&&s.Pending.Count==1,"quiet mode queues rather than deletes ordinary reminders");
            scheduler.Tick(local,utc);check(!scheduler.Tick(local.AddMinutes(29),utc.AddMinutes(29)),"quiet mode stable before exact expiry");
            check(scheduler.Tick(local.AddMinutes(30),utc.AddMinutes(30))&&scheduler.Current(local.AddMinutes(30),utc.AddMinutes(30)).Key=="ordinary","quiet expiry wakes queued item without a new reminder event");
            QuietPolicy.Start(s,utc.AddMinutes(60),utc);scheduler.AddPreview("water");check(ReminderWorkflow.IsPreview(scheduler.Current(local,utc)),"explicit preview remains available during quiet mode");scheduler.Dismiss();
            bool invalid=false;try{QuietPolicy.Start(s,utc,utc);}catch(FormatException){invalid=true;}check(invalid,"past quiet end rejected");

            s=Empty();s.QuietDailyEnabled=true;s.QuietStart="22:00";s.QuietEnd="08:00";
            check(!QuietPolicy.IsQuiet(s,local.Date.AddHours(21),utc.Date.AddHours(13)),"overnight quiet waits until start");
            var lateLocal=local.Date.AddHours(22);var lateUtc=utc.Date.AddHours(14);
            check(QuietPolicy.IsQuiet(s,lateLocal,lateUtc)&&QuietPolicy.Until(s,lateLocal,lateUtc)==lateUtc.AddHours(10),"overnight quiet includes exact start and correct next-morning end");
            check(QuietPolicy.IsQuiet(s,local.Date.AddDays(1).AddHours(7),utc.Date.AddHours(23)),"overnight quiet remains active after midnight");
            check(!QuietPolicy.IsQuiet(s,local.Date.AddDays(1).AddHours(8),utc.Date.AddDays(1)),"daily quiet excludes exact end");
            QuietPolicy.Resume(s,lateLocal,lateUtc);check(!QuietPolicy.IsQuiet(s,lateLocal,lateUtc)&&s.QuietDailyEnabled,"resume temporarily bypasses current daily quiet without deleting rule");
            check(QuietPolicy.IsQuiet(s,lateLocal.AddDays(1),lateUtc.AddDays(1)),"daily quiet returns on next scheduled night");
            s.QuietStart="12:00";s.QuietEnd="13:00";s.QuietBypassUntilUtc=DateTime.MinValue;check(QuietPolicy.IsQuiet(s,local,utc)&&!QuietPolicy.IsQuiet(s,local.AddHours(1),utc.AddHours(1)),"same-day quiet boundaries");
            invalid=false;try{QuietPolicy.Validate("09:00","09:00");}catch(FormatException){invalid=true;}check(invalid,"equal quiet endpoints rejected");
            invalid=false;try{QuietPolicy.Validate("25:00","08:00");}catch(FormatException){invalid=true;}check(invalid,"invalid quiet times rejected");

            s=Empty();s.Pending.Add(Item("quiet-A",local));s.Pending.Add(Item("quiet-B",local));QuietPolicy.Start(s,utc.AddMinutes(30),utc);scheduler=new Scheduler(s,local,utc);
            check(scheduler.Tick(local.AddMinutes(30),utc.AddMinutes(30))&&scheduler.SummaryVisible(local.AddMinutes(30),utc.AddMinutes(30)),"multiple deferred notes merge into one summary after natural quiet expiry");
            scheduler.CloseSummary(utc.AddMinutes(30));check(s.Pending.Count==0&&s.ReminderHistory.Count==2&&s.ReminderHistory.All(r=>r.Outcome=="closed"),"closing merged summary archives rather than completes its notes");
            scheduler.Restore("quiet-A");scheduler.Restore("quiet-B");QuietPolicy.Start(s,utc.AddMinutes(60),utc);scheduler.PrepareQuietResume(local,utc);QuietPolicy.Resume(s,local,utc);check(scheduler.SummaryVisible(local,utc),"manual resume also merges accumulated notes");
            scheduler.SnoozeSummary(utc);check(s.Pending.Count==2&&s.Pending.All(r=>r.SnoozeUntilUtc==utc.AddMinutes(10)),"merged snooze preserves every occurrence without editing schedules");
            check(scheduler.SummaryVisible(local.AddMinutes(10),utc.AddMinutes(10)),"deferred summary returns as one summary rather than separate popups");
            scheduler.Resolve("quiet-A","done",utc);scheduler.Resolve("quiet-B","done",utc);check(!s.QuietSummaryPending,"handling last summarized item cannot label an unrelated future reminder as quiet summary");

            s=Empty();scheduler=new Scheduler(s,local,utc);for(int i=0;i<240;i++){s.Pending.Add(Item("history-"+i,local));scheduler.Resolve("history-"+i,"done",utc.AddSeconds(i));}
            check(s.ReminderHistory.Count==200,"history has a bounded memory and disk footprint");
            check(scheduler.Today(local,utc).Count(x=>x.State=="done")==200,"today projects completed history without duplicate planned entries");
            scheduler.AddPreview("water");var previewKey=s.Pending.Single().Key;scheduler.Resolve(previewKey,"done",utc);check(s.ReminderHistory.Count==200,"preview does not pollute completion history");

            s=Empty();s.Pending.Add(Item("overdue-pending",local.AddDays(-30)));var deferred=Item("overdue-snoozed",local.AddDays(-30));deferred.SnoozeUntilUtc=utc.AddDays(1);s.Pending.Add(deferred);
            s.ReminderHistory.Add(new Reminder{Key="expired",HandledUtc=utc.AddDays(-8),Due=local.AddDays(-8),Outcome="done"});
            s.ReminderHistory.Add(new Reminder{Key="boundary",HandledUtc=utc.AddDays(-7),Due=local.AddDays(-7),Outcome="closed"});
            scheduler=new Scheduler(s,local,utc);check(scheduler.Tick(local,utc)&&s.ReminderHistory.Count==1&&s.ReminderHistory[0].Key=="boundary","first tick after startup prunes older-than-seven-day handled records, retains exact boundary");
            check(s.Pending.Count==2&&s.Pending.Any(r=>r.Key=="overdue-snoozed"),"seven-day retention never deletes overdue pending or snoozed reminders");
            check(scheduler.Today(local,utc).Any(r=>r.Reminder.Key=="overdue-pending"),"today includes unresolved previous-day reminders");
            scheduler.Tick(local.AddDays(1),utc.AddDays(1));check(s.ReminderHistory.Count==0,"next UTC day triggers retention cleanup even without new resolutions");

            string directory=Path.Combine(Path.GetTempPath(),"grassfish-reminder-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{
                var store=new Store(directory);s=Empty();s.Pending.Add(Item("persisted",local));scheduler=new Scheduler(s,local,utc);scheduler.Snooze("persisted",30,utc);QuietPolicy.Start(s,utc.AddHours(1),utc);s.QuietDailyEnabled=true;s.CustomSchedules.Add(new ScheduleRule{Title="重要",AllowDuringQuiet=true});store.Save(s);var restored=store.Load();
                check(restored.Pending.Single().SnoozeUntilUtc==utc.AddMinutes(30)&&restored.Pending.Single().Due==local,"snooze UTC and original local due survive restart");
                check(restored.QuietUntilUtc==utc.AddHours(1)&&restored.CustomSchedules[0].AllowDuringQuiet,"quiet policy and importance survive restart");
                scheduler=new Scheduler(restored,local,utc);check(scheduler.Current(local,utc)==null,"restart cannot bypass snooze or quiet interval");
                scheduler.Resolve("persisted","closed",utc);store.Save(restored);restored=store.Load();check(restored.ReminderHistory.Single().Outcome=="closed"&&restored.ReminderHistory.Single().Message=="保留原提醒内容","closed state survives restart with restorable body");
                File.WriteAllText(store.FilePath,"{\"SchemaVersion\":1,\"Pending\":[{\"Key\":\"old\",\"Kind\":\"water\",\"Title\":\"喝水时间到啦\"}],\"WaterMinutes\":47}");restored=store.Load();check(restored.WaterMinutes==47&&restored.Pending[0].Title=="该喝水了喵"&&restored.ReminderHistory.Count==0&&!restored.QuietDailyEnabled,"legacy settings migrate without losing reminder rules");
                restored.QuietDailyEnabled=true;restored.QuietStart="bad";store.Save(restored);restored=store.Load();check(!restored.QuietDailyEnabled&&restored.WaterMinutes==47,"bad quiet settings do not reset other personal settings");
            }finally{foreach(var path in Directory.GetFiles(directory))File.Delete(path);Directory.Delete(directory);}
            var layout=(Grid)XamlReader.Parse(TodayWindow.Layout);check(layout.FindName("Items") is ListBox&&layout.FindName("Quiet30") is Button&&layout.FindName("QuietDaily") is CheckBox,"native today and manual quiet layout parses");
            foreach(var area in new[]{new System.Windows.Rect(0,0,1920,1040),new System.Windows.Rect(-1920,0,1920,1040),new System.Windows.Rect(0,0,1280,680)})
            foreach(double size in new[]{0.5,0.75,1.0})foreach(int corner in new[]{0,1,2,3}){
                var cat=new System.Windows.Rect((corner%2==0?area.Left+10:area.Right-272*size-10),(corner<2?area.Top+10:area.Bottom-272*size-10),272*size,272*size);var pose=PopupPlacement.Place(cat,new System.Windows.Size(284,222),area);
                check(area.Contains(pose.Bounds),"extended local reminder fits monitor corner");var overlap=System.Windows.Rect.Intersect(cat,pose.Bounds);check(overlap.IsEmpty||overlap.Width*overlap.Height==0,"extended action row never covers pet when space is available");
            }
        }
    }
}
