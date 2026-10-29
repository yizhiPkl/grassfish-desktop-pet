using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopKitten {
    public static class PopupPreferencesUiTests {
        static CheckBox Toggle(PanelWindow panel,string name){return (CheckBox)panel.Root.FindName(name);}
        static void Preview(KittenApp app,double elapsed){app.Completion.Preview(app.Pet.AnimationTime-elapsed);app.Pet.UpdateStatus();app.Pet.QaRender();}
        static async Task WaitForHover(KittenApp app){var until=DateTime.UtcNow.AddSeconds(3);while(app.Completion.Active&&DateTime.UtcNow<until){await Task.Delay(20);app.Pet.QaRender();}}
        static Point OpaquePoint(KittenApp app){
            var bitmap=app.Pet.QaDisplayedFrame;var pixels=Tests.Pixels(bitmap);double scale=PetSizing.Scale(app.Config.PetSize);
            for(int y=90;y<160;y++)for(int x=90;x<170;x++)if(pixels[(y*bitmap.PixelWidth+x)*4+3]>240)return new Point((x+.5)*scale,(y+.5)*scale);
            throw new Exception("No opaque cat body in displayed completion frame");
        }
        static void CapturePanel(PanelWindow panel,string path){
            var root=panel.Root;panel.Content=null;var canvas=new Border{Width=760,Height=720,Background=panel.Background,Resources=GrassFishUi.Create(),Child=root};
            canvas.Measure(new Size(760,720));canvas.Arrange(new Rect(0,0,760,720));canvas.UpdateLayout();
            var scroll=FindScroll((DependencyObject)((TabControl)root.FindName("Tabs")).SelectedContent);scroll.ScrollToVerticalOffset(320);canvas.UpdateLayout();CuteUi.SaveRender(canvas,path);
            canvas.Child=null;panel.Content=root;
        }
        static ScrollViewer FindScroll(DependencyObject root){if(root is ScrollViewer)return (ScrollViewer)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindScroll(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;}
        static void CapturePet(KittenApp app,string directory,int frame,string label,bool pointer){
            var bitmap=app.Pet.QaDisplayedFrame;bool extended=app.Pet.ActiveState=="complete"||app.Pet.ActiveState=="complete-return";int ox=extended?app.Art.Complete.OriginX:0,oy=extended?app.Art.Complete.OriginY:0;
            var visual=new DrawingVisual();using(var d=visual.RenderOpen()){
                d.DrawRectangle(new SolidColorBrush(Color.FromRgb(248,250,242)),null,new Rect(0,0,640,360));
                var text=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),17,GrassFishUi.Ink,1);d.DrawText(text,new Point(22,18));
                d.DrawImage(bitmap,new Rect(224+ox,100+oy,bitmap.PixelWidth,bitmap.PixelHeight));
                if(pointer)d.DrawEllipse(new SolidColorBrush(Color.FromArgb(65,100,150,120)),new Pen(GrassFishUi.Green,1.5),new Point(344,200),9,9);
            }
            var image=new RenderTargetBitmap(640,360,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(directory,frame.ToString("D4")+".png")))encoder.Save(file);
        }
        public static async Task Run(KittenApp app,string output){
            Directory.CreateDirectory(output);var checks=new List<string>();Exception failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
            try{
                check(app.Qa&&app.HeadlessQa,"popup verification uses isolated profile and no desktop windows or real connection");
                var cfg=app.Config;cfg.MonitorCodex=false;cfg.FollowMouse=false;cfg.Animations=true;cfg.WaterIntervalEnabled=false;cfg.MealEnabled=false;cfg.WaterTimesEnabled=false;cfg.Pending.Clear();cfg.ReminderHistory.Clear();cfg.CustomSchedules.Clear();cfg.QuietDailyEnabled=false;cfg.QuietUntilUtc=DateTime.MinValue;cfg.QuietSummaryPending=false;
                cfg.ShowCompletionPopup=cfg.ShowTaskPopup=cfg.ShowReminderPopup=true;cfg.PetSize="large";app.Pet.ApplySize();app.Pet.Left=300;app.Pet.Top=300;app.Status=new Snapshot{State="idle",Connected=true};app.Pet.UpdateStatus();
                var panel=app.Panel;panel.Reload();var completion=Toggle(panel,"CompletionPopup");var tasks=Toggle(panel,"TaskPopup");var reminders=Toggle(panel,"ReminderPopup");
                ((TextBox)panel.Root.FindName("Minutes")).Text="invalid unsaved interval";((TextBox)panel.Root.FindName("ScheduleTitle")).Text="unsaved local schedule";
                int oldMinutes=cfg.WaterMinutes;
                completion.IsChecked=false;tasks.IsChecked=false;reminders.IsChecked=false;var saved=app.Store.Load();
                check(!cfg.ShowCompletionPopup&&!cfg.ShowTaskPopup&&!cfg.ShowReminderPopup&&!saved.ShowCompletionPopup&&!saved.ShowTaskPopup&&!saved.ShowReminderPopup,"actual toggle events apply and persist immediately without Save");
                check(cfg.WaterMinutes==oldMinutes&&saved.WaterMinutes==oldMinutes&&cfg.CustomSchedules.Count==0&&panel.HasUnsavedLocalEdits&&((TextBox)panel.Root.FindName("ScheduleTitle")).Text=="unsaved local schedule","popup toggles neither validate nor commit unrelated unsaved edits");
                string serialized=File.ReadAllText(app.Store.FilePath);panel.Reload();check(File.ReadAllText(app.Store.FilePath)==serialized,"initializing or reloading toggle values does not save or acknowledge anything");
                Directory.CreateDirectory(app.Store.FilePath+".tmp");try{completion.IsChecked=true;check(!cfg.ShowCompletionPopup&&completion.IsChecked==false&&((TextBlock)panel.Root.FindName("Notes")).Text.Contains("未保存"),"write failure rolls preference and checkbox back visibly");}finally{Directory.Delete(app.Store.FilePath+".tmp");}
                var pending=new Reminder{Key="qa-popup-important",Kind="custom",Title="重要约定",Message="隔离测试",Due=DateTime.Now,AllowDuringQuiet=true};cfg.Pending.Add(pending);
                Preview(app,app.Art.Complete.EntryDurationMs+1200);check(!app.Bubble.PrepareContent()&&cfg.Pending.Count==1&&app.Completion.Active,"hiding both popup types preserves completion and important calendar occurrence");
                completion.IsChecked=true;check(app.Bubble.PrepareContent()&&((WrapPanel)((FrameworkElement)app.Bubble.Content).FindName("CompletionActions")).Visibility==Visibility.Visible,"hidden important reminder does not block enabled completion popup");
                reminders.IsChecked=true;check(app.Bubble.PrepareContent()&&((TextBlock)((FrameworkElement)app.Bubble.Content).FindName("Title")).Text==pending.Title,"re-enabling calendar shows existing highest-priority pending item");
                reminders.IsChecked=false;app.DismissReminder();check(cfg.Pending.Count==1&&cfg.ReminderHistory.Count==0&&!app.Completion.Active,"dismiss targets displayed completion rather than hidden calendar task");
                completion.IsChecked=false;completion.IsChecked=true;check(!app.Bubble.PrepareContent(),"re-enabling completion never revives a dismissed notification");
                var today=new TodayWindow(app);check(((ListBox)today.Root.FindName("Items")).Items.Count==1,"hidden important occurrence is still actionable in Today");today.Close();
                cfg.Pending.Clear();
                cfg.Pending.Add(new Reminder{Key="qa-popup-summary-a",Kind="water",Title="该喝水了喵",Due=DateTime.Now});cfg.Pending.Add(new Reminder{Key="qa-popup-summary-b",Kind="meal",Title="该吃饭了喵",Due=DateTime.Now});cfg.QuietSummaryPending=true;
                check(!app.Bubble.PrepareContent()&&cfg.QuietSummaryPending&&cfg.Pending.Count==2,"hidden quiet summary does not clear or resolve its occurrences");
                reminders.IsChecked=true;check(app.Bubble.PrepareContent()&&((WrapPanel)((FrameworkElement)app.Bubble.Content).FindName("SummaryActions")).Visibility==Visibility.Visible,"re-enabled reminders restore a single quiet summary");reminders.IsChecked=false;cfg.Pending.Clear();cfg.QuietSummaryPending=false;
                cfg.MonitorCodex=true;var task=new CodexTask{Id="qa-popup-task",TurnKey="qa-popup-turn",HostId="local",Kind="codex",Title="隔离通知",Status="active",CanReply=true};app.Codex.Current=new CodexTasksSnapshot{Connected=true,DesktopRunning=true,Tasks=new List<CodexTask>{task}};app.Badge.QaSetDraft(task.Id,"unsent isolated draft");app.Badge.Refresh();
                check(app.Badge.ActivityCount==1&&app.Badge.DraftCount==1&&app.Badge.LastVisibilityReason=="popup-disabled","automatic-off retains task ledger and unsent draft while hiding badge");
                app.OpenTasks();app.Badge.Refresh();app.Badge.Refresh();check(app.Badge.PopupRequested&&app.Badge.LastVisibilityReason!="popup-disabled"&&app.Badge.DraftCount==1,"explicit task open remains open over subsequent refreshes while automatic display is disabled");
                app.Badge.Collapse();check(!app.Badge.PopupRequested&&app.Badge.LastVisibilityReason=="popup-disabled"&&app.Badge.DraftCount==1&&app.Badge.ActivityCount==1,"manual collapse hides opted-out badge without reading tasks or losing drafts");
                tasks.IsChecked=true;app.OpenTasks();tasks.IsChecked=false;check(!app.Badge.PopupRequested&&app.Badge.LastVisibilityReason=="popup-disabled"&&app.Badge.DraftCount==1,"switching off immediately hides an already manually opened task popup");
                cfg.MonitorCodex=false;completion.IsChecked=false;
                app.Pet.QaCompletionPointer(new Point(-20,-20));Preview(app,app.Art.Complete.EntryDurationMs+1200);var inside=OpaquePoint(app);app.Pet.QaCompletionPointer(inside);
                Preview(app,app.Art.Complete.EntryDurationMs+60000);app.Pet.QaRender();check(app.Completion.Active&&app.Pet.ActiveState=="complete","completion remains active beyond one minute; existing hover does not dismiss it");
                var hoverFrames=Path.Combine(output,"hover-frames");Directory.CreateDirectory(hoverFrames);
                for(int i=0;i<110;i++){app.Pet.QaRender();CapturePet(app,hoverFrames,i,"关闭完成弹窗：持续摇尾巴，不计时收起",false);await Task.Delay(50);}
                check(app.Completion.Active&&app.Pet.ActiveState=="complete","actual live render stays waving for over five seconds with no timer or pre-existing-hover dismissal");
                app.Pet.QaCompletionPointer(new Point(-20,-20));app.Pet.QaRender();app.Pet.QaCompletionPointer(OpaquePoint(app));double hoverStart=app.Pet.AnimationTime;app.Pet.QaRender();
                check(app.Completion.Active&&app.Pet.ActiveState=="complete","pointer entry keeps the completion waving instead of acknowledging immediately");
                int hoverFrame=110;var hoverUntil=DateTime.UtcNow.AddSeconds(3);
                while(app.Completion.Active&&DateTime.UtcNow<hoverUntil){
                    if(app.Pet.AnimationTime-hoverStart<CompletionAttention.HoverDelayMs)check(app.Pet.ActiveState=="complete","completion keeps waving throughout the 300ms hover delay");
                    CapturePet(app,hoverFrames,hoverFrame++,"鼠标移入：继续摇尾巴0.3秒",true);await Task.Delay(50);app.Pet.QaRender();
                }
                check(!app.Completion.Active&&app.Pet.ActiveState=="complete-return"&&app.Pet.AnimationTime-hoverStart>=300,"300ms fresh hover acknowledges hidden completion and starts authored recovery");var before=Tests.Pixels(app.Pet.QaDisplayedFrame);app.Pet.QaRender();
                check(before.SequenceEqual(Tests.Pixels(app.Pet.QaDisplayedFrame)),"hover recovery starts at the displayed tail phase with no pasted head or snap");
                bool sawIdle=false,exactIdle=false,naturalRecovery=true;
                for(int i=0;i<45;i++){app.Pet.QaRender();if(!sawIdle){if(app.Pet.ActiveState=="idle"){sawIdle=true;exactIdle=Tests.Pixels(app.Pet.QaDisplayedFrame).SequenceEqual(Tests.Pixels(app.Art.Frame(0,0,0)));}else if(app.Pet.ActiveState!="complete-return")naturalRecovery=false;}CapturePet(app,hoverFrames,hoverFrame++,"停留满0.3秒：原有复位动画 → 待机",true);await Task.Delay(50);}
                check(!app.Completion.Active&&sawIdle&&exactIdle&&naturalRecovery,"hover recovery reaches exact original idle before normal breathing/blinking or petting can resume");
                app.Pet.QaCompletionPointer(new Point(-20,-20));Preview(app,app.Art.Complete.EntryDurationMs+1200);app.Pet.QaCompletionPointer(new Point(1,1));app.Pet.QaRender();check(app.Completion.Active,"transparent canvas padding is not hovering over the cat");app.Pet.QaBeginPress(new Point(400,400));app.Pet.QaMovePointer(new Point(402,400));app.Pet.QaEndPress();check(app.Completion.Active,"press or sub-threshold motion without real drag is not acknowledgement");
                var dragFrames=Path.Combine(output,"drag-frames");Directory.CreateDirectory(dragFrames);
                for(int i=0;i<25;i++){app.Pet.QaRender();CapturePet(app,dragFrames,i,"关闭完成弹窗：仍摇尾巴提醒",false);await Task.Delay(50);}
                app.Pet.QaBeginPress(new Point(400,400));app.Pet.QaMovePointer(new Point(409,400));app.Pet.QaRender();check(!app.Completion.Active&&app.Pet.ActiveState=="running-right","true drag acknowledges once and gives the existing walking animation ownership");
                for(int i=25;i<40;i++){app.Pet.QaMovePointer(new Point(409+(i-25)*2,400));app.Pet.QaRender();CapturePet(app,dragFrames,i,"拖动草鱼：沿用原来的行走动作",true);await Task.Delay(50);}
                app.Pet.QaEndPress();app.Pet.QaRender();check(!app.Completion.Active&&app.Pet.ActiveState=="idle","releasing a real drag returns to idle instead of restarting completion");
                for(int i=40;i<65;i++){app.Pet.QaRender();CapturePet(app,dragFrames,i,"松开后待机，不再重复这次提醒",false);await Task.Delay(50);}
                completion.IsChecked=true;Preview(app,app.Art.Complete.EntryDurationMs+1200);app.Pet.QaCompletionPointer(new Point(-20,-20));app.Pet.QaRender();app.Pet.QaCompletionPointer(OpaquePoint(app));app.Pet.QaRender();await Task.Delay(400);app.Pet.QaRender();check(app.Completion.Active,"enabled completion popup retains explicit acknowledgement even beyond hover delay");
                completion.IsChecked=false;app.Pet.QaCompletionPointer(new Point(-20,-20));app.Pet.QaRender();app.Pet.QaCompletionPointer(OpaquePoint(app));app.Pet.QaRender();await Task.Delay(100);app.Pet.QaCompletionPointer(new Point(-20,-20));app.Pet.QaRender();await Task.Delay(400);app.Pet.QaRender();check(app.Completion.Active,"leaving before 300ms cancels hover acknowledgement");
                app.Pet.QaCompletionPointer(OpaquePoint(app));app.Pet.QaRender();await WaitForHover(app);check(app.Pet.ActiveState=="complete-return","a fresh 300ms dwell re-arms recovery after a cancelled hover");app.Status=new Snapshot{State="running",Connected=true};app.Pet.UpdateStatus();app.Pet.QaRender();check(app.Pet.ActiveState=="running","new task immediately preempts hover recovery");
                app.Status=new Snapshot{State="idle",Connected=true};app.Pet.UpdateStatus();app.Pet.QaCompletionPointer(new Point(-20,-20));Preview(app,app.Art.Complete.EntryDurationMs+1200);app.Pet.QaCompletionPointer(OpaquePoint(app));app.Pet.QaRender();app.Status=new Snapshot{State="running",Connected=true};app.Pet.UpdateStatus();await Task.Delay(400);app.Pet.QaRender();check(app.Pet.ActiveState=="running"&&!app.Completion.Active,"an old 300ms hover deadline cannot interrupt a new task");
                app.Status=new Snapshot{State="idle",Connected=true};app.Pet.UpdateStatus();app.Config.Animations=false;app.Pet.QaCompletionPointer(new Point(-20,-20));Preview(app,app.Art.Complete.EntryDurationMs+1200);app.Pet.QaCompletionPointer(OpaquePoint(app));app.Pet.QaRender();await WaitForHover(app);app.Pet.QaRender();check(!app.Completion.Active&&app.Pet.ActiveState=="idle","reduced motion waits 300ms hover but skips animated recovery");
                panel.Reload();panel.SelectTab(2);CapturePanel(panel,Path.Combine(output,"popup-settings.png"));
                check(!app.Pet.IsVisible&&!panel.IsVisible&&!app.Badge.IsVisible&&!app.Bubble.IsVisible&&!app.Badge.WebViewAllocated&&app.Badge.QaSendAttempts==0,"all verification stays headless, sends no real messages, starts no task browser and never modifies installed app");
            }catch(Exception e){failure=e;}
            File.WriteAllText(Path.Combine(output,"popup-ui-qa.json"),new JavaScriptSerializer().Serialize(new{ok=failure==null,checks,error=failure==null?null:failure.ToString(),headless=true,liveMessagesSent=0,realCodexConnectionStarted=false,installedVersionModified=false}));
        }
    }
}
