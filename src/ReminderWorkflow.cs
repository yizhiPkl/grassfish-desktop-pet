using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopKitten {
    public static class ReminderWorkflow {
        public static string Title(string kind){return kind=="water"?"该喝水了喵":"该吃饭了喵";}
        public static bool IsPreview(Reminder r){return (r.Key??"").StartsWith("preview:",StringComparison.Ordinal);}
        public static DateTime Local(DateTime value,DateTime local,DateTime utc){return value==DateTime.MinValue?DateTime.MinValue:local.Add(value-utc);}
        public static string State(Reminder r,DateTime utc){return !string.IsNullOrEmpty(r.Outcome)?r.Outcome:r.SnoozeUntilUtc>utc?"snoozed":"pending";}
    }
    public static class QuietPolicy {
        public static void Validate(string start,string end){if(Store.ParseTimes(start).Count!=1||Store.ParseTimes(end).Count!=1||Store.ParseTimes(start)[0]==Store.ParseTimes(end)[0])throw new FormatException("安静时段请填写两个不同的时间，例如22:30和08:00。");}
        static DateTime DailyEnd(Settings s,DateTime local){
            if(!s.QuietDailyEnabled)return DateTime.MinValue;
            var a=Store.ParseTimes(s.QuietStart).Single();var b=Store.ParseTimes(s.QuietEnd).Single();var t=local.TimeOfDay;
            if(a<b)return t>=a&&t<b?local.Date.Add(b):DateTime.MinValue;
            return t>=a?local.Date.AddDays(1).Add(b):t<b?local.Date.Add(b):DateTime.MinValue;
        }
        public static DateTime Until(Settings s,DateTime local,DateTime utc){
            DateTime end=s.QuietUntilUtc>utc?s.QuietUntilUtc:DateTime.MinValue;
            var daily=DailyEnd(s,local);if(daily!=DateTime.MinValue&&s.QuietBypassUntilUtc<=utc){var value=utc.Add(daily-local);if(value>end)end=value;}
            return end;
        }
        public static bool IsQuiet(Settings s,DateTime local,DateTime utc){return Until(s,local,utc)>utc;}
        public static void Start(Settings s,DateTime until,DateTime utc){if(until<=utc||until>utc.AddDays(7))throw new FormatException("请选择未来7天内的结束时间。");s.QuietUntilUtc=until;s.QuietBypassUntilUtc=DateTime.MinValue;}
        public static void Resume(Settings s,DateTime local,DateTime utc){s.QuietUntilUtc=DateTime.MinValue;var end=DailyEnd(s,local);s.QuietBypassUntilUtc=end==DateTime.MinValue?DateTime.MinValue:utc.Add(end-local);}
        public static string Description(Settings s,DateTime local,DateTime utc){var end=Until(s,local,utc);if(end==DateTime.MinValue)return "正常提醒中";var time=ReminderWorkflow.Local(end,local,utc);return "安静到 "+time.ToString(time.Date==local.Date?"HH:mm":"MM月dd日 HH:mm");}
    }
    public sealed class AgendaItem {
        public Reminder Reminder;
        public string State;
        public bool Upcoming;
    }
    // One arbitration point for all paper notes. Calendar reminders must never
    // wait for a Codex acknowledgement or its animation handoff. Selecting a
    // reminder does not consume the completion, so it can resume afterwards.
    public enum NoticeKind { None, Reminder, Summary, Completion }
    public sealed class NoticePresentation {
        public NoticeKind Kind;
        public Reminder Reminder;
        public static NoticePresentation Select(Scheduler scheduler,CompletionSession completion,DateTime local,DateTime utc){
            var reminder=scheduler.Current(local,utc);
            if(reminder!=null)return new NoticePresentation{Kind=!reminder.AllowDuringQuiet&&!ReminderWorkflow.IsPreview(reminder)&&scheduler.SummaryVisible(local,utc)?NoticeKind.Summary:NoticeKind.Reminder,Reminder=reminder};
            return new NoticePresentation{Kind=completion.Active&&completion.PromptReady?NoticeKind.Completion:NoticeKind.None};
        }
    }
    public sealed partial class Scheduler {
        string lastReady;bool lastQuiet;DateTime historyPruneDay=DateTime.MinValue;
        public Reminder Current(DateTime local,DateTime utc){
            bool quiet=QuietPolicy.IsQuiet(settings,local,utc);
            return settings.Pending.Where(r=>r.SnoozeUntilUtc<=utc&&(!quiet||r.AllowDuringQuiet||ReminderWorkflow.IsPreview(r))).OrderByDescending(r=>r.AllowDuringQuiet).ThenBy(r=>r.Due).FirstOrDefault();
        }
        bool PresentationChanged(DateTime local,DateTime utc){var item=Current(local,utc);string key=item==null?null:item.Key;bool quiet=QuietPolicy.IsQuiet(settings,local,utc);bool priorSummary=settings.QuietSummaryPending;if((quiet||lastQuiet)&&SummaryItems(utc).Count>1)settings.QuietSummaryPending=true;bool changed=key!=lastReady||quiet!=lastQuiet||priorSummary!=settings.QuietSummaryPending;lastReady=key;lastQuiet=quiet;return changed;}
        public List<Reminder> SummaryItems(DateTime utc){return settings.Pending.Where(r=>r.SnoozeUntilUtc<=utc&&!r.AllowDuringQuiet&&!ReminderWorkflow.IsPreview(r)).ToList();}
        public bool SummaryVisible(DateTime local,DateTime utc){return settings.QuietSummaryPending&&!QuietPolicy.IsQuiet(settings,local,utc)&&SummaryItems(utc).Count>0;}
        public void PrepareQuietResume(DateTime local,DateTime utc){if(QuietPolicy.IsQuiet(settings,local,utc)&&SummaryItems(utc).Count>1)settings.QuietSummaryPending=true;}
        public void CloseSummary(DateTime utc){foreach(var r in SummaryItems(utc))Resolve(r.Key,"closed",utc);settings.QuietSummaryPending=false;}
        public void SkipSummary(DateTime utc){foreach(var r in SummaryItems(utc))Resolve(r.Key,"skipped",utc);settings.QuietSummaryPending=false;}
        public void SnoozeSummary(DateTime utc){foreach(var r in SummaryItems(utc))Snooze(r.Key,10,utc);}
        public bool Snooze(string key,int minutes,DateTime utc){
            if(minutes<1||minutes>1440)return false;var r=settings.Pending.FirstOrDefault(x=>x.Key==key);if(r==null)return false;
            r.SnoozeUntilUtc=utc.AddMinutes(minutes);return true;
        }
        public bool SetImportance(string key,bool important,DateTime local,DateTime utc){
            var r=settings.Pending.FirstOrDefault(x=>x.Key==key);
            if(r==null){var planned=Today(local,utc).FirstOrDefault(x=>x.Upcoming&&x.Reminder.Key==key);if(planned==null||string.IsNullOrEmpty(planned.Reminder.ScheduleId))return false;r=planned.Reminder;}
            var rule=settings.CustomSchedules.FirstOrDefault(x=>x.Id==r.ScheduleId);
            if(rule!=null){rule.AllowDuringQuiet=important;foreach(var pending in settings.Pending.Where(x=>x.ScheduleId==rule.Id))pending.AllowDuringQuiet=important;}
            else r.AllowDuringQuiet=important;
            return true;
        }
        public bool Resolve(string key,string outcome,DateTime utc){
            if(!new[]{"done","skipped","closed"}.Contains(outcome))return false;
            var r=settings.Pending.FirstOrDefault(x=>x.Key==key);if(r==null)return false;
            settings.Pending.Remove(r);
            if(!settings.Pending.Any(x=>!x.AllowDuringQuiet&&!ReminderWorkflow.IsPreview(x)))settings.QuietSummaryPending=false;
            if(!ReminderWorkflow.IsPreview(r)){
                settings.ReminderHistory.RemoveAll(x=>x.Key==key);
                settings.ReminderHistory.Add(new Reminder{Key=r.Key,Kind=r.Kind,Title=r.Title,Message=outcome=="closed"?r.Message:null,ScheduleId=r.ScheduleId,Due=r.Due,HandledUtc=utc,Outcome=outcome,AllowDuringQuiet=r.AllowDuringQuiet});
                TrimHistory(utc);
            }
            return true;
        }
        public bool ResolvePlanned(string key,string outcome,DateTime local,DateTime utc){
            if(outcome!="done"&&outcome!="skipped")return false;
            var item=Today(local,utc).FirstOrDefault(row=>row.Upcoming&&row.Reminder.Key==key);if(item==null)return false;
            var r=item.Reminder;
            if(r.Kind=="water"&&key.StartsWith("interval:",StringComparison.Ordinal))settings.WaterNextUtc=settings.WaterNextUtc.AddMinutes(settings.WaterMinutes);
            if(!string.IsNullOrEmpty(r.ScheduleId)){
                var rule=settings.CustomSchedules.FirstOrDefault(x=>x.Id==r.ScheduleId&&x.Enabled&&!x.Completed);if(rule==null)return false;
                if(rule.Mode=="once"){rule.Completed=true;rule.Enabled=false;}
                else if(rule.Mode=="interval"){rule.NextUtc=utc.Add(r.Due-local).AddMinutes(rule.Minutes);}
                else rule.LastFiredLocal=r.Due;
            }
            Add(r);return Resolve(r.Key,outcome,utc);
        }
        void TrimHistory(DateTime utc){settings.ReminderHistory.RemoveAll(r=>r.HandledUtc<utc.AddDays(-7));if(settings.ReminderHistory.Count>200)settings.ReminderHistory=settings.ReminderHistory.OrderByDescending(r=>r.HandledUtc).Take(200).OrderBy(r=>r.HandledUtc).ToList();}
        public bool Restore(string key){
            var old=settings.ReminderHistory.FirstOrDefault(r=>r.Key==key&&r.Outcome=="closed");if(old==null||settings.Pending.Any(r=>r.Key==key))return false;
            if(!string.IsNullOrEmpty(old.ScheduleId)&&settings.Pending.Any(r=>r.ScheduleId==old.ScheduleId))return false;
            var rule=settings.CustomSchedules.FirstOrDefault(r=>r.Id==old.ScheduleId);
            settings.Pending.Add(new Reminder{Key=old.Key,Kind=old.Kind,Title=old.Title,Due=old.Due,ScheduleId=old.ScheduleId,AllowDuringQuiet=old.AllowDuringQuiet,Message=!string.IsNullOrEmpty(old.Message)?old.Message:rule!=null&&!string.IsNullOrEmpty(rule.Message)?rule.Message:old.Kind=="water"?"陪小猫喝口水吧，慢慢来就好。":old.Kind=="meal"?"先把忙碌放一放，好好吃一顿饭吧。":"草鱼提醒你：到约定的时间啦。"});
            settings.ReminderHistory.Remove(old);return true;
        }
        public List<AgendaItem> Today(DateTime local,DateTime utc){
            var rows=new Dictionary<string,AgendaItem>();
            Action<Reminder,string,bool> add=(r,state,upcoming)=>{if(!ReminderWorkflow.IsPreview(r)&&!rows.ContainsKey(r.Key))rows[r.Key]=new AgendaItem{Reminder=r,State=state,Upcoming=upcoming};};
            foreach(var r in settings.Pending)add(r,ReminderWorkflow.State(r,utc),false);
            foreach(var r in settings.ReminderHistory.Where(r=>r.Due.Date==local.Date||ReminderWorkflow.Local(r.HandledUtc,local,utc).Date==local.Date))add(r,r.Outcome,false);
            Action<Reminder> plan=r=>{if(r.Due>local&&r.Due.Date==local.Date&&!settings.Fired.Contains(r.Key))add(r,"upcoming",true);};
            if(settings.WaterIntervalEnabled)plan(new Reminder{Key="interval:"+settings.WaterNextUtc.ToString("o"),Kind="water",Title=ReminderWorkflow.Title("water"),Due=ReminderWorkflow.Local(settings.WaterNextUtc,local,utc)});
            foreach(string kind in new[]{"water","meal"})if(kind=="water"?settings.WaterTimesEnabled:settings.MealEnabled)foreach(var time in Store.ParseTimes(kind=="water"?settings.WaterTimes:settings.MealTimes)){
                var due=local.Date.Add(time);plan(new Reminder{Key=kind+":"+due.ToString("yyyy-MM-ddTHH:mm"),Kind=kind,Title=ReminderWorkflow.Title(kind),Due=due});
            }
            foreach(var rule in settings.CustomSchedules.Where(r=>r.Enabled&&!r.Completed&&!settings.Pending.Any(p=>p.ScheduleId==r.Id))){
                DateTime due;
                if(rule.Mode=="once")due=rule.DueLocal;
                else if(rule.Mode=="interval"){if(rule.NextUtc==DateTime.MinValue)due=local.AddMinutes(rule.Minutes);else due=ReminderWorkflow.Local(rule.NextUtc,local,utc);}
                else{if(rule.Mode=="weekly"&&!rule.Days.Contains((int)local.DayOfWeek))continue;due=local.Date.Add(Store.ParseTimes(rule.Time).Single());if(due<=rule.LastFiredLocal)continue;}
                string key=rule.Mode=="interval"?"upcoming-interval:"+rule.Id+":"+rule.NextUtc.Ticks:"custom:"+rule.Id+":"+due.ToString("yyyy-MM-ddTHH:mm:ss")+":";
                plan(new Reminder{Key=key,Kind="custom",Title=rule.Title,Message=rule.Message,ScheduleId=rule.Id,Due=due,AllowDuringQuiet=rule.AllowDuringQuiet});
            }
            return rows.Values.OrderBy(r=>r.State=="pending"?0:r.State=="snoozed"?1:r.Upcoming?2:3).ThenByDescending(r=>r.Reminder.AllowDuringQuiet).ThenBy(r=>r.State=="snoozed"?r.Reminder.SnoozeUntilUtc:r.Reminder.Due).ToList();
        }
    }
}
