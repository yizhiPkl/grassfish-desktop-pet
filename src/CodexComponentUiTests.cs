using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace DesktopKitten {
    public static class CodexComponentUiTests {
        // Invisible HWND, embedded WebView only, no browser window or live task
        // operations. Verify the public payload after extraction from Codex.
        public static int Run(string output,string cacheOnly=null){
            Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=1;
            app.Startup+=async(s,e)=>{
                var checks=new List<string>();Exception failure=null;WebView2CompositionControl view=null;HwndSource host=null;int fixtureSends=0,fixtureNavigation=0;
                Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
                try{
                    string root=Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string components=await Task.Run(()=>cacheOnly==null?CodexNativeComponents.Prepare(Path.Combine(root,"assets/native-task-ui"),Path.Combine(output,"components-cache")):CodexNativeComponents.PrepareWithArchives(Path.Combine(root,"assets/native-task-ui"),cacheOnly,new string[0]));
                    var parameters=new HwndSourceParameters("GrassFish invisible component verification"){Width=420,Height=400,WindowStyle=unchecked((int)0x80000000)};
                    host=new HwndSource(parameters);var grid=new Grid{Width=420,Height=400};view=new WebView2CompositionControl();grid.Children.Add(view);host.RootVisual=grid;grid.Measure(new Size(420,400));grid.Arrange(new Rect(0,0,420,400));grid.UpdateLayout();
                    var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(output,"webview-profile"),new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-sync --no-first-run"));
                    await view.EnsureCoreWebView2Async(environment);var core=view.CoreWebView2;core.Settings.AreDevToolsEnabled=false;core.Settings.AreHostObjectsAllowed=false;
                    core.SetVirtualHostNameToFolderMapping("grassfish-pet.local",components,CoreWebView2HostResourceAccessKind.DenyCors);
                    core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);core.WebResourceRequested+=(sender,args)=>{if(!args.Request.Uri.StartsWith("https://grassfish-pet.local/")&&!args.Request.Uri.StartsWith("data:"))args.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Local only","");};core.NewWindowRequested+=(sender,args)=>args.Handled=true;core.PermissionRequested+=(sender,args)=>args.State=CoreWebView2PermissionState.Deny;
                    var json=new JavaScriptSerializer();var state=new TaskViewState().Export();
                    core.WebMessageReceived+=(sender,args)=>{var message=json.DeserializeObject(args.TryGetWebMessageAsString());string action=JsonValue.Text(message,"action");if(action=="send")fixtureSends++;if(action=="open")fixtureNavigation++;};
                    var boot=new{nonce="isolated-component-qa",qa=true,state,snapshot=new{connected=true,detail="isolated fixture",expanded=true,delivery=state,items=new[]{new{id="qa-fixture",turnKey="qa-turn",title="兼容性测试",body="正在思考",level="running",isLoading=true,canReply=true}},layout=new{badgeX=10,badgeY=10,trayX=10,trayY=45,trayWidth=360,maxHeight=320,above=false}}};
                    // Invisible documents can suspend animation callbacks. Pump
                    // them with timers in THIS fixture only, so draft restoration
                    // effects run without ever showing/activating a desktop HWND.
                    const string hiddenFrames="window.requestAnimationFrame=callback=>setTimeout(()=>callback(performance.now()),16);window.cancelAnimationFrame=id=>clearTimeout(id);";
                    string bootScript=await core.AddScriptToExecuteOnDocumentCreatedAsync(hiddenFrames+"window.__grassFishBoot="+json.Serialize(boot)+";");core.Navigate("https://grassfish-pet.local/index.html");
                    bool ready=false;for(int i=0;i<120;i++){if(await core.ExecuteScriptAsync("!!window.grassFish&&document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"]').length===1")=="true"){ready=true;break;}await Task.Delay(100);}
                    check(ready,"extracted original official component mounts in invisible embedded WebView");
                    CodexNativeComponents.MarkReady(components);
                    check(File.Exists(Path.Combine(Path.GetDirectoryName(components),"last-good.json")),"successful invisible mount promotes a fully verified cache");
                    await core.ExecuteScriptAsync("document.querySelector('button[aria-label=\"回复 兼容性测试\"]').click()");await Task.Delay(500);
                    check(await core.ExecuteScriptAsync("!!document.querySelector('textarea')")=="true","original inline reply editor expands from original reply button");
                    check(await core.ExecuteScriptAsync("window.grassFish.qa().disabledStops")=="true","unsupported stop is explicitly disabled");
                    check(await core.ExecuteScriptAsync("document.querySelector('textarea').getAttribute('placeholder')==='继续跟进'")=="true","official follow-up field preserves Chinese labels");
                    await core.ExecuteScriptAsync("(()=>{const input=document.querySelector('textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,'草鱼测试草稿');input.dispatchEvent(new Event('input',{bubbles:true}));})()");await Task.Delay(150);
                    check(await core.ExecuteScriptAsync("window.grassFish.exportState().drafts.some(x=>x.id==='qa-fixture'&&x.text==='草鱼测试草稿')")=="true","official editor draft is mirrored into the stable host state");
                    await core.ExecuteScriptAsync("document.querySelector('textarea').dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true,data:'喵'}));document.querySelector('textarea').dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,cancelable:true,key:'Enter',code:'Enter',isComposing:true}));");await Task.Delay(150);
                    check(fixtureSends==0&&await core.ExecuteScriptAsync("!!document.querySelector('textarea')&&window.grassFish.exportState().drafts.some(x=>x.text==='草鱼测试草稿')")=="true","IME composing Enter does not clear or submit the fixture draft");
                    await core.ExecuteScriptAsync("document.querySelector('textarea').dispatchEvent(new CompositionEvent('compositionend',{bubbles:true,data:'喵'}));document.querySelector('button[aria-label=\"收起 qa-fixture\"]')?.click()");
                    // Reload exactly as a controller recreation would, with the
                    // host's mirrored drafts. No task IDs/messages leave this fixture.
                    var restored=new{drafts=new[]{new{id="qa-fixture",text="草鱼测试草稿"}},pending=new string[0],results=new object[0]};
                    core.RemoveScriptToExecuteOnDocumentCreated(bootScript);var restoredBoot=JsonValue.Object(json.DeserializeObject(json.Serialize(boot)));restoredBoot["state"]=restored;
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(hiddenFrames+"window.__grassFishBoot="+json.Serialize(restoredBoot)+";");
                    var navigation=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> completed=(sender,args)=>navigation.TrySetResult(args.IsSuccess);core.NavigationCompleted+=completed;core.Reload();
                    check(await Task.WhenAny(navigation.Task,Task.Delay(10000))==navigation.Task&&await navigation.Task,"renderer recreation navigation completes");core.NavigationCompleted-=completed;
                    bool reloaded=false;for(int i=0;i<100;i++){if(await core.ExecuteScriptAsync("!!window.grassFish&&!!document.querySelector('button[aria-label=\"回复 兼容性测试\"]')")=="true"){reloaded=true;break;}await Task.Delay(100);}
                    check(reloaded,"official task view remounts from the same verified profile");
                    check(await core.ExecuteScriptAsync("window.__grassFishBoot.state.drafts[0]?.text==='草鱼测试草稿'")=="true","new renderer receives the host's preserved draft before mounting");
                    await core.ExecuteScriptAsync("document.querySelector('button[aria-label=\"回复 兼容性测试\"]').click()");
                    bool draftRestored=false;for(int i=0;i<30;i++){await Task.Delay(100);if(await core.ExecuteScriptAsync("document.querySelector('textarea')?.value==='草鱼测试草稿'")=="true"){draftRestored=true;break;}}
                    string fixtureState=await core.ExecuteScriptAsync("JSON.stringify({input:document.querySelector('textarea')?.value,host:window.__grassFishBoot.state.drafts,page:window.grassFish.exportState(),visibility:document.visibilityState})");
                    check(draftRestored,"draft survives renderer recreation under either supported official profile"+(draftRestored?"":"; fixture="+fixtureState));
                    check(!IsWindowVisible(host.Handle),"component verification never shows a native desktop window");exit=0;
                }catch(Exception ex){failure=ex;}
                finally{if(view!=null)view.Dispose();if(host!=null)host.Dispose();File.WriteAllText(Path.Combine(output,"component-ui.json"),new JavaScriptSerializer().Serialize(new{ok=failure==null,checks,error=failure==null?null:failure.ToString(),cacheOnly=cacheOnly!=null,componentStatus=CodexNativeComponents.Status,fixtureSends,fixtureNavigation,liveMessagesSent=0,navigationCalls=0,desktopUiAutomationUsed=false}));app.Shutdown();}
            };
            app.Run();return exit;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr handle);
    }
}
