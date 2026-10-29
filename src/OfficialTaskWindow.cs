using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace DesktopKitten {
    // Only the expanded tray loads the original official views. The collapsed badge is a small WPF control.
    public sealed class TaskBadgeWindow : Window {
        readonly KittenApp app;
        readonly NativeActivityLedger ledger=new NativeActivityLedger();readonly TaskViewState state=new TaskViewState();
        readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
        readonly Grid root=new Grid();readonly Canvas light=new Canvas();readonly Button badge=new Button(),loading=new Button();
        readonly TextBlock badgeText=new TextBlock{FontFamily=new FontFamily("Segoe UI"),FontSize=12,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        readonly string assetRoot;readonly HashSet<string> opening=new HashSet<string>();
        readonly HashSet<uint> browserPids=new HashSet<uint>();
        readonly DispatcherTimer warmTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};
        readonly System.Diagnostics.Stopwatch loadWatch=new System.Diagnostics.Stopwatch();
        public double LastColdLoadMilliseconds;public int WarmReopens;public string LastVisibilityReason;public bool WarmCached {get{return view!=null&&!expanded;}}
        WebView2CompositionControl view;Task releaseTask=Task.FromResult(true),initializationTask=Task.FromResult(true);int generation;
        string nonce,lastLayout="",lastShape="",lastPayload,localComponents;bool ready,closed,expanded,interactive,publishing,dirty;
        double measuredHeight=56;NativeTaskPose pose;public string Error;public int QaSendAttempts,QaOpenAttempts;public string QaOpenTarget,QaOpenError;internal string QaRealNavigationTarget;internal bool QaRealNavigationConfirmed;
        public int ViewCreations,ViewReleases,PublishCount,ShapeUpdates;public uint LastBrowserProcessId;
        public int ActivityCount {get{return ledger.Items.Count;}}
        public int DraftCount {get{return state.DraftCount;}}
        public int PendingSendCount {get{return state.PendingCount;}}
        public bool WebViewAllocated {get{return view!=null;}}
        public uint[] CreatedBrowserPids {get{return browserPids.ToArray();}}
        public string NativeBadgeText {get{return badgeText.Text;}}
        public Rect BadgeBounds {get{return pose==null?Rect.Empty:pose.Badge;}}
        public Rect TrayBounds {get{return pose==null?Rect.Empty:pose.Tray;}}
        public NativeTaskPose Placement {get{return pose;}}
        public bool Ready {get{return ready;}}
        internal bool PopupRequested {get{return app.Config.ShowTaskPopup||expanded;}}
        internal void QaSetDraft(string id,string text){if(!app.HeadlessQa)throw new InvalidOperationException("Headless QA only");state.Set(id,text);}
        [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int a,int b,int c,int d,int e,int f);
        [DllImport("gdi32.dll")]static extern int CombineRgn(IntPtr target,IntPtr a,IntPtr b,int mode);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr target);
        [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
        static ControlTemplate PillTemplate(){
            var b=new FrameworkElementFactory(typeof(Border));b.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));
            b.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));b.SetValue(Border.BorderBrushProperty,new SolidColorBrush(Color.FromArgb(100,255,255,255)));b.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            var c=new FrameworkElementFactory(typeof(ContentPresenter));c.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);c.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);b.AppendChild(c);
            var template=new ControlTemplate(typeof(Button)){VisualTree=b};var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromArgb(249,255,255,255))));template.Triggers.Add(hover);return template;
        }
        public TaskBadgeWindow(KittenApp app){
            this.app=app;assetRoot=Path.Combine(Path.GetDirectoryName(app.Exe),"assets/native-task-ui");Title="草鱼 · 任务通知";Width=48;Height=48;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=app.Qa;
            Content=root;root.Children.Add(light);badge.Content=badgeText;badge.Width=badge.Height=24;badge.Template=PillTemplate();badge.Background=new SolidColorBrush(Color.FromArgb(230,255,255,255));badge.Foreground=new SolidColorBrush(Color.FromRgb(26,28,31));badgeText.FontWeight=FontWeights.Medium;badge.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=4,ShadowDepth=1,Opacity=.14};badge.Cursor=System.Windows.Input.Cursors.Hand;badge.Focusable=false;
            badge.Click+=(s,e)=>{if(expanded)Collapse();else Expand();};light.Children.Add(badge);
            loading.Template=PillTemplate();loading.Background=new SolidColorBrush(Color.FromArgb(242,255,255,255));loading.Padding=new Thickness(10,5,10,5);loading.FontSize=11;loading.Content="加载任务…";loading.Focusable=false;loading.Click+=(s,e)=>{if(!app.Codex.Current.Connected){app.Codex.Refresh();}else if(Error!=null){Error=null;StartView();}else Collapse();};light.Children.Add(loading);
            warmTimer.Tick+=(s,e)=>{warmTimer.Stop();if(!expanded)ReleaseView();};
            SourceInitialized+=(s,e)=>{WindowNative.NoActivate(this,!app.Qa);lastShape="";ShapeWindow();};
            Closed+=(s,e)=>{closed=true;warmTimer.Stop();SetInteractive(false);ReleaseView();};
        }
        bool Current(WebView2CompositionControl target,int token){return !closed&&ReferenceEquals(view,target)&&generation==token;}
        bool CanExpand {get{return !closed&&!app.UpdatePreparing&&expanded&&app.Pet.IsVisible&&app.Config.MonitorCodex&&app.Codex.Current.Connected;}}
        async void StartView(){
            if(view!=null||!CanExpand)return;
            await releaseTask;if(view!=null||!CanExpand)return;
            var target=new WebView2CompositionControl{DefaultBackgroundColor=System.Drawing.Color.Transparent,Visibility=Visibility.Hidden};
            int token=++generation;string sessionNonce=Guid.NewGuid().ToString("N");nonce=sessionNonce;view=target;ready=false;Error=null;lastPayload=null;ViewCreations++;root.Children.Insert(0,target);DrawLight();
            loadWatch.Restart();initializationTask=InitializeView(target,token,sessionNonce);
        }
        async Task InitializeView(WebView2CompositionControl target,int token,string sessionNonce){
            try{
                string components=await Task.Run(()=>CodexNativeComponents.Prepare(assetRoot,Path.Combine(app.Store.DirectoryPath,"codex-components")));
                if(!Current(target,token))return;
                localComponents=components;
                string profile=Path.Combine(app.Store.DirectoryPath,"native-task-webview");
                var environment=await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-sync --no-first-run"));
                // Once environment creation has started, finish creating its controller even after a collapse.
                // ReleaseView waits for this task and disposes that controller; otherwise a late environment
                // can create orphan browser processes after the visible control was already disposed.
                await target.EnsureCoreWebView2Async(environment);
                var core=target.CoreWebView2;LastBrowserProcessId=core.BrowserProcessId;if(app.Qa)browserPids.Add(core.BrowserProcessId);if(!Current(target,token))return;
                core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsZoomControlEnabled=false;
                core.SetVirtualHostNameToFolderMapping("grassfish-pet.local",localComponents,CoreWebView2HostResourceAccessKind.DenyCors);
                core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
                core.WebResourceRequested+=(s,e)=>{if(!e.Request.Uri.StartsWith("https://grassfish-pet.local/",StringComparison.OrdinalIgnoreCase)&&!e.Request.Uri.StartsWith("data:",StringComparison.OrdinalIgnoreCase))e.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Only local pet components are allowed","");};
                core.NavigationStarting+=(s,e)=>{if(e.Uri!="https://grassfish-pet.local/index.html")e.Cancel=true;};core.NewWindowRequested+=(s,e)=>e.Handled=true;core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
                core.WebMessageReceived+=async(s,e)=>{if(Current(target,token))await OnMessage(target,token,e);};
                core.ProcessFailed+=(s,e)=>{if(!Current(target,token))return;Error="任务界面暂不可用，点此重试；草鱼和日程不受影响。";ReleaseView();DrawLight();};
                await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__grassFishBoot="+json.Serialize(new{nonce=sessionNonce,qa=app.Qa,state=state.Export(),snapshot=Snapshot()})+";");
                if(Current(target,token))core.Navigate("https://grassfish-pet.local/index.html");
            }catch(Exception e){if(Current(target,token)){Error="任务界面暂不可用，点此重试："+CodexConnection.ShortError(e);ReleaseView();DrawLight();}}
        }
        void ReleaseView(){
            warmTimer.Stop();
            var old=view;generation++;if(old==null)return;
            var versions=state.Versions();view=null;ready=false;dirty=false;lastPayload=null;SetInteractive(false);
            old.Visibility=Visibility.Hidden;DrawLight();releaseTask=FlushAndDispose(old,versions,initializationTask);
        }
        async Task FlushAndDispose(WebView2CompositionControl old,Dictionary<string,long> versions,Task initialization){
            try{
                await initialization;
                if(old.CoreWebView2!=null){
                    var export=old.CoreWebView2.ExecuteScriptAsync("window.grassFish?window.grassFish.exportState():null");
                    if(await Task.WhenAny(export,Task.Delay(600))==export){string value=await export;if(value!="null")state.Import(json.DeserializeObject(value),versions);}
                    else ObserveFailure(export);
                }
            }catch(Exception){/* Renderer crash/exit: continuously mirrored drafts remain in host memory. */}
            finally{root.Children.Remove(old);try{old.Dispose();}catch(InvalidOperationException){}ViewReleases++;}
        }
        static void ObserveFailure(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        public void Refresh(){
            ledger.Apply(app.Codex.Current);
            var c=app.Codex.Current;
            bool petAvailable=app.Pet.IsVisible||app.HeadlessQa;
            if(!petAvailable||!app.Config.MonitorCodex||!(c.Connected||c.DesktopRunning)){LastVisibilityReason=!petAvailable?"pet-hidden":!app.Config.MonitorCodex?"integration-disabled":"codex-not-running";expanded=false;ReleaseView();SetInteractive(false);Hide();return;}
            if(!PopupRequested){LastVisibilityReason="popup-disabled";ReleaseView();SetInteractive(false);Hide();return;}
            LastVisibilityReason=c.Connected?(ActivityCount==0?"connected-empty":"connected-tasks"):"reconnecting";
            if(!c.Connected){SetInteractive(false);ReleaseView();}
            Position();if(!IsVisible&&!app.HeadlessQa)Show();DrawLight();if(expanded&&c.Connected&&!app.HeadlessQa){warmTimer.Stop();if(view!=null&&ready){view.CoreWebView2.Resume();view.Visibility=Visibility.Visible;}else if(Error==null)StartView();}Publish();
        }
        public void Expand(){if(!expanded&&view!=null&&ready)WarmReopens++;expanded=true;Error=null;warmTimer.Stop();app.Codex.Refresh();Refresh();}
        public void Collapse(){expanded=false;SetInteractive(false);if(view!=null){view.Visibility=Visibility.Hidden;Publish();if(!ready)ReleaseView();else{warmTimer.Stop();warmTimer.Start();SuspendHidden(view,generation);}}Refresh();}
        async void SuspendHidden(WebView2CompositionControl target,int token){try{await Task.Delay(100);if(!Current(target,token)||expanded||!ready)return;await target.CoreWebView2.TrySuspendAsync();if(Current(target,token)&&expanded)target.CoreWebView2.Resume();}catch(Exception){/* Best effort only. The fixed idle deadline still disposes the controller. */}}
        public void ReleaseImmediately(){expanded=false;SetInteractive(false);ReleaseView();Refresh();}
        public void SuppressPopup(){expanded=false;SetInteractive(false);ReleaseView();Hide();}
        public void QaExpireWarmCache(){if(!app.Qa)throw new InvalidOperationException("QA only");warmTimer.Stop();if(!expanded)ReleaseView();}
        public void Position(){
            var pet=app.Pet;if(double.IsNaN(pet.Left)||double.IsNaN(pet.Top))return;
            var area=WindowNative.WorkArea(pet,new Point(pet.CatBounds.Left+pet.CatBounds.Width/2,pet.CatBounds.Top+pet.CatBounds.Height/2));
            pose=NativeTaskPlacement.Place(new Rect(pet.Left,pet.Top,pet.Width,pet.Height),area,expanded,measuredHeight);
            var b=pose.Window;if(Math.Abs(Left-b.Left)>.1||double.IsNaN(Left))Left=b.Left;if(Math.Abs(Top-b.Top)>.1||double.IsNaN(Top))Top=b.Top;if(Math.Abs(Width-b.Width)>.1)Width=b.Width;if(Math.Abs(Height-b.Height)>.1)Height=b.Height;
            ShapeWindow();DrawLight();string key=b.ToString()+";"+pose.Badge.ToString()+";"+pose.Tray.ToString();if(lastLayout!=key){lastLayout=key;Publish();}
        }
        void DrawLight(){
            light.Visibility=ready&&expanded?Visibility.Collapsed:Visibility.Visible;if(pose==null)return;
            badgeText.Text=app.Codex.Current.Connected?ActivityCount.ToString():"…";
            if(expanded){var chevron=new System.Windows.Shapes.Path{Data=Geometry.Parse(pose.TrayAbove?"M 0,0 L 5,5 L 10,0":"M 0,5 L 5,0 L 10,5"),Stroke=badge.Foreground,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};badge.Content=chevron;}else badge.Content=badgeText;
            badge.ToolTip=expanded?"收起任务":app.Codex.Current.Connected?"显示任务，共 "+ActivityCount+" 个":app.Codex.Current.Detail;
            Canvas.SetLeft(badge,pose.Badge.Left-pose.Window.Left);Canvas.SetTop(badge,pose.Badge.Top-pose.Window.Top);
            loading.Visibility=expanded&&!pose.Tray.IsEmpty?Visibility.Visible:Visibility.Collapsed;
            if(!pose.Tray.IsEmpty){Canvas.SetLeft(loading,pose.Tray.Left-pose.Window.Left+(pose.Tray.Width-142)/2);Canvas.SetTop(loading,pose.Tray.Top-pose.Window.Top);loading.Width=Math.Min(142,pose.Tray.Width);loading.Height=Math.Min(28,pose.Tray.Height);loading.Content=!app.Codex.Current.Connected?app.Codex.Current.RetryLabel:Error!=null?(Error.Contains("未适配")?"任务界面未适配 · 重试":"任务未就绪 · 点此重试"):"加载任务…";loading.ToolTip=Error??app.Codex.Current.Detail;}
        }
        void ShapeWindow(){
            if(pose==null)return;var source=PresentationSource.FromVisual(this);if(source==null)return;var m=source.CompositionTarget.TransformToDevice;
            var badgeLocal=pose.Badge;badgeLocal.Offset(-pose.Window.Left,-pose.Window.Top);var trayLocal=pose.Tray;if(!trayLocal.IsEmpty)trayLocal.Offset(-pose.Window.Left,-pose.Window.Top);
            string key=badgeLocal.ToString()+";"+trayLocal.ToString()+";"+m.M11+";"+m.M22;if(lastShape==key)return;lastShape=key;ShapeUpdates++;
            Func<Rect,IntPtr> region=r=>{r.Offset(-pose.Window.Left,-pose.Window.Top);r.Inflate(10,10);var a=m.Transform(r.TopLeft);var b=m.Transform(r.BottomRight);return CreateRoundRectRgn((int)Math.Floor(a.X),(int)Math.Floor(a.Y),(int)Math.Ceiling(b.X),(int)Math.Ceiling(b.Y),18,18);};
            IntPtr total=region(pose.Badge);if(!pose.Tray.IsEmpty){IntPtr second=region(pose.Tray);CombineRgn(total,total,second,2);DeleteObject(second);}if(SetWindowRgn(new WindowInteropHelper(this).Handle,total,true)==0){DeleteObject(total);lastShape="";}
        }
        object Snapshot(){var b=pose.Window;var t=pose.Tray;return new{connected=app.Codex.Current.Connected,detail=app.Codex.Current.Detail,expanded,delivery=state.Export(),items=ledger.Items.Select(x=>new{id=x.Task.Id,turnKey=x.TurnKey,title=x.Task.Title,body=x.Body,level=x.Level,isLoading=x.IsLoading,canReply=x.Task.CanReply&&app.Codex.Current.Connected}).ToArray(),layout=new{badgeX=pose.Badge.Left-b.Left,badgeY=pose.Badge.Top-b.Top,trayX=t.IsEmpty?0:t.Left-b.Left,trayY=t.IsEmpty?0:t.Top-b.Top,trayWidth=t.IsEmpty?360:t.Width,maxHeight=t.IsEmpty?56:t.Height,above=pose.TrayAbove}};}
        async void Publish(){
            dirty=true;if(!ready||closed||pose==null||publishing||view==null)return;publishing=true;var target=view;int token=generation;
            try{while(dirty&&ready&&Current(target,token)){dirty=false;string payload=json.Serialize(Snapshot());if(lastPayload==payload)continue;await target.CoreWebView2.ExecuteScriptAsync("window.grassFish&&window.grassFish.update("+payload+");");if(Current(target,token)){lastPayload=payload;PublishCount++;}}}
            catch(Exception e){if(Current(target,token))Error=CodexConnection.ShortError(e);}finally{publishing=false;if(dirty&&ready&&!closed)Publish();}
        }
        void SetInteractive(bool value){if(interactive==value)return;interactive=value;WindowNative.Interactive(this,value);if(value&&view!=null){Activate();view.Focus();if(ready)view.CoreWebView2.ExecuteScriptAsync("window.grassFish&&window.grassFish.focus()");}}
        async Task OnMessage(WebView2CompositionControl target,int token,CoreWebView2WebMessageReceivedEventArgs e){try{
            if(!Current(target,token)||e.Source!="https://grassfish-pet.local/index.html")return;
            var data=json.DeserializeObject(e.TryGetWebMessageAsString());if(JsonValue.Text(data,"nonce")!=nonce)return;string action=JsonValue.Text(data,"action");
            if(action=="ready"){ready=true;string mounted=localComponents;if(mounted!=null)await Task.Run(()=>CodexNativeComponents.MarkReady(mounted));if(!Current(target,token))return;LastColdLoadMilliseconds=loadWatch.Elapsed.TotalMilliseconds;target.Visibility=expanded?Visibility.Visible:Visibility.Hidden;DrawLight();Publish();return;}
            if(action=="view-error"){Error="官方任务组件发生错误："+JsonValue.Text(data,"error");ReleaseView();DrawLight();return;}
            if(action=="measure"){double height;double.TryParse(JsonValue.Text(data,"height"),out height);if(height>=40&&height<=10000&&Math.Abs(height-measuredHeight)>1){measuredHeight=height;Position();}return;}
            if(action=="expand"){if(JsonValue.Bool(data,"expanded"))Expand();else Collapse();return;}
            if(action=="focus"){SetInteractive(JsonValue.Bool(data,"active")&&expanded&&IsVisible);return;}
            string id=JsonValue.Text(data,"id"),turn=JsonValue.Text(data,"turnKey");var item=ledger.Find(id,turn);if(item==null)return;
            if(action=="draft"){state.Set(id,JsonValue.Text(data,"draft"));return;}
            if(action=="dismiss"){ledger.Dismiss(id,turn);Refresh();return;}
            if(action=="open"){
                if(!expanded||!IsVisible||!app.Config.MonitorCodex||!opening.Add(id))return;
                bool ok=false;string error=null;bool wasInteractive=interactive;
                try{if(!app.Codex.Current.Connected)throw new InvalidOperationException("Codex 连接暂不可用，无法打开原对话。");ledger.RetainOpened(id,turn);if(app.Qa){QaOpenAttempts++;QaOpenTarget=id;if(QaOpenError!=null)throw new InvalidOperationException(QaOpenError);if(id==QaRealNavigationTarget){await app.Codex.Open(item.Task);QaRealNavigationConfirmed=true;}}else{SetInteractive(true);await app.Codex.Open(item.Task);CodexWindowActivation.ActivateExisting();}ok=true;}
                catch(Exception failure){error="未能打开 Codex 对话："+CodexConnection.ShortError(failure);}finally{opening.Remove(id);if(!wasInteractive&&Current(target,token))SetInteractive(false);}
                if(Current(target,token)&&ready)await target.CoreWebView2.ExecuteScriptAsync("window.grassFish.openResult("+json.Serialize(new{id,ok,error})+");");return;
            }
            if(action!="send")return;string prompt=JsonValue.Text(data,"prompt");
            if(!expanded||!IsVisible||!app.Config.MonitorCodex||!app.Codex.Current.Connected||!item.Task.CanReply||!state.Begin(id,turn,prompt))return;
            bool sent=false;string failureText=null;
            try{if(app.Qa){QaSendAttempts++;throw new InvalidOperationException("QA隔离验证：没有发送真实消息，草稿已保留。");}await Task.Run(()=>app.Codex.Send(item.Task,prompt));sent=true;}
            catch(Exception failure){failureText=CodexConnection.ShortError(failure)+" 请先查看原对话确认结果，不会自动重发。";}
            finally{state.Finish(id,turn,sent,failureText);Publish();}
        }catch(Exception failure){if(Current(target,token))Error=CodexConnection.ShortError(failure);}}
        public async Task<bool> WaitReady(int milliseconds){for(int age=0;age<milliseconds&&!ready&&Error==null;age+=100)await Task.Delay(100);return ready;}
        public async Task<bool> WaitReleased(int milliseconds){var task=releaseTask;return await Task.WhenAny(task,Task.Delay(milliseconds))==task&&view==null;}
        public async Task Capture(string path){
            if(ready&&expanded&&view!=null){using(var stream=File.Create(path))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);return;}
            root.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(Width),(int)Math.Ceiling(Height),96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);
        }
        public Task<string> QaScript(string script){if(!app.Qa||!ready||view==null)throw new InvalidOperationException("只能在已就绪的隔离QA任务界面运行脚本");return view.CoreWebView2.ExecuteScriptAsync(script);}
    }
}
