using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopKitten {
    public static class ReminderUiTests {
        static T Find<T>(FrameworkElement root,string name) where T:class{return root.FindName(name) as T;}
        static void Click(FrameworkElement root,string name){Find<Button>(root,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        public static void Run(KittenApp app,string output){
            Directory.CreateDirectory(output);var checks=new List<string>();Exception failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
            try{
                check(app.Qa&&app.HeadlessQa,"headless isolated profile, no desktop UI automation");
                var menuLabels=app.Pet.ContextMenu.Items.OfType<MenuItem>().Select(item=>Convert.ToString(item.Header)).ToArray();
                check(!menuLabels.Any(label=>label.Contains("预览")),"cat right-click menu contains no reminder/completion previews");
                check(menuLabels.Contains("今日小事")&&!app.Pet.ContextMenu.StaysOpen,"right-click menu preserves normal commands with outside dismissal enabled");
                var cfg=app.Config;cfg.Pending.Clear();cfg.ReminderHistory.Clear();cfg.Fired.Clear();cfg.CustomSchedules.Clear();cfg.WaterIntervalEnabled=false;cfg.WaterTimesEnabled=false;cfg.MealEnabled=false;cfg.QuietUntilUtc=DateTime.MinValue;cfg.QuietBypassUntilUtc=DateTime.MinValue;cfg.QuietDailyEnabled=false;cfg.MonitorCodex=false;
                var bubble=(FrameworkElement)app.Bubble.Content;
                cfg.Pending.Add(new Reminder{Key="qa-water",Kind="water",Title=ReminderWorkflow.Title("water"),Message="陪小猫喝口水吧，慢慢来就好。",Due=DateTime.Now});
                check(app.Bubble.PrepareContent(),"ready reminder prepares visible content");
                check(Find<TextBlock>(bubble,"Title").Text=="该喝水了喵"&&Find<TextBlock>(bubble,"Body").Text=="陪小猫喝口水吧，慢慢来就好。","only water title changes, body retained");
                check(Find<WrapPanel>(bubble,"ReminderActions").Visibility==Visibility.Visible,"local reminder action row is present");
                Capture(app.Bubble,Path.Combine(output,"01-喝水提醒.png"),284,222);
                Click(bubble,"DoneReminder");check(cfg.Pending.Count==0&&cfg.ReminderHistory.Single().Outcome=="done","actual WPF complete button records selected occurrence");

                cfg.Pending.Add(new Reminder{Key="qa-meal",Kind="meal",Title=ReminderWorkflow.Title("meal"),Message="先把忙碌放一放，好好吃一顿饭吧。",Due=DateTime.Now});
                check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text=="该吃饭了喵","meal title exact");Capture(app.Bubble,Path.Combine(output,"02-吃饭提醒.png"),284,222);
                var agenda=new TodayWindow(app);var list=Find<ListBox>(agenda.Root,"Items");check(list.Items.Count==2,"today shows both completed and pending occurrences");
                var meal=(AgendaRow)list.Items.Cast<AgendaRow>().Single(r=>r.Title=="该吃饭了喵");meal.Snooze.Execute("10");agenda.Refresh(true);
                check(cfg.Pending.Single().SnoozeUntilUtc>DateTime.UtcNow.AddMinutes(9)&&!app.Bubble.PrepareContent(),"bound today snooze command defers only the selected item");
                Find<ComboBox>(agenda.Root,"Filter").SelectedIndex=3;check(list.Items.Count==1,"snoozed filter renders only deferred rows");
                Find<ComboBox>(agenda.Root,"Filter").SelectedIndex=2;check(list.Items.Count==1,"completed filter renders only completed rows");Find<ComboBox>(agenda.Root,"Filter").SelectedIndex=0;

                cfg.Pending.Add(new Reminder{Key="qa-skip",Kind="custom",Title="站起来伸伸懒腰",Message="给肩颈一点轻松的时间。",Due=DateTime.Now});
                app.Bubble.PrepareContent();Click(bubble,"SkipReminder");check(cfg.ReminderHistory.Any(r=>r.Key=="qa-skip"&&r.Outcome=="skipped")&&cfg.Pending.Single().Key=="qa-meal","skip button never targets an earlier snoozed item");
                cfg.Pending[0].SnoozeUntilUtc=DateTime.MinValue;app.Bubble.PrepareContent();Click(bubble,"Dismiss");check(cfg.ReminderHistory.Single(r=>r.Key=="qa-meal").Outcome=="closed","close button does not count as done");agenda.Refresh(true);
                ((AgendaRow)list.Items.Cast<AgendaRow>().Single(r=>r.Title=="该吃饭了喵")).Restore.Execute(null);check(cfg.Pending.Single().Key=="qa-meal","bound restore command returns closed reminder to queue");

                Click(agenda.Root,"Quiet30");check(QuietPolicy.IsQuiet(cfg,DateTime.Now,DateTime.UtcNow)&&!app.Bubble.PrepareContent(),"30-minute button immediately silences ordinary notes");
                cfg.Pending.Add(new Reminder{Key="qa-important",Kind="custom",Title="重要会议",Message="到约定的时间啦。",Due=DateTime.Now,AllowDuringQuiet=true});check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text=="重要会议","important exception selected behind quiet ordinary reminder");
                app.ResolveReminder("qa-important","done");Click(agenda.Root,"Resume");check(!QuietPolicy.IsQuiet(cfg,DateTime.Now,DateTime.UtcNow)&&app.Bubble.PrepareContent(),"resume button restores queued reminders");
                Find<TextBox>(agenda.Root,"QuietStart").Text="22:30";Find<TextBox>(agenda.Root,"QuietEnd").Text="08:00";Find<CheckBox>(agenda.Root,"QuietDaily").IsChecked=true;Click(agenda.Root,"SaveQuiet");check(cfg.QuietDailyEnabled&&cfg.QuietStart=="22:30"&&cfg.QuietEnd=="08:00","daily quiet save button persists overnight schedule");
                Find<TextBox>(agenda.Root,"QuietStart").Text="bad";Click(agenda.Root,"SaveQuiet");check(cfg.QuietStart=="22:30","invalid quiet edit cannot overwrite valid policy");Find<TextBox>(agenda.Root,"QuietStart").Text="22:30";
                Click(agenda.Root,"Quiet60");check(cfg.QuietUntilUtc>DateTime.UtcNow.AddMinutes(59),"one-hour manual quiet button");

                var when=DateTime.Now.AddMinutes(20);cfg.CustomSchedules.Add(new ScheduleRule{Title="记得拿快递",Mode="once",DueLocal=when,Time=when.ToString("HH:mm")});agenda.Refresh(true);Find<TextBlock>(agenda.Root,"Notes").Text="已处理记录按7天、最多200条清理；未处理和稍后项保留到你处理。×只收起，不计完成。";Capture(agenda,Path.Combine(output,"03-今日小事与免打扰.png"),700,700);
                var panel=app.Panel;Click(panel.Root,"Today");check(!app.Pet.IsVisible&&!app.Bubble.IsVisible&&!panel.IsVisible,"headless button wiring never shows a desktop window");
                panel.OpenScheduleEditor(null);check(Find<ComboBox>(panel.Root,"ScheduleMode").SelectedIndex==2,"temporary small thing uses existing one-shot editor");
                Find<TextBox>(panel.Root,"ScheduleTitle").Text="重要约定测试";Find<CheckBox>(panel.Root,"ScheduleImportant").IsChecked=true;Click(panel.Root,"SaveSchedule");check(cfg.CustomSchedules.Any(r=>r.Title=="重要约定测试"&&r.AllowDuringQuiet&&r.Mode=="once"),"important and one-shot fields save through actual editor button");
                panel.SelectTab(0);Capture(panel,Path.Combine(output,"04-控制面板入口.png"),760,700);

                app.Completion.Preview(app.Pet.AnimationTime);check(!app.Bubble.PrepareContent(),"completion prompt still waits for its first displayed frame");app.Completion.TryPresentPrompt(app.Art.Complete.Sample(0,true));check(app.Bubble.PrepareContent()&&Find<WrapPanel>(bubble,"ReminderActions").Visibility==Visibility.Collapsed,"Codex completion has original content with no local reminder actions");
                Capture(app.Bubble,Path.Combine(output,"05-任务完成提示保持原样.png"),284,174);Click(bubble,"Dismiss");check(!app.Completion.Active,"completion close still acknowledges the completion session");
                var saved=app.Store.Load();check(saved.QuietUntilUtc>DateTime.UtcNow&&saved.ReminderHistory.Count>=3,"UI workflow persists in isolated profile");
                check(!app.Badge.WebViewAllocated&&app.Badge.ViewCreations==0,"new local features never initialize the official task browser");
                check(!app.Pet.IsVisible&&!agenda.IsVisible&&!panel.IsVisible&&!app.Bubble.IsVisible,"no real desktop windows shown by verification");agenda.Close();
                cfg.QuietDailyEnabled=false;cfg.QuietUntilUtc=DateTime.MinValue;cfg.Pending.Clear();cfg.Pending.Add(new Reminder{Key="summary-a",Kind="water",Title="该喝水了喵",Due=DateTime.Now});cfg.Pending.Add(new Reminder{Key="summary-b",Kind="meal",Title="该吃饭了喵",Due=DateTime.Now});
                app.SetQuietMinutes(30);app.ResumeReminders();check(app.Bubble.PrepareContent()&&Find<WrapPanel>(bubble,"SummaryActions").Visibility==Visibility.Visible,"manual resume shows one combined reminder card");Capture(app.Bubble,Path.Combine(output,"06-免打扰结束汇总.png"),284,222);Click(bubble,"Dismiss");check(cfg.Pending.Count==0&&cfg.ReminderHistory.Any(r=>r.Key=="summary-a"&&r.Outcome=="closed")&&cfg.ReminderHistory.Any(r=>r.Key=="summary-b"&&r.Outcome=="closed"),"closing combined card never marks its notes completed");
            }catch(Exception ex){failure=ex;}
            File.WriteAllText(Path.Combine(output,"reminder-ui-qa.json"),new JavaScriptSerializer().Serialize(new{ok=failure==null,checks,error=failure==null?null:failure.ToString(),headless=true,desktopUiAutomationUsed=false,liveMessagesSent=0,realCodexConnectionStarted=false,installedVersionRestarted=false}));
        }
        static void Capture(Window owner,string path,double width,double height){
            var element=(FrameworkElement)owner.Content;owner.Content=null;
            var canvas=new Border{Width=width,Height=height,Background=owner.Background,Child=element,Resources=GrassFishUi.Create()};
            canvas.Measure(new Size(width,height));canvas.Arrange(new Rect(0,0,width,height));canvas.UpdateLayout();CuteUi.SaveRender(canvas,path);
            canvas.Child=null;owner.Content=element;
        }
    }
}
