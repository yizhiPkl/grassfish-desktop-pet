using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace DesktopKitten {
    public sealed partial class KittenApp : Application {
        public readonly bool Qa;
        public readonly bool HeadlessQa;
        public readonly string Exe;
        public readonly Settings Config;
        public readonly Store Store;
        public readonly Scheduler Scheduler;
        public readonly Assets Art;
        public readonly CodexMonitor Monitor;
        public readonly CodexConnection Codex;
        public readonly TaskBadgeWindow Badge;

        public readonly CompletionSession Completion=new CompletionSession();
        public readonly PetWindow Pet;
        public readonly BubbleWindow Bubble;
        PanelWindow panel;
        TodayWindow today;
        public PanelWindow Panel { get { if(panel==null){panel=new PanelWindow(this);panel.Closed+=(s,e)=>panel=null;}return panel; } }
        public Snapshot Status=new Snapshot {State="idle",Detail="正在连接本机 Codex…"};
        readonly Forms.NotifyIcon tray;
        readonly DispatcherTimer seconds;
        readonly EventWaitHandle reopen;
        readonly string renderDirectory,qaCommand;
        DateTime lastCommand=DateTime.MinValue;
        string lastTaskHealth;
        bool quitting;
        int age;
        readonly Stopwatch performance=Stopwatch.StartNew();
        public KittenApp(string[] args,EventWaitHandle reopen) {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;this.reopen=reopen;HeadlessQa=args.Contains("--reminder-ui-test")||args.Contains("--completion-ui-test");Qa=HeadlessQa||args.Contains("--qa")||args.Contains("--render-previews")||args.Contains("--native-task-preview")||args.Contains("--live-navigation-preview")||args.Contains("--completion-sync-preview");
            Exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
            string root=Path.GetDirectoryName(Exe);
            var directory=Qa?Path.Combine(root,args.Contains("--native-task-preview")?"native-qa-profile":"qa-profile"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopKitten");
            Store=new Store(directory);Config=Store.Load();Scheduler=new Scheduler(Config,DateTime.Now,DateTime.UtcNow);Art=new Assets(Path.Combine(root,"assets"));
            InitializeUpdates();
            Codex=new CodexConnection(CodexLocalProfile.Home,null);
            qaCommand=Path.Combine(directory,"qa-command.json");
            int renderIndex=Array.IndexOf(args,"--render-previews");if(renderIndex>=0&&renderIndex+1<args.Length)renderDirectory=args[renderIndex+1];
            Pet=new PetWindow(this);Bubble=new BubbleWindow(this);Badge=new TaskBadgeWindow(this);
            Codex.Changed+=value=>Dispatcher.BeginInvoke(new Action(()=>{if(quitting)return;Badge.Refresh();RefreshPanel();WriteTaskHealth();}));
            Monitor=new CodexMonitor(Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"));
            Monitor.Changed+=snapshot=>Dispatcher.BeginInvoke(new Action(()=>{
                if(quitting)return;
                if(Qa&&File.Exists(qaCommand))return;
                Status=snapshot;Pet.UpdateStatus();if(Pet.IsVisible)Bubble.Refresh();RefreshPanel();
            }));
            tray=new Forms.NotifyIcon {Icon=new System.Drawing.Icon(Path.Combine(root,"assets/cat.ico")),Text="草鱼 · 温柔的小猫伙伴",Visible=!HeadlessQa};
            var menu=new Forms.ContextMenuStrip();AddMenu(menu,"打开猫咪小管家",OpenPanel);AddMenu(menu,"显示小猫",()=>ShowPet(true));AddMenu(menu,"隐藏小猫",HidePet);
            menu.Items.Add(new Forms.ToolStripSeparator());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>OpenPanel();
            AddMenu(menu,"草鱼的任务通知",OpenTasks);
            AddMenu(menu,"今日小事",OpenToday);
            var quietMenu=new Forms.ToolStripMenuItem("免打扰");AddMenuTo(quietMenu,"安静30分钟",()=>SetQuietMinutes(30));AddMenuTo(quietMenu,"安静1小时",()=>SetQuietMinutes(60));AddMenuTo(quietMenu,"恢复提醒",ResumeReminders);AddMenuTo(quietMenu,"设置安静时段",OpenToday);menu.Items.Add(quietMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());AddMenu(menu,"退出桌面小猫",Quit);
            seconds=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};seconds.Tick+=Tick;
            Startup+=(s,e)=>{
                if(args.Contains("--completion-ui-test")){int index=Array.IndexOf(args,"--completion-ui-test");Dispatcher.BeginInvoke(new Action(async()=>{try{await CompletionUiTests.Run(this,args[index+1]);}finally{Quit();}}));return;}
                if(HeadlessQa){int index=Array.IndexOf(args,"--reminder-ui-test");try{ReminderUiTests.Run(this,args[index+1]);}finally{Quit();}return;}
                Pet.Show();if(!args.Contains("--background"))OpenPanel();Bubble.Refresh();Monitor.Enable(Config.MonitorCodex);if(!Qa){Monitor.Start();Codex.Enable(Config.MonitorCodex);Codex.Start();}Badge.Refresh();seconds.Start();
                int nativePreview=Array.IndexOf(args,"--native-task-preview");if(nativePreview>=0&&nativePreview+1<args.Length){seconds.Stop();var previewTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};previewTimer.Tick+=async(sender,evt)=>{previewTimer.Stop();await NativeOverlayTests.Preview(this,args[nativePreview+1]);Quit();};previewTimer.Start();}
                int livePreview=Array.IndexOf(args,"--live-navigation-preview");if(livePreview>=0&&livePreview+1<args.Length){seconds.Stop();var previewTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};previewTimer.Tick+=async(sender,evt)=>{previewTimer.Stop();await NativeOverlayTests.LiveNavigation(this,args[livePreview+1]);Quit();};previewTimer.Start();}
                int completionPreview=Array.IndexOf(args,"--completion-sync-preview");if(completionPreview>=0&&completionPreview+1<args.Length){seconds.Stop();var previewTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};previewTimer.Tick+=async(sender,evt)=>{previewTimer.Stop();await CompletionSyncTests.Preview(this,args[completionPreview+1]);Quit();};previewTimer.Start();}
                if(renderDirectory!=null){Directory.CreateDirectory(renderDirectory);var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=(sender,evt)=>{timer.Stop();RenderPreviews();Quit();};timer.Start();}
            };
            Exit+=(s,e)=>{Updates.Dispose();seconds.Stop();tray.Visible=false;tray.Dispose();Monitor.Dispose();Codex.Dispose();reopen.Dispose();};
        }
        void AddMenu(Forms.ContextMenuStrip menu,string text,Action action){var item=menu.Items.Add(text);item.Click+=(s,e)=>Dispatcher.BeginInvoke(action);}
        internal string[] TrayMenuLabels {get{return tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Select(x=>x.Text).ToArray();}}
        void AddMenuTo(Forms.ToolStripMenuItem menu,string text,Action action){var item=menu.DropDownItems.Add(text);item.Click+=(s,e)=>Dispatcher.BeginInvoke(action);}
        void Tick(object sender,EventArgs e) {
            age++;if(reopen.WaitOne(0))OpenPanel();
            if(!Qa&&(age==30||age%3600==0))CheckUpdates(false);
            if(Scheduler.Tick(DateTime.Now,DateTime.UtcNow)){Persist();if(Scheduler.Current(DateTime.Now,DateTime.UtcNow)!=null)ShowPet(false);Bubble.Refresh();}
            if(panel!=null&&panel.IsVisible)panel.RefreshStatus();
            if(today!=null&&today.IsVisible)today.Refresh();
            if(Badge.IsVisible||Bubble.IsVisible)PositionBubble();
            if(Qa) {
                ReadQaCommand();
                if(age%5==0)WriteDiagnostics();
                // QA is explicitly temporary; no hidden long-lived preview instance.
                if(age>=240)Quit();
            }
        }
        void ReadQaCommand() {
            if(!File.Exists(qaCommand))return;var modified=File.GetLastWriteTimeUtc(qaCommand);if(modified==lastCommand)return;
            try {
                var value=new JavaScriptSerializer().Deserialize<Snapshot>(File.ReadAllText(qaCommand));
                if(value==null||!new[]{"idle","running","waiting","review","failed","unknown"}.Contains(value.State))return;
                lastCommand=modified;Status=value;Pet.UpdateStatus();Bubble.Refresh();RefreshPanel();
            }catch(IOException){}catch(ArgumentException){}
        }
        void WriteDiagnostics() {
            try {
                Directory.CreateDirectory(Store.DirectoryPath);var p=Process.GetCurrentProcess();Memory memory;
                GetProcessMemoryInfo(p.Handle,out memory,(uint)Marshal.SizeOf(typeof(Memory)));
                var value=new {elapsedSeconds=performance.Elapsed.TotalSeconds,cpuSeconds=p.TotalProcessorTime.TotalSeconds,workingSetBytes=memory.working.ToUInt64(),privateBytes=memory.privateBytes.ToUInt64(),
                    state=Status.State,animationState=Pet.ActiveState,frame=Pet.CurrentFrame,frameUpdates=Pet.FrameUpdates,managedBytes=GC.GetTotalMemory(false),pendingReminders=Config.Pending.Count,petVisible=Pet.IsVisible,panelVisible=panel!=null&&panel.IsVisible,bubbleVisible=Bubble.IsVisible,startupTouched=false};
                File.WriteAllText(Path.Combine(Store.DirectoryPath,"qa-diagnostics.json"),new JavaScriptSerializer().Serialize(value));
            }catch(IOException){}
        }
        [StructLayout(LayoutKind.Sequential)] struct Memory { public uint cb,pageFaults;public UIntPtr peakWorking,working,quotaPeakPaged,quotaPaged,quotaPeakNonPaged,quotaNonPaged,pagefile,peakPagefile,privateBytes; }
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr handle,out Memory memory,uint size);
        public void Persist(){try{Store.Save(Config);}catch(IOException){Store.Warning="设置暂时无法保存，请检查磁盘空间或文件权限。";}catch(UnauthorizedAccessException){Store.Warning="设置目录不可写。";}}
        void WriteTaskHealth(){
            if(Qa)return;var c=Codex.Current;string signature=c.Connected+";"+c.DesktopRunning+";"+c.Stale+";"+c.Issue+";"+c.DetailFailures+";"+Badge.ActivityCount+";"+Badge.LastVisibilityReason+";"+Badge.WebViewAllocated+";"+Codex.ListFailures+";"+Codex.ReadFailures;if(lastTaskHealth==signature)return;lastTaskHealth=signature;
            try{File.WriteAllText(Path.Combine(Store.DirectoryPath,"task-health.json"),new JavaScriptSerializer().Serialize(new{atUtc=DateTimeOffset.UtcNow.ToString("o"),c.Connected,c.DesktopRunning,c.Stale,c.Issue,c.DetailFailures,activityCount=Badge.ActivityCount,visible=Badge.IsVisible,reason=Badge.LastVisibilityReason,webViewAllocated=Badge.WebViewAllocated,listFailures=Codex.ListFailures,detailReadFailures=Codex.ReadFailures}));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        public void OpenPanel(){var value=Panel;if(!HeadlessQa){value.Show();value.Activate();}}
        void RefreshPanel(){if(panel!=null)panel.RefreshStatus();if(today!=null)today.Refresh();}
        public void OpenToday(){if(today==null){today=new TodayWindow(this);today.Closed+=(s,e)=>today=null;}today.Refresh(true);if(!HeadlessQa){today.Show();today.Activate();}}
        public void OpenSchedule(string id){OpenPanel();Panel.OpenScheduleEditor(id);}
        public void LocalRemindersChanged(){Persist();if(Scheduler.Current(DateTime.Now,DateTime.UtcNow)!=null)ShowPet(false);Bubble.Refresh();RefreshPanel();}
        public bool ResolveReminder(string key,string outcome){if(!Scheduler.Resolve(key,outcome,DateTime.UtcNow))return false;LocalRemindersChanged();return true;}
        public void ResolveAgenda(string key,string outcome,bool upcoming){bool changed=Scheduler.Resolve(key,outcome,DateTime.UtcNow);if(!changed&&upcoming)changed=Scheduler.ResolvePlanned(key,outcome,DateTime.Now,DateTime.UtcNow);if(changed)LocalRemindersChanged();}
        public bool SnoozeReminder(string key,int minutes){if(!Scheduler.Snooze(key,minutes,DateTime.UtcNow))return false;LocalRemindersChanged();return true;}
        public void RestoreReminder(string key){if(Scheduler.Restore(key))LocalRemindersChanged();}
        public void SetQuietMinutes(int minutes){SetQuietUntil(DateTime.UtcNow.AddMinutes(minutes));}
        public void SetQuietUntil(DateTime until){QuietPolicy.Start(Config,until,DateTime.UtcNow);LocalRemindersChanged();}
        public void ResumeReminders(){Scheduler.PrepareQuietResume(DateTime.Now,DateTime.UtcNow);QuietPolicy.Resume(Config,DateTime.Now,DateTime.UtcNow);LocalRemindersChanged();}
        public void CloseReminderSummary(){Scheduler.CloseSummary(DateTime.UtcNow);LocalRemindersChanged();}
        public void SnoozeReminderSummary(){Scheduler.SnoozeSummary(DateTime.UtcNow);LocalRemindersChanged();}
        public void ShowPet(bool locate){if(HeadlessQa)return;if(!Pet.IsVisible)Pet.Show();if(locate){var area=WindowNative.WorkArea(Pet,WindowNative.Cursor(Pet));var cat=Pet.CatBounds;Pet.PlaceCat(area.Right-cat.Width-48,area.Bottom-cat.Height-32);WindowNative.Clamp(Pet);Config.X=Pet.CatBounds.Left;Config.Y=Pet.CatBounds.Top;Persist();}Bubble.Refresh();Badge.Refresh();}
        public void HidePet(){Pet.Hide();Bubble.Hide();Badge.ReleaseImmediately();Badge.Hide();}
        public void PositionBubble(){if(Badge!=null&&Badge.IsVisible)Badge.Position();if(Bubble!=null&&Bubble.IsVisible)Bubble.Position(Pet);}
        public void OpenTasks(){if(!Pet.IsVisible)ShowPet(false);Badge.Expand();Codex.Refresh();}
        public void Preview(string kind){Scheduler.AddPreview(kind);Persist();ShowPet(false);Bubble.Refresh();RefreshPanel();}
        public void DismissReminder(){if(Completion.Active){Pet.BeginCompletionReturn();Completion.Dismiss();Pet.UpdateStatus();}else {var item=Scheduler.Current(DateTime.Now,DateTime.UtcNow);if(item!=null)Scheduler.Resolve(item.Key,"closed",DateTime.UtcNow);Persist();}Bubble.Refresh();RefreshPanel();}
        public void PreviewCompletion(){if(Art.Complete==null||Status.State=="running"||Status.State=="waiting")return;Completion.Preview(Pet.AnimationTime);Pet.UpdateStatus();ShowPet(false);Bubble.Refresh();}
        public void Quit(){if(quitting)return;quitting=true;Persist();Updates.Dispose();seconds.Stop();if(updateWindow!=null)updateWindow.Close();if(today!=null)today.Close();if(panel!=null)panel.CloseForExit();Badge.Close();Bubble.Close();Pet.Close();Shutdown();}
        void RenderPreviews(){
            UiFixture();
            Config.CustomSchedules.Clear();Config.Pending.Clear();Config.Fired.Clear();Config.WaterMinutes=30;Config.WaterIntervalEnabled=true;Config.MealEnabled=true;
            Config.CustomSchedules.Add(new ScheduleRule{Title="站起来伸伸懒腰",Message="给肩颈一点轻松的时间。",Mode="interval",Minutes=60});Config.CustomSchedules.Add(new ScheduleRule{Title="记得好好休息",Message="关掉屏幕，给自己一个温柔的晚安。",Mode="daily",Time="22:30"});Panel.Reload();Panel.PrepareEditorPreview();
            Codex.Current=new CodexTasksSnapshot{Connected=true,Detail="QA模拟 · 实际本地连接已另行只读验证",Tasks=new System.Collections.Generic.List<CodexTask>{new CodexTask{Id="qa-task-1",HostId="local",Kind="codex",Title="优化草鱼任务面板",Status="active",CanReply=false},new CodexTask{Id="qa-task-2",HostId="local",Kind="codex",Title="整理下一步工作",Status="needsAttention",CanReply=false}}};Badge.Refresh();
            Panel.RefreshStatus();for(int i=0;i<4;i++){Panel.SelectTab(i);Panel.UpdateLayout();CuteUi.SaveRender(Panel,Path.Combine(renderDirectory,"panel-"+i+".png"));}
            Panel.SelectTab(1);Panel.UpdateLayout();Panel.ScrollEditorPreview();Panel.UpdateLayout();CuteUi.SaveRender(Panel,Path.Combine(renderDirectory,"panel-schedule-editor.png"));
            Preview("water");Bubble.UpdateLayout();CuteUi.SaveRender((FrameworkElement)Bubble.Content,Path.Combine(renderDirectory,"bubble.png"));
            var area=WindowNative.WorkArea(Pet,new System.Windows.Point(Pet.Left,Pet.Top));
            var positions=new[]{new System.Windows.Point(area.Left+15,area.Top+15),new System.Windows.Point(area.Right-Pet.Width-15,area.Top+15),new System.Windows.Point(area.Left+15,area.Bottom-Pet.Height-45),new System.Windows.Point(area.Right-Pet.Width-15,area.Bottom-Pet.Height-45),new System.Windows.Point(area.Left+area.Width*.5,area.Top+area.Height*.5)};
            var geometry=new System.Collections.Generic.List<object>();for(int i=0;i<positions.Length;i++){Pet.Left=positions[i].X;Pet.Top=positions[i].Y;Badge.Position();Bubble.Position(Pet);Pet.UpdateLayout();Badge.UpdateLayout();Bubble.UpdateLayout();geometry.Add(new{petLeft=Pet.Left,petTop=Pet.Top,bubbleLeft=Bubble.Left,bubbleTop=Bubble.Top,width=Bubble.Width,height=Bubble.Height});SaveScene(area,Path.Combine(renderDirectory,"position-"+i+".png"));}
            File.WriteAllText(Path.Combine(renderDirectory,"popup-positions.json"),new JavaScriptSerializer().Serialize(geometry));
            OpenTasks();
        }
        void UiFixture(){
            var root=Panel.Root;Config.CustomSchedules.Clear();Panel.Reload();int checks=0;Action<bool,string> check=(v,n)=>{if(!v)throw new Exception("UI fixture: "+n);checks++;};
            var mode=(System.Windows.Controls.ComboBox)root.FindName("ScheduleMode");var time=(System.Windows.Controls.TextBox)root.FindName("ScheduleTime");var minutes=(System.Windows.Controls.TextBox)root.FindName("ScheduleMinutes");var title=(System.Windows.Controls.TextBox)root.FindName("ScheduleTitle");var date=(System.Windows.Controls.DatePicker)root.FindName("ScheduleDate");var save=(System.Windows.Controls.Button)root.FindName("SaveSchedule");
            for(int i=0;i<4;i++){mode.SelectedIndex=i;title.Text="QA约定"+i;time.Text="23:55";minutes.Text="12";date.SelectedDate=DateTime.Today.AddDays(1);check(time.Visibility==(i==3?Visibility.Collapsed:Visibility.Visible),"mode time field");check(date.Visibility==(i==2?Visibility.Visible:Visibility.Collapsed),"mode date field");save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));check(Config.CustomSchedules.Count==i+1,"save repeat mode "+i);}
            title.Text="坏时间";time.Text="25:88";save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));check(Config.CustomSchedules.Count==4,"invalid time not saved");
            var restored=Store.Load();check(restored.CustomSchedules.Count==4&&restored.CustomSchedules.Any(r=>r.Mode=="once"),"UI persistence roundtrip");
            File.WriteAllText(Path.Combine(renderDirectory,"ui-fixture.json"),new JavaScriptSerializer().Serialize(new{ok=true,checks,liveMessagesSent=0,profile="isolated qa-profile",mode="WPF controls raised actual save events"}));
        }
        void SaveScene(Rect area,string path){
            var v=new System.Windows.Media.DrawingVisual();using(var d=v.RenderOpen()){
                d.DrawRectangle(new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(238,244,233),System.Windows.Media.Color.FromRgb(247,236,220),45),null,new Rect(0,0,area.Width,area.Height));
                foreach(var window in new Window[]{Pet,Badge,Bubble})if(window.IsVisible){var brush=new System.Windows.Media.VisualBrush(window){Stretch=System.Windows.Media.Stretch.Fill};d.DrawRectangle(brush,null,new Rect(window.Left-area.Left,window.Top-area.Top,window.Width,window.Height));}
                var label=new System.Windows.Media.FormattedText("草鱼 · 自动布局验收（模拟桌面背景，真实 WPF 窗口布局）",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new System.Windows.Media.Typeface("Microsoft YaHei UI"),12,System.Windows.Media.Brushes.DarkOliveGreen,1.0);d.DrawText(label,new Point(20,area.Height-24));
            }
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)area.Width,(int)area.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(v);var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(path))enc.Save(stream);
        }
    }
    public static class EntryPoint {
        [STAThread] public static int Main(string[] args) {
            if(args.Length==2&&args[0]=="--update-helper-probe")return UpdateHelper.Probe(args[1]);
            if(args.Length==2&&args[0]=="--update-check-probe")return UpdateTests.CheckNetwork(args[1]);
            if(args.Length==3&&args[0]=="--verify-update-package")return UpdateTests.VerifyPackage(args[1],args[2]);
            if(args.Length==2&&args[0]=="--apply-update")return UpdateHelper.Run(args[1]);
            if(args.Length>=2&&args[0]=="--components-ui-test")return CodexComponentUiTests.Run(args[1],args.Length>=3?args[2]:null);
            if(args.Length>=2&&args[0]=="--components-probe"){
                try{string root=Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);string target=CodexNativeComponents.Prepare(Path.Combine(root,"assets/native-task-ui"),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"components-cache"));File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{ok=true,files=Directory.GetFiles(Path.Combine(target,"vendor")).Length,bundledOfficialCode=false,installedCodexModified=false}));return 0;}
                catch(Exception e){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{ok=false,error=e.ToString()}));return 1;}
            }
            if(args.Length>=2&&(args[0]=="--connection-probe"||args[0]=="--native-connection-probe")){
                try{using(var bridge=new CodexConnection(CodexLocalProfile.Home,null))File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(args[0]=="--native-connection-probe"?bridge.ProbeActivities():bridge.Probe()));return 0;}
                catch(Exception e){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{ok=false,error=CodexConnection.ShortError(e),liveMessagesSent=0}));return 1;}
            }
            if(args.Length>=1&&args[0]=="--self-test") {
                try {var result=Tests.Run();if(args.Length>1){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));File.WriteAllText(args[1],result);}return 0;}
                catch(Exception e){if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{ok=false,error=e.ToString()}));return 1;}
            }
            string scope="Local\\DesktopKitten-"+Environment.UserName+(args.Contains("--qa")||args.Contains("--render-previews")||args.Contains("--native-task-preview")||args.Contains("--live-navigation-preview")||args.Contains("--completion-sync-preview")||args.Contains("--reminder-ui-test")||args.Contains("--completion-ui-test")?"-QA":"");bool first;
            using(var mutex=new Mutex(true,scope,out first)) {
                var reopen=new EventWaitHandle(false,EventResetMode.AutoReset,scope+"-Open");
                if(!first){reopen.Set();reopen.Dispose();return 0;}
                try {var app=new KittenApp(args,reopen);app.Run();return 0;}
                catch(Exception e){MessageBox.Show("小猫暂时没能启动。\n\n"+e.Message+"\n\n请确认程序旁的 assets 文件夹完整。","桌面小猫",MessageBoxButton.OK,MessageBoxImage.Warning);reopen.Dispose();return 1;}
                finally{mutex.ReleaseMutex();}
            }
        }
    }
}
