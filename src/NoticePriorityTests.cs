using System;
using System.IO;
using System.Linq;

namespace DesktopKitten {
    public static class NoticePriorityTests {
        static Settings Empty(){return new Settings{WaterIntervalEnabled=false,WaterTimesEnabled=false,MealEnabled=false};}
        static Reminder Item(string key,DateTime due,bool important=false){return new Reminder{Key=key,Kind="custom",Title=key,Due=due,AllowDuringQuiet=important};}
        public static void Run(Action<bool,string> check){
            var local=new DateTime(2026,10,23,12,0,0);var utc=new DateTime(2026,10,23,4,0,0,DateTimeKind.Utc);
            var s=Empty();var scheduler=new Scheduler(s,local,utc);var completion=new CompletionSession();
            check(NoticePresentation.Select(scheduler,completion,local,utc).Kind==NoticeKind.None,"no empty or synthetic notices");
            completion.Preview(0);check(NoticePresentation.Select(scheduler,completion,local,utc).Kind==NoticeKind.None,"completion still honors its visual handoff when no reminders are ready");
            var ordinary=Item("ordinary",local.AddHours(-1));s.Pending.Add(ordinary);
            check(NoticePresentation.Select(scheduler,completion,local,utc).Reminder==ordinary,"ready calendar reminder bypasses unfinished task-animation handoff");
            completion.TryPresentPrompt(new WorkPose());
            check(NoticePresentation.Select(scheduler,completion,local,utc).Reminder==ordinary&&completion.Active&&completion.PromptReady,"unacknowledged completion cannot starve ordinary calendar reminders");
            var important=Item("important",local,true);s.Pending.Add(important);
            var second=Item("second-important",local,true);s.Pending.Add(second);
            check(scheduler.Current(local,utc)==important,"new important note preempts a previously displayed older ordinary note");
            check(scheduler.Today(local,utc).First().Reminder==important,"today list puts important pending notes first");
            for(int i=0;i<5;i++)check(NoticePresentation.Select(scheduler,completion,local,utc).Reminder==important,"equal-priority notices have stable FIFO order without rotating");
            check(s.Pending[0]==ordinary&&s.Pending[1]==important,"presentation ordering never rewrites the stored queue");
            scheduler.Snooze(important.Key,10,utc);check(scheduler.Current(local,utc)==second,"snoozed importance never defeats an explicit snooze");
            scheduler.Resolve(second.Key,"skipped",utc);check(scheduler.Current(local,utc)==ordinary,"resolving highest priority resumes the original ordinary occurrence");
            scheduler.Resolve(ordinary.Key,"done",utc);check(NoticePresentation.Select(scheduler,completion,local,utc).Kind==NoticeKind.Completion&&completion.Active,"completion resumes without duplicating or consuming snoozed reminders");
            check(scheduler.Tick(local.AddMinutes(10),utc.AddMinutes(10))&&scheduler.Current(local.AddMinutes(10),utc.AddMinutes(10))==important,"important snooze expiry preempts a waiting completion exactly when ready");
            QuietPolicy.Start(s,utc.AddHours(1),utc);check(scheduler.Current(local.AddMinutes(10),utc.AddMinutes(10))==important,"important reminder bypasses quiet hours as well as task notices");

            s=Empty();scheduler=new Scheduler(s,local,utc);
            var rule=new ScheduleRule{Title="existing interval",Mode="interval",NextUtc=utc.AddHours(1),LastFiredLocal=local.AddMinutes(-1)};s.CustomSchedules.Add(rule);
            var pending=Item("existing",local);pending.ScheduleId=rule.Id;s.Pending.Add(pending);
            check(scheduler.SetImportance(pending.Key,true,local,utc)&&pending.AllowDuringQuiet&&rule.AllowDuringQuiet,"importance edit synchronizes current occurrence and future custom rule");
            check(rule.NextUtc==utc.AddHours(1)&&rule.LastFiredLocal==local.AddMinutes(-1),"importance-only edits never reset recurring schedule clocks");
            scheduler.Snooze(pending.Key,30,utc);scheduler.SetImportance(pending.Key,false,local,utc);
            check(!rule.AllowDuringQuiet&&pending.SnoozeUntilUtc==utc.AddMinutes(30),"demotion preserves snooze time");
            check(!scheduler.SetImportance("stale",true,local,utc),"stale importance command cannot change a different task");
            scheduler.Resolve(pending.Key,"done",utc);
            var plan=scheduler.Today(local,utc).Single(x=>x.Upcoming);
            check(scheduler.SetImportance(plan.Reminder.Key,true,local,utc)&&rule.AllowDuringQuiet&&rule.NextUtc==utc.AddHours(1),"upcoming importance edit changes no scheduling dates");
            scheduler.Tick(local.AddHours(1),utc.AddHours(1));check(s.Pending.Single().AllowDuringQuiet,"future occurrence inherits importance");
            var ruleCopy=rule.Copy();check(ruleCopy.AllowDuringQuiet&&ruleCopy.NextUtc==rule.NextUtc,"rule copy retains priority and exact clocks");

            var folder=Path.Combine(Path.GetTempPath(),"grassfish-priority-"+Guid.NewGuid().ToString("N"));
            try{var store=new Store(folder);store.Save(s);var restored=store.Load();check(restored.CustomSchedules.Single().AllowDuringQuiet&&restored.Pending.Single().AllowDuringQuiet,"legacy importance field roundtrips as the single source of priority");}
            finally{if(Directory.Exists(folder)){foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}}

            s=Empty();scheduler=new Scheduler(s,local,utc);s.Pending.Add(Item("summary-A",local));s.Pending.Add(Item("summary-B",local));
            QuietPolicy.Start(s,utc.AddMinutes(30),utc);scheduler.PrepareQuietResume(local,utc);QuietPolicy.Resume(s,local,utc);
            check(NoticePresentation.Select(scheduler,completion,local,utc).Kind==NoticeKind.Summary,"quiet summary also outranks a completion notice");
            important=Item("urgent-over-summary",local,true);s.Pending.Add(important);
            check(NoticePresentation.Select(scheduler,completion,local,utc).Reminder==important,"important reminder outranks a merged quiet summary");
            var deferred=Item("deferred",local);deferred.SnoozeUntilUtc=utc.AddMinutes(20);s.Pending.Add(deferred);
            scheduler.SkipSummary(utc);check(s.Pending.Count==2&&s.Pending.Contains(important)&&s.Pending.Contains(deferred)&&s.ReminderHistory.All(x=>x.Outcome=="skipped"),"summary skip touches only ready ordinary notes, never important or snoozed ones");
        }
    }
}
