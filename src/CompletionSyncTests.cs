using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopKitten {
    // An isolated WPF fixture. It never connects to Codex or sends a real task.
    public static class CompletionSyncTests {
        static readonly DateTimeOffset epoch=DateTimeOffset.Parse("2026-10-15T00:00:00Z");
        sealed class Observation {
            public string Name;
            public bool EarlyPrompt;
            public double FirstCompletionFrameMs=-1, FirstWaveMs=-1, FirstPromptMs=-1;
            public List<object> Frames=new List<object>();
        }
        static void Status(KittenApp app,string state,int end){
            app.Status=new Snapshot{State=state,Connected=true,EndAt=epoch.AddSeconds(end)};
            app.Pet.UpdateStatus();app.Bubble.Refresh();
        }
        static bool Waving(KittenApp app){return app.Pet.ActiveState=="complete"&&(app.Pet.CurrentFrame??"").StartsWith("complete:work:loop:",StringComparison.Ordinal);}
        static bool CompletionDisplayed(KittenApp app){return app.Pet.ActiveState=="complete"&&(app.Pet.CurrentFrame??"").StartsWith("complete:work:",StringComparison.Ordinal);}
        static void Dismiss(KittenApp app){((Button)((FrameworkElement)app.Bubble.Content).FindName("Dismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static async Task<Observation> Observe(KittenApp app,string output,string name){
            var result=new Observation{Name=name};var time=Stopwatch.StartNew();string previous=null;bool savedEntry=false;
            for(int i=0;i<600;i++){
                bool wave=Waving(app),visible=app.Bubble.IsVisible;
                bool completion=CompletionDisplayed(app);
                if(completion&&result.FirstCompletionFrameMs<0)result.FirstCompletionFrameMs=time.Elapsed.TotalMilliseconds;
                if(wave&&result.FirstWaveMs<0)result.FirstWaveMs=time.Elapsed.TotalMilliseconds;
                if(visible&&result.FirstPromptMs<0)result.FirstPromptMs=time.Elapsed.TotalMilliseconds;
                if(visible&&!completion)result.EarlyPrompt=true;
                string key=app.Pet.CurrentFrame+";"+visible;
                if(key!=previous){result.Frames.Add(new{atMs=time.Elapsed.TotalMilliseconds,state=app.Pet.ActiveState,frame=app.Pet.CurrentFrame,bubbleVisible=visible,awaitingHandoff=app.Completion.AwaitingHandoff});previous=key;}
                if(!savedEntry&&app.Pet.ActiveState=="complete"&&!wave){SaveScene(app,Path.Combine(output,name+"-收键盘.png"));savedEntry=true;}
                if(wave&&visible){SaveScene(app,Path.Combine(output,name+"-挥手与提示.png"));break;}
                await Task.Delay(20);
            }
            return result;
        }
        public static async Task Preview(KittenApp app,string output){
            Directory.CreateDirectory(output);var checks=new List<string>();var observations=new List<Observation>();Exception failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
            try{
                check(app.Qa,"isolated QA profile and no production singleton");app.Config.MonitorCodex=true;app.Config.Animations=true;app.Config.FollowMouse=false;app.Config.WaterIntervalEnabled=false;app.Config.MealEnabled=false;app.Config.Pending.Clear();
                app.Config.PetSize="large";app.Pet.ApplySize();var area=WindowNative.WorkArea(app.Pet,new Point(app.Pet.Left,app.Pet.Top));app.Pet.PlaceCat(area.Left+area.Width*.45,area.Top+area.Height*.45);
                Status(app,"idle",0);Status(app,"running",0);await Task.Delay(220);Status(app,"review",1);
                var actual=await Observe(app,output,"01-真实完成流程模拟");observations.Add(actual);
                check(actual.FirstWaveMs>=0&&actual.FirstPromptMs>=0,"natural keyboard handoff and completion transition reach the visible wave");
                check(!actual.EarlyPrompt,"completion prompt never precedes the rendered keyboard exit");
                check(Math.Abs(actual.FirstPromptMs-actual.FirstCompletionFrameMs)<=75,"prompt and first rendered keyboard exit frame are observed together within one animation tick");
                Status(app,"idle",1);await Task.Delay(150);check(app.Completion.Active&&app.Bubble.IsVisible,"completion survives short-lived review state");
                await Task.Delay(app.Art.Complete.LoopDurationMs+100);check(Waving(app)&&app.Bubble.IsVisible,"waving loop and prompt persist without repeating keyboard exit");
                Dismiss(app);await Task.Delay(100);check(!app.Completion.Active&&!app.Bubble.IsVisible,"actual close button acknowledges completion");

                app.PreviewCompletion();var preview=await Observe(app,output,"02-手动预览");observations.Add(preview);check(!preview.EarlyPrompt&&preview.FirstPromptMs>=0&&preview.FirstPromptMs<preview.FirstWaveMs,"manual preview announces the keyboard exit before the waving loop");Dismiss(app);

                app.Config.Animations=false;Status(app,"running",1);await Task.Delay(100);Status(app,"review",2);
                var reduced=await Observe(app,output,"03-关闭动画");observations.Add(reduced);check(!reduced.EarlyPrompt&&reduced.FirstPromptMs>=0&&reduced.FirstPromptMs<750,"reduced motion shows the static no-keyboard pose and prompt without a fixed delay");Dismiss(app);app.Config.Animations=true;Status(app,"idle",2);

                app.PreviewCompletion();await Task.Delay(250);check(app.Bubble.IsVisible&&CompletionDisplayed(app),"transitioning preview displays its prompt with the keyboard exit");Status(app,"running",2);await Task.Delay(app.Art.Complete.EntryDurationMs+200);
                check(!app.Completion.Active&&!app.Bubble.IsVisible&&app.Pet.ActiveState=="running","new work cancels the old transition without a delayed stale prompt");Status(app,"idle",2);

                app.PreviewCompletion();await Task.Delay(180);app.HidePet();app.Bubble.Refresh();await Task.Delay(app.Art.Complete.EntryDurationMs+200);check(!app.Pet.IsVisible&&!app.Bubble.IsVisible,"hidden pet never produces a delayed floating prompt");
                app.ShowPet(false);var resumed=await Observe(app,output,"04-隐藏后恢复");observations.Add(resumed);check(!resumed.EarlyPrompt&&resumed.FirstPromptMs>=0,"restored pet presents its wave before showing the pending completion");Dismiss(app);

                app.Scheduler.AddPreview("water");app.Bubble.Refresh();check(app.Config.Pending.Count==1&&app.Bubble.IsVisible,"existing local reminder is visible before completion");
                app.PreviewCompletion();await Task.Delay(200);check(app.Bubble.IsVisible&&app.Completion.Active&&app.Config.Pending.Count==1,"completion transition shows its own prompt and preserves the reminder queue");
                var queued=await Observe(app,output,"05-保留日程提醒");observations.Add(queued);check(!queued.EarlyPrompt&&queued.FirstPromptMs>=0,"queued reminder cannot make the completion prompt appear early");Dismiss(app);
                check(!app.Completion.Active&&app.Config.Pending.Count==1&&app.Bubble.IsVisible,"closing completion restores the same queued reminder");Dismiss(app);check(app.Config.Pending.Count==0&&!app.Bubble.IsVisible,"local reminder close still removes only its own reminder");

                app.PreviewCompletion();await Task.Delay(150);app.Config.MonitorCodex=false;Status(app,"review",3); // Manual preview remains valid without the optional Codex connection.
                var offline=await Observe(app,output,"06-离线预览");observations.Add(offline);check(!offline.EarlyPrompt&&offline.FirstPromptMs>=0,"manual preview still synchronizes with Codex monitoring disabled");Dismiss(app);
                check(!app.Badge.WebViewAllocated&&app.Badge.ViewCreations==0,"completion synchronization never initializes a task browser");
            }catch(Exception e){failure=e;}
            File.WriteAllText(Path.Combine(output,"completion-sync-qa.json"),new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(new{ok=failure==null,checks,observations,error=failure==null?null:failure.ToString(),liveMessagesSent=0,realCodexConnectionStarted=false,originalPetStopped=false,artworkModified=false}));
        }
        static void SaveScene(KittenApp app,string path){
            app.Pet.UpdateLayout();app.Bubble.UpdateLayout();var bounds=new Rect(app.Pet.Left,app.Pet.Top,app.Pet.Width,app.Pet.Height);
            if(app.Bubble.IsVisible)bounds.Union(new Rect(app.Bubble.Left,app.Bubble.Top,app.Bubble.Width,app.Bubble.Height));bounds.Inflate(24,24);
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(239,242,233)),null,new Rect(0,0,bounds.Width,bounds.Height));
                foreach(Window window in new Window[]{app.Pet,app.Bubble})if(window.IsVisible)dc.DrawRectangle(new VisualBrush(window),null,new Rect(window.Left-bounds.Left,window.Top-bounds.Top,window.Width,window.Height));
            }
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(bounds.Width),(int)Math.Ceiling(bounds.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);
        }
    }
}
