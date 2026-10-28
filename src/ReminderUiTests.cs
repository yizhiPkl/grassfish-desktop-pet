using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
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
                check(!app.TrayMenuLabels.Any(label=>label.Contains("预览")),"system tray menu also contains no water, meal or completion previews");
                check(app.TrayMenuLabels.Last()=="退出桌面小猫","system tray exit command is the final menu option");
                check(menuLabels.Contains("今日小事")&&!app.Pet.ContextMenu.StaysOpen,"right-click menu preserves normal commands with outside dismissal enabled");
                var cfg=app.Config;cfg.Pending.Clear();cfg.ReminderHistory.Clear();cfg.Fired.Clear();cfg.CustomSchedules.Clear();cfg.WaterIntervalEnabled=false;cfg.WaterTimesEnabled=false;cfg.MealEnabled=false;cfg.QuietUntilUtc=DateTime.MinValue;cfg.QuietBypassUntilUtc=DateTime.MinValue;cfg.QuietDailyEnabled=false;cfg.MonitorCodex=false;
                var bubble=(FrameworkElement)app.Bubble.Content;
                cfg.Pending.Add(new Reminder{Key="qa-water",Kind="water",Title=ReminderWorkflow.Title("water"),Message="陪小猫喝口水吧，慢慢来就好。",Due=DateTime.Now});
                check(app.Bubble.PrepareContent(),"ready reminder prepares visible content");
                check(Find<TextBlock>(bubble,"Title").Text=="该喝水了喵"&&Find<TextBlock>(bubble,"Body").Text=="陪小猫喝口水吧，慢慢来就好。","only water title changes, body retained");
                check(Find<WrapPanel>(bubble,"ReminderActions").Visibility==Visibility.Visible,"local reminder action row is present");
                Capture(app.Bubble,Path.Combine(output,"01-喝水提醒.png"),284,222);
                check(Math.Abs(Find<Button>(bubble,"DoneReminder").TranslatePoint(new Point(),bubble).Y-Find<Button>(bubble,"SkipReminder").TranslatePoint(new Point(),bubble).Y)<1,"three reminder actions remain on one row at the actual popup width");
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
                cfg.Pending[0].SnoozeUntilUtc=DateTime.MinValue;app.Bubble.PrepareContent();Click(bubble,"SkipReminder");check(cfg.ReminderHistory.Single(r=>r.Key=="qa-meal").Outcome=="skipped","skip replaces the old close action without marking done");agenda.Refresh(true);
                check(Find<ComboBox>(agenda.Root,"Filter").Items.Count==5,"today has no separate closed filter");
                cfg.ReminderHistory.Add(new Reminder{Key="legacy-closed",Kind="custom",Title="旧版收起记录",Due=DateTime.Now,HandledUtc=DateTime.UtcNow,Outcome="closed"});
                agenda.Refresh(true);Find<ComboBox>(agenda.Root,"Filter").SelectedIndex=4;
                check(list.Items.Cast<AgendaRow>().Any(r=>r.Title=="旧版收起记录"&&r.StateText=="已跳过")&&cfg.ReminderHistory.Any(r=>r.Key=="legacy-closed"&&r.Outcome=="closed"),"legacy closed records share skipped filter without rewriting or losing history");Find<ComboBox>(agenda.Root,"Filter").SelectedIndex=0;
                cfg.Pending.Add(new Reminder{Key="qa-next-meal",Kind="meal",Title=ReminderWorkflow.Title("meal"),Message="先把忙碌放一放，好好吃一顿饭吧。",Due=DateTime.Now});

                Click(agenda.Root,"Quiet30");check(QuietPolicy.IsQuiet(cfg,DateTime.Now,DateTime.UtcNow)&&!app.Bubble.PrepareContent(),"30-minute button immediately silences ordinary notes");
                cfg.Pending.Add(new Reminder{Key="qa-important",Kind="custom",Title="重要会议",Message="到约定的时间啦。",Due=DateTime.Now,AllowDuringQuiet=true});check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text=="重要会议","important exception selected behind quiet ordinary reminder");
                app.ResolveReminder("qa-important","done");Click(agenda.Root,"Resume");check(!QuietPolicy.IsQuiet(cfg,DateTime.Now,DateTime.UtcNow)&&app.Bubble.PrepareContent(),"resume button restores queued reminders");
                Find<TextBox>(agenda.Root,"QuietStart").Text="22:30";Find<TextBox>(agenda.Root,"QuietEnd").Text="08:00";Find<CheckBox>(agenda.Root,"QuietDaily").IsChecked=true;Click(agenda.Root,"SaveQuiet");check(cfg.QuietDailyEnabled&&cfg.QuietStart=="22:30"&&cfg.QuietEnd=="08:00","daily quiet save button persists overnight schedule");
                Find<TextBox>(agenda.Root,"QuietStart").Text="bad";Click(agenda.Root,"SaveQuiet");check(cfg.QuietStart=="22:30","invalid quiet edit cannot overwrite valid policy");Find<TextBox>(agenda.Root,"QuietStart").Text="22:30";
                Click(agenda.Root,"Quiet60");check(cfg.QuietUntilUtc>DateTime.UtcNow.AddMinutes(59),"one-hour manual quiet button");

                var when=DateTime.Now.AddMinutes(20);cfg.CustomSchedules.Add(new ScheduleRule{Title="记得拿快递",Mode="once",DueLocal=when,Time=when.ToString("HH:mm")});agenda.Refresh(true);Capture(agenda,Path.Combine(output,"03-今日小事与免打扰.png"),700,700);
                var panel=app.Panel;Click(panel.Root,"Today");check(!app.Pet.IsVisible&&!app.Bubble.IsVisible&&!panel.IsVisible,"headless button wiring never shows a desktop window");
                check(Find<TextBlock>(panel.Root,"Version").Text==AppVersion.AboutText,"about version is read from the installed assembly, not handwritten UI text");
                panel.SelectTab(3);Capture(panel,Path.Combine(output,"07-关于草鱼与检查更新.png"),760,700);
                Click(panel.Root,"CheckUpdate");check(app.Updates.Status.Contains("隔离测试"),"about update button is wired but isolated UI tests cannot contact GitHub");
                Find<CheckBox>(panel.Root,"AutoUpdates").IsChecked=false;check(!app.Updates.Automatic,"automatic update checks can be disabled through the real checkbox");
                panel.Reload();check(!panel.HasUnsavedLocalEdits,"saved control panel does not falsely block update");Find<TextBox>(panel.Root,"Minutes").Text="47";
                check(panel.HasUnsavedLocalEdits,"real control panel unsaved edit is detected for updater");panel.Reload();
                using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;var feed=new FixtureUpdateFeed{Payload=new byte[4096]};feed.Offer=UpdateTests.FixtureOffer(rsa,feed.Payload);
                    using(var updates=new UpdateManager(Path.Combine(output,"update-ui-fixture"),true,a=>a(),feed)){
                        updates.Check(true).GetAwaiter().GetResult();int requests=0;var update=new UpdateWindow(updates,()=>{requests++;return Task.FromResult(true);},app.Art.Portrait);
                        check(Find<TextBlock>(update.Root,"VersionLine").Text.Contains(AppVersion.Text)&&Find<TextBlock>(update.Root,"ReleaseNotes").Text.Contains("保留日程"),"update dialog renders installed version and plain-text release notes");
                        Capture(update,Path.Combine(output,"08-草鱼的小更新.png"),460,560);Click(update.Root,"Apply");check(requests==1&&feed.Downloads==0,"update button invokes supplied action, fixture never installs");
                        update.Preparing=true;check(!Find<Button>(update.Root,"Apply").IsEnabled&&!Find<Button>(update.Root,"Later").IsEnabled,"preparation prevents duplicate install and premature close");update.Preparing=false;
                        Click(update.Root,"Skip");check(new UpdatePreferencesStore(Path.Combine(output,"update-ui-fixture")).Value.SkippedVersion==feed.Offer.Manifest.version,"actual skip button saves only selected version");
                    }
                }
                var weekdays=Find<WrapPanel>(panel.Root,"Days").Children.OfType<CheckBox>().ToArray();
                check(weekdays.Select(b=>(int)b.Tag).SequenceEqual(new[]{1,2,3,4,5,6,0})&&weekdays.Select(b=>Convert.ToString(b.Content)).SequenceEqual(new[]{"一","二","三","四","五","六","日"}),"weekly selector displays Monday through Sunday without changing day identifiers");
                Click(panel.Root,"NewSchedule");check(weekdays.Where(b=>b.IsChecked==true).Select(b=>(int)b.Tag).SequenceEqual(new[]{1,2,3,4,5}),"default weekday selection remains Monday through Friday");
                var weekend=new ScheduleRule{Title="周末顺序测试",Mode="weekly",Time="09:00",Days=new[]{0,6},NextUtc=DateTime.UtcNow.AddDays(1),LastFiredLocal=DateTime.Today.AddDays(-7)};cfg.CustomSchedules.Add(weekend);panel.OpenScheduleEditor(weekend.Id);
                check(weekdays.Where(b=>b.IsChecked==true).Select(b=>(int)b.Tag).SequenceEqual(new[]{6,0})&&weekdays.Last().IsChecked==true,"legacy Sunday and Saturday settings still select the correct last two controls");
                Capture(panel,Path.Combine(output,"09-每周提醒周日排在最后.png"),760,900);Click(panel.Root,"SaveSchedule");var unchanged=cfg.CustomSchedules.Single(r=>r.Id==weekend.Id);
                check(unchanged.Days.SequenceEqual(new[]{0,6})&&unchanged.NextUtc==weekend.NextUtc&&unchanged.LastFiredLocal==weekend.LastFiredLocal,"saving an unchanged legacy weekly rule preserves stored weekday order and firing state");
                Find<TextBox>(panel.Root,"ScheduleTitle").Text="周一和周日测试";Find<ComboBox>(panel.Root,"ScheduleMode").SelectedIndex=1;foreach(var day in weekdays)day.IsChecked=(int)day.Tag==0||(int)day.Tag==1;Click(panel.Root,"SaveSchedule");
                check(cfg.CustomSchedules.Single(r=>r.Title=="周一和周日测试").Days.SequenceEqual(new[]{0,1}),"new weekly rule still saves Sunday as zero and Monday as one");
                panel.OpenScheduleEditor(null);check(Find<ComboBox>(panel.Root,"ScheduleMode").SelectedIndex==2,"temporary small thing uses existing one-shot editor");
                Find<TextBox>(panel.Root,"ScheduleTitle").Text="重要约定测试";Find<CheckBox>(panel.Root,"ScheduleImportant").IsChecked=true;Click(panel.Root,"SaveSchedule");check(cfg.CustomSchedules.Any(r=>r.Title=="重要约定测试"&&r.AllowDuringQuiet&&r.Mode=="once"),"important and one-shot fields save through actual editor button");
                panel.SelectTab(0);Capture(panel,Path.Combine(output,"04-控制面板入口.png"),760,700);

                cfg.QuietUntilUtc=DateTime.MinValue;cfg.QuietDailyEnabled=false;cfg.Pending.Clear();
                app.Completion.Preview(app.Pet.AnimationTime);check(!app.Bubble.PrepareContent(),"completion prompt alone still waits for its first displayed frame");app.Completion.TryPresentPrompt(app.Art.Complete.Sample(0,true));check(app.Bubble.PrepareContent()&&Find<WrapPanel>(bubble,"ReminderActions").Visibility==Visibility.Collapsed,"Codex completion has no calendar actions");
                check(Find<TextBlock>(bubble,"Body").Text=="喵喵喵"&&Find<TextBlock>(bubble,"Counter").Text==""&&Convert.ToString(Find<Button>(bubble,"Dismiss").Content)=="知道啦","completion uses only meows and a compact acknowledgement, never a cross");
                Capture(app.Bubble,Path.Combine(output,"05-简洁任务完成提示.png"),284,180);
                check(Find<ScrollViewer>(bubble,"BodyScroll").ComputedVerticalScrollBarVisibility==Visibility.Collapsed,"short meows fit fully without a redundant scrollbar");
                cfg.Pending.Add(new Reminder{Key="priority-normal",Kind="custom",Title="普通日程",Message="日程不被任务完成提示挡住。",Due=DateTime.Now.AddMinutes(-2)});
                check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text=="普通日程"&&app.Completion.Active,"ordinary due reminder preempts completion without losing it");
                var urgentRule=new ScheduleRule{Title="开会提醒",Mode="once",DueLocal=DateTime.Now.AddMinutes(-1),Enabled=false,Completed=true};cfg.CustomSchedules.Add(urgentRule);
                cfg.Pending.Add(new Reminder{Key="priority-important",Kind="custom",Title=urgentRule.Title,ScheduleId=urgentRule.Id,Due=urgentRule.DueLocal,Message="到时间啦。"});agenda.Refresh(true);
                var urgentRow=list.Items.Cast<AgendaRow>().Single(r=>r.Title==urgentRule.Title);urgentRow.SetImportant.Execute(true);app.Bubble.PrepareContent();
                check(Find<TextBlock>(bubble,"Title").Text==urgentRule.Title&&Find<TextBlock>(bubble,"Tag").Text=="草鱼 · 重要提醒"&&urgentRule.AllowDuringQuiet,"today importance command immediately preempts an already visible ordinary note");
                Capture(app.Bubble,Path.Combine(output,"10-重要日程优先.png"),284,222);
                check(Find<Button>(bubble,"DoneReminder").FontSize==12&&Find<WrapPanel>(bubble,"CompletionActions").Visibility==Visibility.Collapsed,"reminder has larger action buttons and no dismiss control");
                Click(bubble,"Dismiss");check(app.Completion.Active&&cfg.Pending.Count==2,"hidden completion acknowledgement cannot consume a reminder or its task session");
                app.SnoozeReminder("priority-important",10);check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text=="普通日程","explicit snooze is respected even for the highest priority");
                app.ResolveReminder("priority-normal","done");check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Body").Text=="喵喵喵"&&app.Completion.Active,"completion resumes after ready reminders are handled");
                Click(bubble,"Dismiss");check(!app.Completion.Active&&cfg.Pending.Single().Key=="priority-important","acknowledgement does not touch snoozed calendar item");
                panel.OpenScheduleEditor(urgentRule.Id);Find<CheckBox>(panel.Root,"ScheduleImportant").IsChecked=false;Click(panel.Root,"SaveSchedule");
                check(!cfg.Pending.Single().AllowDuringQuiet&&cfg.CustomSchedules.Single(r=>r.Id==urgentRule.Id).Completed,"editing importance preserves fired one-shot completion and synchronizes its pending reminder");
                app.Completion.Preview(app.Pet.AnimationTime);cfg.Pending.Single().SnoozeUntilUtc=DateTime.MinValue;cfg.Pending.Single().AllowDuringQuiet=true;
                check(app.Bubble.PrepareContent()&&Find<TextBlock>(bubble,"Title").Text==urgentRule.Title&&!app.Completion.PromptReady,"important calendar note never waits for an animation handoff");
                app.ResolveReminder("priority-important","skipped");app.Completion.TryPresentPrompt(app.Art.Complete.Sample(0,true));app.Bubble.PrepareContent();Click(bubble,"Dismiss");
                app.SetQuietMinutes(30);
                var saved=app.Store.Load();check(saved.QuietUntilUtc>DateTime.UtcNow&&saved.ReminderHistory.Count>=3,"UI workflow persists in isolated profile");
                check(!app.Badge.WebViewAllocated&&app.Badge.ViewCreations==0,"new local features never initialize the official task browser");
                check(!app.Pet.IsVisible&&!agenda.IsVisible&&!panel.IsVisible&&!app.Bubble.IsVisible,"no real desktop windows shown by verification");agenda.Close();
                cfg.QuietDailyEnabled=false;cfg.QuietUntilUtc=DateTime.MinValue;cfg.Pending.Clear();cfg.Pending.Add(new Reminder{Key="summary-a",Kind="water",Title="该喝水了喵",Due=DateTime.Now});cfg.Pending.Add(new Reminder{Key="summary-b",Kind="meal",Title="该吃饭了喵",Due=DateTime.Now});
                app.SetQuietMinutes(30);app.ResumeReminders();check(app.Bubble.PrepareContent()&&Find<WrapPanel>(bubble,"SummaryActions").Visibility==Visibility.Visible,"manual resume shows one combined reminder card");Capture(app.Bubble,Path.Combine(output,"06-免打扰结束汇总.png"),284,222);Click(bubble,"SkipSummary");check(cfg.Pending.Count==0&&cfg.ReminderHistory.Any(r=>r.Key=="summary-a"&&r.Outcome=="skipped")&&cfg.ReminderHistory.Any(r=>r.Key=="summary-b"&&r.Outcome=="skipped"),"summary skip records skipped rather than closed or completed");
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
