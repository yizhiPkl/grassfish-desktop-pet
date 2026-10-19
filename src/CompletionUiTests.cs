using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DesktopKitten {
    public static class CompletionUiTests {
        static void Save(BitmapSource bitmap,string path){var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))e.Save(f);}
        static void Preview(KittenApp app,double elapsed){app.Completion.Preview(app.Pet.AnimationTime-elapsed);app.Pet.UpdateStatus();app.Pet.QaRender();app.Bubble.PrepareContent();}
        static void Close(KittenApp app){((Button)((FrameworkElement)app.Bubble.Content).FindName("Dismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        public static async Task Run(KittenApp app,string output){
            Directory.CreateDirectory(output);var checks=new List<string>();Exception failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
            try{
                check(app.Qa&&app.HeadlessQa,"completion tests use isolated settings and no visible desktop windows");app.Config.MonitorCodex=false;app.Config.FollowMouse=false;app.Config.Animations=true;app.Config.Pending.Clear();app.Config.WaterIntervalEnabled=false;app.Config.MealEnabled=false;
                var clip=app.Art.Complete;
                foreach(var size in new[]{"small","medium","large"}){
                    app.Config.PetSize=size;app.Pet.ApplySize();Preview(app,clip.EntryDurationMs+1200);double scale=PetSizing.Scale(size);
                    check(Math.Abs(app.Pet.Width-clip.Width*scale)<.001&&Math.Abs(app.Pet.CatBounds.Width-192*scale)<.001,"completion canvas preserves cat scale: "+size);
                    var before=Tests.Pixels(app.Pet.QaDisplayedFrame);Save(app.Pet.QaDisplayedFrame,Path.Combine(output,size+"-before-close.png"));Close(app);
                    check(!app.Completion.Active&&app.Pet.ActiveState=="complete-return","actual close button acknowledges immediately and starts tail recovery: "+size);
                    app.Pet.QaRender();check(before.SequenceEqual(Tests.Pixels(app.Pet.QaDisplayedFrame)),"close starts from the displayed tail phase without snapping: "+size);
                    var until=DateTime.UtcNow.AddSeconds(4);while(app.Pet.ActiveState=="complete-return"&&DateTime.UtcNow<until){await Task.Delay(25);app.Pet.QaRender();}
                    check(app.Pet.ActiveState=="idle"&&Tests.Pixels(app.Pet.QaDisplayedFrame).SequenceEqual(Tests.Pixels(app.Art.Frame(0,0,0))),"tail recovery reaches the actual original idle pixels: "+size);
                    check(Math.Abs(app.Pet.Width-192*scale)<.001,"original idle returns at exactly the same cat scale: "+size);Save(app.Pet.QaDisplayedFrame,Path.Combine(output,size+"-after-idle.png"));
                }
                Preview(app,250);Close(app);check(app.Pet.ActiveState=="complete-return","closing during keyboard exit uses forward recovery rather than teleporting to idle");
                app.Status=new Snapshot{State="running",Connected=true};app.Pet.UpdateStatus();app.Pet.QaRender();check(app.Pet.ActiveState=="running","new task preempts completion recovery immediately");
                app.Status=new Snapshot{State="idle",Connected=true};app.Pet.UpdateStatus();Preview(app,clip.EntryDurationMs+500);Close(app);app.PreviewCompletion();app.Pet.QaRender();check(app.Completion.Active&&app.Pet.ActiveState=="complete","a fresh completion cancels previous recovery cleanly");
                app.Config.Animations=false;Close(app);app.Pet.QaRender();check(app.Pet.ActiveState=="idle","reduced motion skips animated recovery");
                check(!app.Pet.IsVisible&&!app.Bubble.IsVisible&&!app.Badge.IsVisible&&!app.Badge.WebViewAllocated,"verification never shows a desktop window or starts task WebView");
            }catch(Exception e){failure=e;}
            File.WriteAllText(Path.Combine(output,"completion-ui-qa.json"),new JavaScriptSerializer().Serialize(new{ok=failure==null,checks,error=failure==null?null:failure.ToString(),headless=true,liveMessagesSent=0,realCodexConnectionStarted=false,installedVersionModified=false}));
        }
    }
}
