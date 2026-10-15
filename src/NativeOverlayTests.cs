using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
namespace DesktopKitten {
    public static class NativeOverlayTests {
        public static async Task LiveNavigation(KittenApp app,string output){
            Directory.CreateDirectory(output);var json=new JavaScriptSerializer();var checks=new List<string>();var samples=new List<object>();Exception failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
            try{
                check(app.Qa,"live navigation verification uses isolated QA profile");app.Config.MonitorCodex=true;app.Config.WaterIntervalEnabled=false;app.Config.MealEnabled=false;app.Config.Pending.Clear();
                await Task.Run(()=>app.Codex.PollForDiagnostics());check(app.Codex.Current.Connected,"live task list connects from standalone candidate process");
                string context=app.Codex.ContextId;check(!string.IsNullOrWhiteSpace(context),"existing caller automatically discovered from local profile");
                var target=app.Codex.Current.Tasks.FirstOrDefault(t=>t.Id==context);check(target!=null&&target.Relevant,"current caller is a real relevant local task; no synthetic chat or unread state");app.Badge.Refresh();await ExpandReady(app);app.Badge.QaRealNavigationTarget=context;
                await app.Badge.QaScript("window.chrome.webview.postMessage(JSON.stringify({nonce:window.__grassFishBoot.nonce,action:'open',id:"+json.Serialize(target.Id)+",turnKey:"+json.Serialize(target.TurnKey)+"}))");
                for(int i=0;i<100&&!app.Badge.QaRealNavigationConfirmed;i++)await Task.Delay(100);check(app.Badge.QaRealNavigationConfirmed,"real navigation of existing current conversation acknowledged by Codex");
                for(int i=0;i<4;i++){await Task.Delay(1500);await Task.Run(()=>app.Codex.PollForDiagnostics());app.Badge.Refresh();samples.Add(new{sample=i,app.Codex.Current.Connected,app.Codex.Current.DesktopRunning,app.Codex.Current.Stale,notifications=app.Badge.ActivityCount,visible=app.Badge.IsVisible,app.Badge.Ready,reason=app.Badge.LastVisibilityReason});check(app.Badge.IsVisible,"entry remains after real navigation refresh "+i);check(app.Codex.Current.Connected&&app.Badge.Ready,"real post-navigation task list and expanded renderer stay usable "+i);}
                await app.Badge.Capture(Path.Combine(output,"live-after-navigation.png"));app.Badge.Collapse();app.Badge.QaExpireWarmCache();check(await app.Badge.WaitReleased(3000),"live navigation QA releases its own renderer");
            }catch(Exception e){failure=e;}
            File.WriteAllText(Path.Combine(output,"live-navigation.json"),json.Serialize(new{ok=failure==null,checks,samples,error=failure==null?null:failure.ToString(),liveMessagesSent=0,tasksStopped=0,newChatsCreated=0,realNavigationTarget="existing caller conversation only",foregroundWindowActivationTested=false}));
        }
        public static async Task Preview(KittenApp app,string output){
            Directory.CreateDirectory(output);var json=new JavaScriptSerializer();var checks=new List<string>();Exception previewFailure=null;
            Action<bool,string> check=(v,n)=>{if(!v)throw new Exception(n);checks.Add(n);};
            try{
                app.Config.WaterIntervalEnabled=false;app.Config.MealEnabled=false;app.Config.Pending.Clear();app.Config.MonitorCodex=true;
                app.Config.PetSize="large";app.Pet.ApplySize();
                var task=new CodexTask{Id="qa-native-1",HostId="local",Kind="codex",Title="你好",Status="active",TurnKey="qa-turn-1",ActivityBody="正在思考",CanReply=true,Updated=1};
                app.Codex.Current=new CodexTasksSnapshot{Connected=true,Detail="隔离QA模拟，不是真实任务",Tasks=new List<CodexTask>{task}};
                var area=WindowNative.WorkArea(app.Pet,new Point(app.Pet.Left,app.Pet.Top));
                app.Pet.PlaceCat(area.Left+area.Width/2-48,area.Top+130);app.Badge.Refresh();
                check(!app.Badge.WebViewAllocated&&app.Badge.ViewCreations==0,"collapsed badge never initializes WebView2");await Phase(app,output,"initial-collapsed");
                app.Badge.Collapse();await Task.Delay(350);await app.Badge.Capture(Path.Combine(output,"01-collapsed.png"));SaveScene(app,output,"01-collapsed");
                check(app.Badge.NativeBadgeText=="1","lightweight numeric badge shows actual activity count");
                await ExpandReady(app);check(app.Badge.ViewCreations==1,"first expansion lazily creates the official embedded view");await Phase(app,output,"first-expanded");
                check((await app.Badge.QaScript("getComputedStyle(document.querySelector('[data-testid=\"avatar-overlay-notification-badge\"]')).backgroundColor==='rgba(0, 0, 0, 0)'"))=="false","official host CSS layer order preserves badge material fallback");
                await app.Badge.Capture(Path.Combine(output,"02-expanded.png"));
                SaveScene(app,output,"02-expanded");
                check((await app.Badge.QaScript("document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"]').length"))=="1","expanded original task pill");
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').click()");await Task.Delay(350);
                check(app.Badge.QaOpenAttempts==1&&app.Badge.QaOpenTarget==task.Id,"single task click reaches the selected original conversation");
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}))");await Task.Delay(250);
                check(app.Badge.QaOpenAttempts==2&&app.Badge.QaOpenTarget==task.Id,"keyboard activation opens the selected conversation");
                app.Badge.QaOpenError="QA模拟跳转失败";
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').click()");await Task.Delay(250);
                check((await app.Badge.QaScript("document.querySelector('[role=status]')?.innerText.includes('QA模拟跳转失败')"))=="true","navigation error is visible instead of silently ignored");
                app.Badge.QaOpenError=null;
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').click()");await Task.Delay(250);
                check((await app.Badge.QaScript("!document.querySelector('[role=status]')"))=="true","successful manual navigation retry clears error");
                int opens=app.Badge.QaOpenAttempts;
                await app.Badge.QaScript("document.querySelector('button[aria-label=\"回复 你好\"]').click()");await Task.Delay(600);
                check(app.Badge.QaOpenAttempts==opens,"reply button does not activate conversation navigation");
                check((await app.Badge.QaScript("!!document.querySelector('textarea')"))=="true","original reply textarea opens");
                check((await app.Badge.QaScript("window.grassFish.qa().disabledStops"))=="true","unsupported stop explicitly disabled");
                await app.Badge.Capture(Path.Combine(output,"03-inline-reply.png"));
                SaveScene(app,output,"03-inline-reply");
                await app.Badge.QaScript("(()=>{let e=document.querySelector('textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(e,'QA消息，不发送到真实Codex');e.dispatchEvent(new Event('input',{bubbles:true}));})()");await Task.Delay(100);
                await app.Badge.QaScript("document.querySelector('textarea').dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}))");await Task.Delay(600);
                for(int wait=0;wait<30&&app.Badge.QaSendAttempts==0;wait++)await Task.Delay(100);
                check(app.Badge.QaSendAttempts==1,"UI message reaches native guard exactly once, no live send (attempts="+app.Badge.QaSendAttempts+")");
                await app.Badge.QaScript("window.chrome.webview.postMessage(JSON.stringify({nonce:'not-the-host-token',action:'send',id:'qa-native-1',turnKey:'qa-turn-1',prompt:'invalid bridge token'}))");await Task.Delay(100);check(app.Badge.QaSendAttempts==1,"invalid bridge token rejected");
                await app.Badge.QaScript("window.chrome.webview.postMessage(JSON.stringify({nonce:window.__grassFishBoot.nonce,action:'send',id:'qa-native-1',turnKey:'old-stale-turn',prompt:'stale target'}))");await Task.Delay(100);check(app.Badge.QaSendAttempts==1,"stale task target rejected");
                await app.Badge.QaScript("window.chrome.webview.postMessage(JSON.stringify({nonce:'invalid-token',action:'open',id:'qa-native-1',turnKey:'qa-turn-1'}));window.chrome.webview.postMessage(JSON.stringify({nonce:window.__grassFishBoot.nonce,action:'open',id:'qa-native-1',turnKey:'old-turn'}))");await Task.Delay(100);
                check(app.Badge.QaOpenAttempts==opens,"invalid token and stale turn cannot navigate to a conversation");
                check((await app.Badge.QaScript("document.querySelector('textarea').value"))=="\"QA消息，不发送到真实Codex\"","native rejection preserves draft");
                check(app.Badge.QaOpenAttempts==opens,"textarea Enter submits follow-up without opening conversation");
                await Task.Delay(400);check(app.Badge.QaSendAttempts==1,"no automatic resend");await app.Badge.Capture(Path.Combine(output,"04-failure-retains-draft.png"));
                int warmCreates=app.Badge.ViewCreations;uint firstBrowser=app.Badge.LastBrowserProcessId;app.Badge.Collapse();await Task.Delay(400);check(app.Badge.WarmCached&&app.Badge.Ready,"brief collapse retains hidden view for bounded warm reopen");
                var warmClock=System.Diagnostics.Stopwatch.StartNew();app.Badge.Expand();check(app.Badge.Ready&&app.Badge.ViewCreations==warmCreates,"warm reopening does not recreate or reload WebView2");double warmMs=warmClock.Elapsed.TotalMilliseconds;for(int w=0;w<50;w++){if(await app.Badge.QaScript("!!document.querySelector('.native-tray')")=="true")break;await Task.Delay(20);}double warmContentMs=warmClock.Elapsed.TotalMilliseconds;await Task.Delay(250);
                check(app.Badge.WarmReopens>0,"warm reuse counter records immediate reopening");
                await Phase(app,output,"warm-reopened");
                app.Badge.Collapse();app.Badge.QaExpireWarmCache();check(await app.Badge.WaitReleased(3000),"idle expiry disposes the expanded view");
                check(await BrowserExited(firstBrowser,12000),"own WebView2 browser process exits after collapse");check(app.Badge.DraftCount==1,"draft is owned by host memory after renderer exit");await Phase(app,output,"first-released");
                await ExpandReady(app);
                await app.Badge.QaScript("document.querySelector('button[aria-label=\"回复 你好\"]').click()");await Task.Delay(450);
                check((await app.Badge.QaScript("document.querySelector('textarea').value"))=="\"QA消息，不发送到真实Codex\"","draft survives collapsed panel reopening");
                app.Badge.Collapse();app.Badge.QaExpireWarmCache();await app.Badge.WaitReleased(3000);task.ActivityBody="已运行命令";app.Codex.Current.Tasks.Add(new CodexTask{Id="qa-native-2",HostId="local",Kind="codex",Title="第二个任务",Status="needsAttention",TurnKey="qa-turn-2",ActivityBody="需要你的输入",CanReply=true,Updated=2});app.Badge.Refresh();await ExpandReady(app);
                await app.Badge.Capture(Path.Combine(output,"05-multiple-stacked.png"));
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').click()");await Task.Delay(550);
                check(app.Badge.QaOpenAttempts==opens,"first click on stacked tasks expands without opening wrong conversation");
                check((await app.Badge.QaScript("document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"]').length"))=="2","native task stack expands both tasks");await app.Badge.Capture(Path.Combine(output,"06-multiple-expanded.png"));
                await app.Badge.QaScript("[...document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]')].find(e=>e.innerText.includes('第二个任务')).click()");await Task.Delay(250);
                check(app.Badge.QaOpenAttempts==opens+1&&app.Badge.QaOpenTarget=="qa-native-2","expanded second task opens second conversation, not the first");
                var positions=new[]{new Point(area.Left+10,area.Top+10),new Point(area.Right-app.Pet.Width-10,area.Top+10),new Point(area.Left+10,area.Bottom-app.Pet.Height-50),new Point(area.Right-app.Pet.Width-10,area.Bottom-app.Pet.Height-50)};var geometry=new List<object>();
                for(int i=0;i<positions.Length;i++){app.Pet.PlaceCat(positions[i].X,positions[i].Y);app.Badge.Position();await Task.Delay(450);var p=app.Badge.Placement;check(area.Contains(p.Badge)&&area.Contains(p.Tray),"all native content inside work area corner "+i);check(!p.Tray.IntersectsWith(new Rect(app.Pet.Left,app.Pet.Top,app.Pet.Width,app.Pet.Height)),"tray avoids the actual cat artwork corner "+i);geometry.Add(new{corner=i,window=p.Window,badge=p.Badge,tray=p.Tray,above=p.TrayAbove});await app.Badge.Capture(Path.Combine(output,"corner-"+i+".png"));}
                File.WriteAllText(Path.Combine(output,"dom-diagnostics.json"),await app.Badge.QaScript("JSON.stringify((()=>{let e=document.querySelector('[data-testid=\"avatar-overlay-notification-badge\"]');let s=getComputedStyle(e);return {sheets:[...document.styleSheets].map(s=>({href:s.href,rules:s.cssRules.length})),badgeClass:e.className,badgeStyle:e.getAttribute('style'),badgeColor:s.color,badgeBorder:s.borderColor,badgeBackground:s.backgroundColor,infoSoft:s.getPropertyValue('--color-background-info-soft'),materialBackground:s.getPropertyValue('--avatar-overlay-material-background'),viewport:[innerWidth,innerHeight]}})())"));
                File.WriteAllText(Path.Combine(output,"placement.json"),json.Serialize(geometry));
                for(int i=3;i<=8;i++)app.Codex.Current.Tasks.Add(new CodexTask{Id="qa-native-"+i,HostId="local",Kind="codex",Title="任务 "+i,Status="active",TurnKey="qa-turn-"+i,ActivityBody="正在思考",CanReply=true,Updated=i});app.Badge.Refresh();await Task.Delay(650);
                check((await app.Badge.QaScript("document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"]').length"))=="8","all eight task rows remain accessible when expanded");
                int published=app.Badge.PublishCount,shaped=app.Badge.ShapeUpdates;for(int i=0;i<30;i++){app.Badge.Refresh();app.Badge.Position();}await Task.Delay(500);
                check(app.Badge.PublishCount==published,"unchanged snapshots do not execute redundant browser scripts");check(app.Badge.ShapeUpdates==shaped,"unchanged geometry does not recreate native window regions");
                var draftCycles=new List<object>();for(int i=0;i<3;i++){uint pid=app.Badge.LastBrowserProcessId;app.Badge.Collapse();app.Badge.QaExpireWarmCache();check(await app.Badge.WaitReleased(3000),"cycle "+i+" disposes controller");check(await BrowserExited(pid,12000),"cycle "+i+" exits its browser process");await ExpandReady(app);draftCycles.Add(new{cycle=i,hostDrafts=app.Badge.DraftCount,page=await app.Badge.QaScript("JSON.stringify(window.grassFish.exportState())")});}File.WriteAllText(Path.Combine(output,"draft-cycles.json"),json.Serialize(draftCycles));
                await app.Badge.QaScript("document.querySelector('[data-avatar-overlay-hit-region=\"activity-pill\"] [role=\"button\"]').click()");await Task.Delay(400);
                await app.Badge.QaScript("document.querySelector('button[aria-label=\"回复 你好\"]').click()");await Task.Delay(400);
                string finalDraft=await app.Badge.QaScript("document.querySelector('textarea')?.value");check(finalDraft=="\"QA消息，不发送到真实Codex\"","host draft survives several full renderer recreations, actual="+finalDraft);
                uint finalBrowser=app.Badge.LastBrowserProcessId;app.Badge.Collapse();app.Badge.QaExpireWarmCache();await app.Badge.WaitReleased(3000);check(await BrowserExited(finalBrowser,12000),"last expanded browser exits");await Phase(app,output,"cycles-released");
                app.Codex.Current.Connected=false;app.Badge.Refresh();check(!app.Badge.IsVisible&&!app.Badge.WebViewAllocated,"Codex disconnect hides badge without starting renderer");
                app.Badge.Expand();await Task.Delay(500);check(!app.Badge.WebViewAllocated,"offline expansion cannot initialize browser");check(app.Badge.DraftCount==1,"Codex disconnect preserves memory-only draft");await Phase(app,output,"offline");
                app.Codex.Current.Connected=true;app.Badge.Collapse();app.Badge.Refresh();
                for(int i=0;i<4;i++){app.Badge.Expand();await Task.Delay(40);app.Badge.Collapse();app.Badge.QaExpireWarmCache();await app.Badge.WaitReleased(3000);}
                await Task.Delay(300);check(!app.Badge.WebViewAllocated,"rapid load cancellation leaves no active controller");
                for(int i=0;i<3;i++){app.Badge.Expand();await Task.Delay(200);app.Badge.Collapse();app.Badge.QaExpireWarmCache();check(await app.Badge.WaitReleased(16000),"initialization race "+i+" fully disposes late controller");}
                await ExpandReady(app);uint disconnectedPid=app.Badge.LastBrowserProcessId;app.Codex.Current.Connected=false;app.Badge.Refresh();check(await app.Badge.WaitReleased(3000)&&await BrowserExited(disconnectedPid,12000),"disconnect while expanded releases renderer");app.Codex.Current.Connected=true;app.Badge.Refresh();
                await ExpandReady(app);uint disabledPid=app.Badge.LastBrowserProcessId;app.Config.MonitorCodex=false;app.Badge.Refresh();check(await app.Badge.WaitReleased(3000)&&await BrowserExited(disabledPid,12000),"disabling integration while expanded releases renderer");app.Config.MonitorCodex=true;app.Badge.Refresh();
                await ExpandReady(app);uint hiddenPid=app.Badge.LastBrowserProcessId;app.HidePet();check(await app.Badge.WaitReleased(3000)&&await BrowserExited(hiddenPid,12000),"hiding pet while expanded releases renderer");app.ShowPet(false);
                foreach(uint pid in app.Badge.CreatedBrowserPids)check(await BrowserExited(pid,12000),"all tracked browser processes exit, including cancelled initialization "+pid);
                await Phase(app,output,"rapid-toggle-released");
                var stress=new List<object>();for(int i=0;i<6;i++){await ExpandReady(app);uint pid=app.Badge.LastBrowserProcessId;app.Badge.Collapse();app.Badge.QaExpireWarmCache();check(await app.Badge.WaitReleased(3000)&&await BrowserExited(pid,12000),"extended release cycle "+i+" exits browser");using(var process=System.Diagnostics.Process.GetCurrentProcess()){stress.Add(new{cycle=i,managedBytes=GC.GetTotalMemory(false),workingSetBytes=process.WorkingSet64,privateCommitBytes=process.PrivateMemorySize64});}}File.WriteAllText(Path.Combine(output,"extended-cycles.json"),json.Serialize(stress));await Phase(app,output,"extended-cycles-released");
                var keep=app.Codex.Current;app.Codex.Current=new CodexTasksSnapshot{Connected=true,DesktopRunning=true};app.Badge.Refresh();check(app.Badge.IsVisible&&app.Badge.NativeBadgeText=="0","zero tasks retains the lightweight entry while Codex runs");await app.Badge.Capture(Path.Combine(output,"07-empty-badge.png"));
                app.Codex.Current=keep;app.Badge.Refresh();await ExpandReady(app);task.Status="idle";task.Unread=false;app.Badge.Refresh();check(app.Badge.IsVisible&&app.Badge.Ready,"navigation/read state cannot close an expanded tray");
                app.Codex.Current=new CodexTasksSnapshot{DesktopRunning=true,Stale=true,Tasks=keep.Tasks,Detail="QA temporary list failure"};app.Badge.Refresh();check(app.Badge.IsVisible&&app.Badge.NativeBadgeText=="…","temporary list failure retains a reconnect entry");check(await app.Badge.WaitReleased(3000),"reconnect releases rendering rather than keeping a stale live editor");await app.Badge.Capture(Path.Combine(output,"08-reconnecting.png"));app.Codex.Current=keep;app.Badge.Refresh();await ExpandReady(app);check(app.Badge.IsVisible&&app.Badge.Ready,"successful retry restores expanded tray without another click");
                app.Badge.Collapse();uint timedPid=app.Badge.LastBrowserProcessId;var expiry=System.Diagnostics.Stopwatch.StartNew();for(int n=0;n<70&&app.Badge.WebViewAllocated;n++){app.Badge.Refresh();await Task.Delay(250);}check(!app.Badge.WebViewAllocated&&await app.Badge.WaitReleased(3000)&&await BrowserExited(timedPid,12000),"real 15 second idle deadline releases warm view even with repeated refreshes");double naturalExpiryMs=expiry.Elapsed.TotalMilliseconds;await Phase(app,output,"natural-idle-released");
                File.WriteAllText(Path.Combine(output,"latency.json"),json.Serialize(new{coldLastMs=app.Badge.LastColdLoadMilliseconds,warmHostReopenMs=warmMs,warmContentMs,warmReuses=app.Badge.WarmReopens,naturalExpiryMs,policy="15 seconds hidden warm cache, then dispose; no permanent renderer"}));
                File.WriteAllText(Path.Combine(output,"native-qa.json"),json.Serialize(new{ok=true,checks,liveMessagesSent=0,liveTasksStopped=0,oldTaskWindowRemoved=true,source="actual original components inside desktop kitten WPF host",error=app.Badge.Error}));
            }catch(Exception e){previewFailure=e;}
            if(previewFailure!=null){string dom=null;try{if(app.Badge.Ready){dom=await app.Badge.QaScript("JSON.stringify({text:document.body.innerText,textarea:document.querySelector('textarea')?.value,exported:window.grassFish.exportState(),qa:window.grassFish.qa()})");await app.Badge.Capture(Path.Combine(output,"failure.png"));}}catch(Exception){}File.WriteAllText(Path.Combine(output,"native-qa.json"),json.Serialize(new{ok=false,checks,error=previewFailure.ToString(),viewError=app.Badge.Error,app.Badge.QaSendAttempts,dom,liveMessagesSent=0}));}
        }
        static async Task ExpandReady(KittenApp app){app.Badge.Expand();if(!await app.Badge.WaitReady(16000))throw new Exception("expanded official view not ready: "+app.Badge.Error);await Task.Delay(500);}
        static async Task<bool> BrowserExited(uint pid,int timeout){for(int elapsed=0;elapsed<timeout;elapsed+=200){try{using(var p=System.Diagnostics.Process.GetProcessById((int)pid)){if(p.HasExited)return true;}}catch(ArgumentException){return true;}await Task.Delay(200);}return false;}
        static async Task Phase(KittenApp app,string output,string name){WritePhase(app,output,name,true);await Task.Delay(6000);WritePhase(app,output,name,false);}
        static void WritePhase(KittenApp app,string output,string name,bool active){File.WriteAllText(Path.Combine(output,"phase.json"),new JavaScriptSerializer().Serialize(new{phase=name,active,atUtc=DateTimeOffset.UtcNow.ToString("o"),pid=System.Diagnostics.Process.GetCurrentProcess().Id,app.Badge.WebViewAllocated,app.Badge.Ready,app.Badge.ViewCreations,app.Badge.ViewReleases,app.Badge.DraftCount,app.Badge.PublishCount,app.Badge.ShapeUpdates,app.Badge.LastBrowserProcessId}));}
        static void SaveScene(KittenApp app,string output,string name){
            var cat=new Rect(app.Pet.Left,app.Pet.Top,app.Pet.Width,app.Pet.Height);var window=app.Badge.Placement.Window;var bounds=Rect.Union(cat,window);bounds.Inflate(25,25);
            var shot=new System.Windows.Media.Imaging.BitmapImage();shot.BeginInit();shot.UriSource=new Uri(Path.Combine(output,name+".png"));shot.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;shot.EndInit();
            var visual=new System.Windows.Media.DrawingVisual();using(var d=visual.RenderOpen()){
                d.DrawRectangle(new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(220,215,199),System.Windows.Media.Color.FromRgb(161,164,141),45),null,new Rect(0,0,bounds.Width,bounds.Height));
                d.DrawRectangle(new System.Windows.Media.VisualBrush(app.Pet),null,new Rect(cat.Left-bounds.Left,cat.Top-bounds.Top,cat.Width,cat.Height));
                d.DrawImage(shot,new Rect(window.Left-bounds.Left,window.Top-bounds.Top,window.Width,window.Height));
            }
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(bounds.Width*1.5),(int)Math.Ceiling(bounds.Height*1.5),144,144,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,name+"-scene.png")))enc.Save(stream);
        }
        public static void Run(Action<bool,string> check){
            using(var bridge=new CodexConnection("fixture-home","fixture-context")){
                var docList=new JavaScriptSerializer().DeserializeObject("{\"threads\":[{\"id\":\"A\",\"kind\":\"codex\",\"hostId\":\"local\",\"title\":\"fixture\",\"status\":\"active\"}]}");
                bridge.Current=bridge.RefreshCycle(true,(tool,args)=>{if(tool=="list_threads")return docList;throw new IOException("fixture detail failed");});
                check(bridge.Current.Connected&&bridge.Current.DetailFailures==1&&bridge.Current.Count==1,"one detail failure cannot disconnect or empty a successful task list");
                bridge.Current=bridge.RefreshCycle(true,(tool,args)=>{throw new IOException("fixture list failed");});check(!bridge.Current.Connected&&bridge.Current.DesktopRunning&&bridge.Current.Stale&&bridge.Current.Tasks.Count==1&&!bridge.Current.Tasks[0].CanReply,"list failure retains read-only stale tasks and desktop presence");
                bool called=false;bridge.Current=bridge.RefreshCycle(false,(tool,args)=>{called=true;return null;});check(!called&&!bridge.Current.DesktopRunning,"Codex not running causes zero backend calls");
                bridge.Current=bridge.RefreshCycle(true,(tool,args)=>tool=="list_threads"?docList:new JavaScriptSerializer().DeserializeObject("{\"turns\":[]}"));check(bridge.Current.Connected&&!bridge.Current.Stale&&bridge.Current.Tasks[0].CanReply,"fresh task list restores safe actions after retry");
                var navLedger=new NativeActivityLedger();var unread=new CodexTask{Id="U",Kind="codex",HostId="local",Status="idle",Unread=true,TurnKey="read-turn"};var snap=new CodexTasksSnapshot{Connected=true,Tasks=new List<CodexTask>{unread}};navLedger.Apply(snap);navLedger.RetainOpened("U","read-turn");unread.Unread=false;navLedger.Apply(snap);check(navLedger.Items.Count==1,"opening unread-only task retains selected card until dismissed");
            }
            var state=new TaskViewState();check(state.Set("A","草稿"),"host accepts memory-only draft");check(state.Get("A")=="草稿","host owns draft independently of renderer");
            check(state.Begin("A","t1","草稿"),"host records explicit pending send");check(!state.Begin("A","t1","草稿"),"pending send cannot duplicate after reopen");
            var versions=state.Versions();state.Finish("A","t1",true,null);var stale=new JavaScriptSerializer().DeserializeObject("{\"drafts\":[{\"id\":\"A\",\"text\":\"草稿\"}]}");state.Import(stale,versions);check(state.Get("A")=="","late release export cannot resurrect a successfully sent draft");
            state.Begin("A","t2","保留我");state.Set("A","新的未发送编辑");state.Finish("A","t2",true,null);check(state.Get("A")=="新的未发送编辑","send completion preserves a newer edit");
            state.Begin("A","t3","失败草稿");state.Finish("A","t3",false,"结果未知，不自动重发");check(state.Get("A")=="失败草稿"&&state.PendingCount==0,"failed or unknown delivery preserves draft without automatic resend");
            check(!state.Set("A",new string('x',20001)),"draft memory is bounded per conversation");check(!state.Begin("A","t4"," "),"empty send rejected");
            var finalVersions=state.Versions();state.Import(new JavaScriptSerializer().DeserializeObject("{\"drafts\":[{\"id\":\"A\",\"text\":\"最后的输入\"}]}"),finalVersions);check(state.Get("A")=="最后的输入","final export captures latest input before dispose");
            var bounded=new TaskViewState();for(int i=0;i<105;i++){check(bounded.Begin("chat"+i,"turn","message"),"completed empty drafts permit future chats "+i);bounded.Finish("chat"+i,"turn",true,null);}check(bounded.DraftCount==0,"successful messages leave no unsent drafts");
            var ledger=new NativeActivityLedger();var t=new CodexTask{Id="T",Kind="codex",HostId="local",Title="原标题",Status="idle",TurnKey="turn1"};
            var s=new CodexTasksSnapshot{Connected=true,Tasks=new List<CodexTask>{t}};ledger.Apply(s);check(ledger.Items.Count==0,"idle recent chat is not a notification");
            t.Status="active";ledger.Apply(s);check(ledger.Items.Count==1&&ledger.Items[0].IsLoading,"running creates notification");ledger.Dismiss("T","turn1");ledger.Apply(s);check(ledger.Items.Count==0,"dismiss survives same-turn refresh");
            t.TurnKey="turn2";ledger.Apply(s);check(ledger.Items.Count==1,"new turn returns notification");t.Status="idle";ledger.Apply(s);check(ledger.Items.Count==1&&!ledger.Items[0].IsLoading,"observed completion retains ready notification");
            ledger.Apply(new CodexTasksSnapshot{Connected=false});check(ledger.Items.Count==1,"disconnect keeps existing notification but host disables send");
            ledger.Apply(new CodexTasksSnapshot{Connected=true});check(ledger.Items.Count==0,"removed chat removed from notifications");
            var doc=new JavaScriptSerializer().DeserializeObject("{\"turns\":[{\"id\":\"latest\",\"items\":[{\"type\":\"commandExecution\",\"status\":\"completed\"}]}]}");t.Status="active";check(NativeActivityLedger.ActivityBody(doc,t)=="已运行命令"&&t.TurnKey=="latest","latest actual activity label and turn identity");
            foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-1280,-100,1280,720),new Rect(0,0,1024,600)})foreach(var size in new[]{new Size(96,104),new Size(192,208),new Size(272,248)})for(int x=0;x<=6;x++)for(int y=0;y<=6;y++)foreach(var height in new[]{56.0,112.0,220.0,420.0}){
                var cat=new Rect(area.Left+(area.Width-size.Width)*x/6,area.Top+(area.Height-size.Height)*y/6,size.Width,size.Height);var p=NativeTaskPlacement.Place(cat,area,true,height);check(area.Contains(p.Badge)&&area.Contains(p.Tray),"native badge and task tray stay in work area");check(!cat.IntersectsWith(p.Tray),"native task tray avoids cat artwork");
            }
        }
    }
}
