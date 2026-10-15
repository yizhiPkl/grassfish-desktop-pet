using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopKitten {
    public static class WindowNative {
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr h,int index,int value);
        public static void NoActivate(Window w, bool toolWindow) { var h=new WindowInteropHelper(w).Handle; int style=GetWindowLong(h,-20)|0x08000000; SetWindowLong(h,-20,toolWindow?style|0x80:style&~0x80); }
        public static void Interactive(Window w,bool active){var h=new WindowInteropHelper(w).Handle;int style=GetWindowLong(h,-20);SetWindowLong(h,-20,active?style&~0x08000000:style|0x08000000);}
        public static System.Windows.Point Cursor(Window w) {
            Point p; GetCursorPos(out p); var source=PresentationSource.FromVisual(w);
            return source==null ? new System.Windows.Point(p.X,p.Y) : source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(p.X,p.Y));
        }
        public static Rect WorkArea(Window w, System.Windows.Point near) {
            var source=PresentationSource.FromVisual(w); var toDevice=source==null?Matrix.Identity:source.CompositionTarget.TransformToDevice;
            var fromDevice=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
            var p=toDevice.Transform(near); var area=Forms.Screen.FromPoint(new System.Drawing.Point((int)p.X,(int)p.Y)).WorkingArea;
            var a=fromDevice.Transform(new System.Windows.Point(area.Left,area.Top)); var b=fromDevice.Transform(new System.Windows.Point(area.Right,area.Bottom));
            return new Rect(a,b);
        }
        public static void Clamp(Window w) {
            var area=WorkArea(w,new System.Windows.Point(w.Left+w.Width/2,w.Top+w.Height/2));
            w.Left=Math.Max(area.Left,Math.Min(w.Left,area.Right-w.Width)); w.Top=Math.Max(area.Top,Math.Min(w.Top,area.Bottom-w.Height));
        }
    }
    public sealed class Assets {
        public readonly BitmapSource Portrait;
        public readonly WorkAnimation Work;
        public readonly WorkAnimation Complete;
        public readonly PettingAnimation Petting;
        public readonly WorkAnimation PettingLayout;
        readonly Dictionary<string,byte[]> pettingPixels=new Dictionary<string,byte[]>();
        byte[] pettingBlend;
        WriteableBitmap pettingSurface;
        readonly string directory;
        readonly Dictionary<string,BitmapSource> frames=new Dictionary<string,BitmapSource>();
        static BitmapSource Load(string path) {
            var b=new BitmapImage(); b.BeginInit(); b.UriSource=new Uri(path); b.CacheOption=BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); return b;
        }
        public Assets(string directory) {
            this.directory=directory;
            Portrait=Load(Path.Combine(directory,"portrait.png"));
            Work=WorkAnimation.Load(Path.Combine(directory,"work","animation.json"));
            Complete=WorkAnimation.Load(Path.Combine(directory,"complete","animation.json"));
            if(Complete!=null && Work!=null && Complete.HandoffWorkIndex>=Work.Loop.Length)throw new InvalidDataException("完成动画交接帧超出工作循环");
            Petting=PettingAnimation.Load(Path.Combine(directory,"petting","animation.json"));
            if(Petting!=null)PettingLayout=new WorkAnimation{Width=Petting.width,Height=Petting.height,OriginX=-Petting.catOrigin[0],OriginY=-Petting.catOrigin[1]};
        }
        public BitmapSource Frame(int row,int column,int phase) {
            if(row!=0||column==0)phase=0;
            string key=row+":"+column+":"+phase; BitmapSource bitmap;
            if(!frames.TryGetValue(key,out bitmap)) {
                // Pixel-identical small files are loaded on demand, not the whole animated spritesheet.
                bitmap=Load(Path.Combine(directory,"frames",row+"-"+column+"-"+phase+".png"));frames[key]=bitmap;
            }
            return bitmap;
        }
        public BitmapSource WorkFrame(WorkPose pose) {
            BitmapSource bitmap;
            if(!frames.TryGetValue(pose.Key,out bitmap)) {
                bitmap=Load(Path.Combine(directory,"work",pose.File));
                if(bitmap.PixelWidth!=Work.Width||bitmap.PixelHeight!=Work.Height)throw new InvalidDataException("工作动画图片尺寸不符");
                frames[pose.Key]=bitmap;
            }
            return bitmap;
        }
        public BitmapSource CompletionFrame(WorkPose pose) {
            string key="complete:"+pose.Key;BitmapSource bitmap;
            if(!frames.TryGetValue(key,out bitmap)) {
                bitmap=Load(Path.Combine(directory,"complete",pose.File));
                if(bitmap.PixelWidth!=Complete.Width||bitmap.PixelHeight!=Complete.Height)throw new InvalidDataException("完成动画图片尺寸不符");
                frames[key]=bitmap;
            }
            return bitmap;
        }
        byte[] PettingPixels(string id){
            byte[] pixels;if(pettingPixels.TryGetValue(id,out pixels))return pixels;
            string filename;if(!Petting.frameFiles.TryGetValue(id,out filename))throw new InvalidDataException("悬停图片编号无效");
            var source=Load(Path.Combine(directory,"petting",filename));
            if(source.PixelWidth!=Petting.width||source.PixelHeight!=Petting.height)throw new InvalidDataException("悬停图片尺寸不符");
            var bitmap=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);pixels=new byte[Petting.width*Petting.height*4];bitmap.CopyPixels(pixels,Petting.width*4,0);pettingPixels[id]=pixels;return pixels;
        }
        public void CapturePetting(BitmapSource source){
            if(source==null||source.PixelWidth!=192||source.PixelHeight!=208)source=Frame(0,0,0);
            var bitmap=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);var small=new byte[192*208*4];bitmap.CopyPixels(small,192*4,0);
            var padded=new byte[Petting.width*Petting.height*4];
            for(int y=0;y<208;y++)Array.Copy(small,y*192*4,padded,((y+Petting.catOrigin[1])*Petting.width+Petting.catOrigin[0])*4,192*4);
            pettingPixels["captured"]=padded;
        }
        public BitmapSource PettingFrame(PettingLayer[] layers){
            if(pettingSurface==null){pettingSurface=new WriteableBitmap(Petting.width,Petting.height,96,96,PixelFormats.Pbgra32,null);pettingBlend=new byte[Petting.width*Petting.height*4];}
            var buffers=layers.Select(f=>PettingPixels(f.id)).ToArray();
            if(layers.Length==1&&Math.Abs(layers[0].weight-1)<1e-9)Array.Copy(buffers[0],pettingBlend,pettingBlend.Length);
            else for(int i=0;i<pettingBlend.Length;i++){double value=0;for(int j=0;j<layers.Length;j++)value+=buffers[j][i]*layers[j].weight;pettingBlend[i]=(byte)Math.Min(255,Math.Max(0,Math.Round(value)));}
            pettingSurface.WritePixels(new Int32Rect(0,0,Petting.width,Petting.height),pettingBlend,Petting.width*4,0);return pettingSurface;
        }
    }
    public sealed class PetWindow : Window {
        readonly KittenApp app;
        readonly Image picture;
        readonly DispatcherTimer timer;
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly WorkSession workSession=new WorkSession();
        readonly PettingPlayer petting;
        double pettingIdleOrigin=-1;
        double displayScale;
        string state="waving", frameKey=null;
        double stateStart;
        int gaze=-1;
        double gazeChanged;
        bool mouseDown,dragging,hovered;
        System.Windows.Point down,origin;
        public string ActiveState {get{return state;}}
        public string CurrentFrame {get{return frameKey;}}
        public int FrameUpdates {get;private set;}
        public double AnimationTime {get{return clock.Elapsed.TotalMilliseconds;}}
        WorkAnimation CurrentClip {get{return state=="petting"?app.Art.PettingLayout:state=="complete"?app.Art.Complete:state=="running"?app.Art.Work:null;}}
        public Rect CatBounds { get {
            var clip=CurrentClip;
            return new Rect(Left-(clip!=null?clip.OriginX:0)*displayScale,Top-(clip!=null?clip.OriginY:0)*displayScale,192*displayScale,208*displayScale);
        } }
        public PetWindow(KittenApp app) {
            this.app=app;if(app.Art.Petting!=null)petting=new PettingPlayer(app.Art.Petting);displayScale=PetSizing.Scale(app.Config.PetSize);var dimensions=PetSizing.Dimensions(app.Config.PetSize);Title="桌面小猫";Width=dimensions.Width;Height=dimensions.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=app.Qa;ShowActivated=false;
            picture=new Image {Width=Width,Height=Height,Stretch=Stretch.Uniform};Content=picture;
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);
            SourceInitialized+=(s,e)=>WindowNative.NoActivate(this,!app.Qa);
            Loaded+=(s,e)=> {var area=WindowNative.WorkArea(this,WindowNative.Cursor(this));Left=app.Config.X<0?area.Right-Width-48:app.Config.X;Top=app.Config.Y<0?area.Bottom-Height-32:app.Config.Y;WindowNative.Clamp(this);Render();};
            MouseLeftButtonDown+=(s,e)=> {
                if(e.ClickCount==2){app.OpenPanel();e.Handled=true;return;}
                mouseDown=true;down=WindowNative.Cursor(this);origin=new System.Windows.Point(CatBounds.Left,CatBounds.Top);CaptureMouse();
            };
            MouseMove+=(s,e)=> {
                if(!mouseDown)return;var current=WindowNative.Cursor(this);var delta=current-down;
                if(!dragging&&delta.Length>4)dragging=true;
                if(dragging){double x=origin.X+delta.X;if(state!="running-left"&&state!="running-right")SetState(delta.X<0?"running-left":"running-right");else if(Math.Abs(x-CatBounds.Left)>0.2)SetState(x<CatBounds.Left?"running-left":"running-right");Left=x;Top=origin.Y+delta.Y;app.PositionBubble();}
            };
            MouseLeftButtonUp+=(s,e)=> {if(!mouseDown)return;mouseDown=false;ReleaseMouseCapture();if(dragging){dragging=false;WindowNative.Clamp(this);app.Config.X=Left;app.Config.Y=Top;app.Persist();UpdateStatus();}};
            LostMouseCapture+=(s,e)=> {mouseDown=false;if(dragging){dragging=false;UpdateStatus();}};
            MouseEnter+=(s,e)=>{hovered=true;if(petting==null&&!dragging&&app.Status.State!="running"&&!app.Completion.Active)SetState("jumping");};
            MouseLeave+=(s,e)=>{hovered=false;};
            ContextMenu=new ContextMenu{StaysOpen=false};
            AddMenu("打开猫咪小管家",()=>app.OpenPanel());AddMenu("隐藏小猫",()=>app.HidePet());
            AddMenu("今日小事",()=>app.OpenToday());AddMenu("安静30分钟",()=>app.SetQuietMinutes(30));AddMenu("恢复提醒",()=>app.ResumeReminders());
            ContextMenu.Items.Add(new Separator());AddMenu("退出桌面小猫",()=>app.Quit());
            new PetContextMenu(this,ContextMenu);
            timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=(s,e)=>Render();timer.Start();
            IsVisibleChanged+=(s,e)=>{if(IsVisible)timer.Start();else timer.Stop();};
            LocationChanged+=(s,e)=>app.PositionBubble();SizeChanged+=(s,e)=>app.PositionBubble();
            Closed+=(s,e)=>timer.Stop();
        }
        void AddMenu(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(s,e)=>action();ContextMenu.Items.Add(item);}
        public void ApplySize(){
            var dimensions=PetSizing.Dimensions(app.Config.PetSize);
            bool positioned=IsLoaded&&!double.IsNaN(Left)&&!double.IsNaN(Top);
            // Size presets refer to the cat, not the cat-plus-keyboard rectangle.
            var bounds=positioned?PetSizing.ResizeKeepingFeet(CatBounds,app.Config.PetSize):new Rect(0,0,dimensions.Width,dimensions.Height);
            displayScale=PetSizing.Scale(app.Config.PetSize);ApplyArtworkLayout();
            if(positioned){PlaceCat(bounds.Left,bounds.Top);WindowNative.Clamp(this);app.Config.X=CatBounds.Left;app.Config.Y=CatBounds.Top;}
            gaze=-1;frameKey=null;Render();if(positioned)app.PositionBubble();
        }
        void ApplyArtworkLayout(){
            var clip=CurrentClip;
            Width=(clip!=null?clip.Width:192)*displayScale;Height=(clip!=null?clip.Height:208)*displayScale;
            picture.Width=Width;picture.Height=Height;
        }
        public void PlaceCat(double left,double top){
            var clip=CurrentClip;
            Left=left+(clip!=null?clip.OriginX:0)*displayScale;Top=top+(clip!=null?clip.OriginY:0)*displayScale;
        }
        public void SetState(string value){
            if(value=="unknown")value="idle";if(value==state)return;
            bool positioned=IsLoaded&&!double.IsNaN(Left)&&!double.IsNaN(Top);var oldBounds=positioned?CatBounds:Rect.Empty;
            var oldClip=CurrentClip;
            state=value;stateStart=clock.Elapsed.TotalMilliseconds;gaze=-1;frameKey=null;
            if(oldClip!=CurrentClip){ApplyArtworkLayout();if(positioned){PlaceCat(oldBounds.Left,oldBounds.Top);WindowNative.Clamp(this);app.PositionBubble();}}
        }
        public void UpdateStatus(){
            app.Completion.Update(app.Status,clock.Elapsed.TotalMilliseconds,app.Config.MonitorCodex&&app.Art.Complete!=null);
            workSession.Update(app.Status.State=="running",clock.Elapsed.TotalMilliseconds);
            if(dragging){if(petting!=null)petting.Cancel(clock.Elapsed.TotalMilliseconds);return;}
            if(app.Completion.Active){if(petting!=null)petting.Cancel(clock.Elapsed.TotalMilliseconds);SetState(app.Completion.AwaitingHandoff?"running":"complete");return;}
            if(state=="petting"&&EnjoymentAllowed())return;
            if(state=="petting"&&petting!=null)petting.Cancel(clock.Elapsed.TotalMilliseconds);
            // Keep the original short greeting when connecting to an idle Codex.
            // A running task still takes precedence immediately.
            if(app.Status.State=="idle"&&state=="waving"&&clock.Elapsed.TotalMilliseconds-stateStart<Motion.Durations["waving"].Sum()*3)return;
            SetState(app.Completion.SuppressReview(app.Status)?"idle":app.Status.State);
        }
        bool EnjoymentAllowed(){return PettingRules.Allowed(app.Completion.SuppressReview(app.Status)?"idle":app.Status.State,app.Completion.Active,app.Config.Animations,dragging,mouseDown);}
        bool RenderPetting(double now){
            if(petting==null)return false;
            bool allowed=EnjoymentAllowed()&&(state=="idle"||state=="petting")&&(ContextMenu==null||!ContextMenu.IsOpen);
            if(!allowed){if(petting.State!="idle")petting.Cancel(now);if(state=="petting")SetState(app.Completion.Active?(app.Completion.AwaitingHandoff?"running":"complete"):(app.Completion.SuppressReview(app.Status)?"idle":app.Status.State));return false;}
            var cursor=WindowNative.Cursor(this);var bounds=CatBounds;
            bool inside=app.Art.Petting.Hit((cursor.X-bounds.Left)/displayScale+app.Art.Petting.catOrigin[0],(cursor.Y-bounds.Top)/displayScale+app.Art.Petting.catOrigin[1]);
            if(inside&&petting.State=="idle"&&!petting.Hovered){app.Art.CapturePetting(picture.Source as BitmapSource);petting.EnterFromCurrent(now);}
            else petting.SetHover(inside,now);
            var pose=petting.Sample(now);
            if(pose.state=="idle"){if(state=="petting"){SetState("idle");stateStart=petting.IdleOrigin;pettingIdleOrigin=petting.IdleOrigin;}return false;}
            SetState("petting");string pettingKey="petting:"+string.Join("|",pose.layers.Select(f=>f.id+":"+f.weight.ToString("R",CultureInfo.InvariantCulture)));
            if(pettingKey!=frameKey){picture.Source=app.Art.PettingFrame(pose.layers);frameKey=pettingKey;FrameUpdates++;}gaze=-1;return true;
        }
        void Render() {
            if(RenderPetting(clock.Elapsed.TotalMilliseconds))return;
            double now=clock.Elapsed.TotalMilliseconds, elapsed=now-stateStart;
            string active=state;
            if(!dragging && active!="idle" && active!="running" && active!="complete" && elapsed>=Motion.Durations[active].Sum()*3) {
                if(app.Completion.Active)SetState(app.Completion.AwaitingHandoff?"running":"complete");else
                if(app.Status.State=="running")SetState("running");else if(app.Status.State=="waiting"&&state!="waiting")SetState("waiting");else SetState("idle");
                active=state;elapsed=now-stateStart;
            }
            if(!dragging && active=="idle" && app.Status.State=="running"){SetState("running");active=state;elapsed=0;}
            if(active=="running"&&app.Art.Work!=null){
                var pose=app.Art.Work.Sample(workSession.Elapsed(now),app.Config.Animations);
                if(app.Art.Complete!=null&&app.Completion.TryHandoff(pose,app.Art.Complete.HandoffWorkIndex,app.Config.Animations,now)){
                    SetState("complete");active=state;
                }else{
                if(pose.Key!=frameKey){picture.Source=app.Art.WorkFrame(pose);frameKey=pose.Key;FrameUpdates++;}
                gaze=-1;return;
                }
            }
            if(active=="complete"&&app.Art.Complete!=null){
                var pose=app.Art.Complete.Sample(app.Completion.Elapsed(now),app.Config.Animations);string completeKey="complete:"+pose.Key;
                if(completeKey!=frameKey){picture.Source=app.Art.CompletionFrame(pose);frameKey=completeKey;FrameUpdates++;}
                if(app.Completion.TryPresentPrompt(pose))app.Bubble.Refresh();gaze=-1;return;
            }
            int row=Motion.Rows[active],column=app.Config.Animations?Motion.Frame(Motion.Durations[active],elapsed):0, phase=row==0&&app.Config.Animations?Motion.BlinkPhase(pettingIdleOrigin<0?now:now-pettingIdleOrigin):0;
            if(app.Config.FollowMouse && app.Config.Animations && active=="idle" && !dragging && !hovered) {
                var cursor=WindowNative.Cursor(this);int direction=Motion.Direction(cursor.X-(Left+Width/2),cursor.Y-(Top+Height/2),gaze,app.Config.FollowRadius,38*PetSizing.Scale(app.Config.PetSize));
                if(direction!=gaze && (direction<0||now-gazeChanged>=70)){gaze=direction;gazeChanged=now;}
                if(gaze>=0){row=9+gaze/8;column=gaze%8;phase=0;}
            }else gaze=-1;
            string key=row+":"+column+":"+phase;
            if(key!=frameKey){picture.Source=app.Art.Frame(row,column,phase);frameKey=key;FrameUpdates++;}
        }
    }
    public static class CuteUi {
        public const string Resources=@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <SolidColorBrush x:Key='Ink' Color='#354B54'/><SolidColorBrush x:Key='Muted' Color='#7A898B'/>
 <Style TargetType='TextBlock'><Setter Property='Foreground' Value='{StaticResource Ink}'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13'/></Style>
 <Style TargetType='Button'><Setter Property='Foreground' Value='#435B60'/><Setter Property='Background' Value='#EEF2EE'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='15,8'/><Setter Property='FontSize' Value='13'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Body' CornerRadius='10' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Body' Property='Opacity' Value='0.65'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Body' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='CheckBox'><Setter Property='Foreground' Value='#435B60'/><Setter Property='FontSize' Value='13'/><Setter Property='Margin' Value='0,6'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/></Style>
 <Style x:Key='SizeChoice' TargetType='RadioButton'><Setter Property='Foreground' Value='#435B60'/><Setter Property='Background' Value='#EEF2EE'/><Setter Property='Padding' Value='22,9'/><Setter Property='Margin' Value='0,0,8,0'/><Setter Property='FontSize' Value='13'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'><Border x:Name='Body' Background='{TemplateBinding Background}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Body' Property='Background' Value='#78988A'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Opacity' Value='0.82'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='TextBox'><Setter Property='Foreground' Value='#435B60'/><Setter Property='Background' Value='#F7F9F6'/><Setter Property='BorderBrush' Value='#DDE5DF'/><Setter Property='Padding' Value='10,7'/><Setter Property='FontSize' Value='13'/></Style>
 <Style TargetType='TabItem'><Setter Property='Foreground' Value='#6E8185'/><Setter Property='Padding' Value='24,10'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'><Border x:Name='Tab' Background='Transparent' CornerRadius='10' Margin='3,0' Padding='{TemplateBinding Padding}'><ContentPresenter ContentSource='Header' HorizontalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Tab' Property='Background' Value='#E7EFE9'/><Setter Property='Foreground' Value='#3F6861'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='TabControl'><Setter Property='BorderThickness' Value='0'/><Setter Property='Background' Value='Transparent'/><Setter Property='Padding' Value='0,12,0,0'/></Style>
</ResourceDictionary>";
        public static ResourceDictionary Create(){return (ResourceDictionary)XamlReader.Parse(Resources);}
        public static void SaveRender(FrameworkElement element,string path) {
            element.UpdateLayout();var r=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);r.Render(element);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(r));using(var f=File.Create(path))encoder.Save(f);
        }
    }
}
