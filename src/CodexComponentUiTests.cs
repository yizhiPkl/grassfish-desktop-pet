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
        public static int Run(string output){
            Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=1;
            app.Startup+=async(s,e)=>{
                var checks=new List<string>();Exception failure=null;WebView2CompositionControl view=null;HwndSource host=null;
                Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
                try{
                    string root=Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string components=await Task.Run(()=>CodexNativeComponents.Prepare(Path.Combine(root,"assets/native-task-ui"),Path.Combine(output,"components-cache")));
                    var parameters=new HwndSourceParameters("GrassFish invisible component verification"){Width=420,Height=400,WindowStyle=unchecked((int)0x80000000)};
                    host=new HwndSource(parameters);var grid=new Grid{Width=420,Height=400};view=new WebView2CompositionControl();grid.Children.Add(view);host.RootVisual=grid;grid.Measure(new Size(420,400));grid.Arrange(new Rect(0,0,420,400));grid.UpdateLayout();
                    var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(output,"webview-profile"),new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-sync --no-first-run"));
                    await view.EnsureCoreWebView2Async(environment);var core=view.CoreWebView2;core.Settings.AreDevToolsEnabled=false;core.Settings.AreHostObjectsAllowed=false;
                    core.SetVirtualHostNameToFolderMapping("grassfish-pet.local",components,CoreWebView2HostResourceAccessKind.DenyCors);
                    core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);core.WebResourceRequested+=(sender,args)=>{if(!args.Request.Uri.StartsWith("https://grassfish-pet.local/")&&!args.Request.Uri.StartsWith("data:"))args.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Local only","");};core.NewWindowRequested+=(sender,args)=>args.Handled=true;core.PermissionRequested+=(sender,args)=>args.State=CoreWebView2PermissionState.Deny;
                    var json=new JavaScriptSerializer();var state=new TaskViewState().Export();
                    var boot=new{nonce="isolated-component-qa",qa=true,state,snapshot=new{connected=true,detail="isolated fixture",expanded=true,delivery=state,items=new[]{new{id="qa-fixture",turnKey="qa-turn",title="兼容性测试",body="正在思考",level="running",isLoading=true,canReply=true}},layout=new{badgeX=10,badgeY=10,trayX=10,trayY=45,trayWidth=360,maxHeight=320,above=false}}};
                    await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__grassFishBoot="+json.Serialize(boot)+";");core.Navigate("https://grassfish-pet.local/index.html");
                    bool ready=false;for(int i=0;i<120;i++){if(await core.ExecuteScriptAsync("!!window.grassFish&&document.querySelectorAll('[data-avatar-overlay-hit-region=\"activity-pill\"]').length===1")=="true"){ready=true;break;}await Task.Delay(100);}
                    check(ready,"extracted original official component mounts in invisible embedded WebView");
                    await core.ExecuteScriptAsync("document.querySelector('button[aria-label=\"回复 兼容性测试\"]').click()");await Task.Delay(500);
                    check(await core.ExecuteScriptAsync("!!document.querySelector('textarea')")=="true","original inline reply editor expands from original reply button");
                    check(await core.ExecuteScriptAsync("window.grassFish.qa().disabledStops")=="true","unsupported stop is explicitly disabled");
                    check(await core.ExecuteScriptAsync("document.querySelector('textarea').getAttribute('placeholder')==='继续跟进'")=="true","official follow-up field preserves Chinese labels");
                    check(!IsWindowVisible(host.Handle),"component verification never shows a native desktop window");exit=0;
                }catch(Exception ex){failure=ex;}
                finally{if(view!=null)view.Dispose();if(host!=null)host.Dispose();File.WriteAllText(Path.Combine(output,"component-ui.json"),new JavaScriptSerializer().Serialize(new{ok=failure==null,checks,error=failure==null?null:failure.ToString(),liveMessagesSent=0,navigationCalls=0,desktopUiAutomationUsed=false}));app.Shutdown();}
            };
            app.Run();return exit;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr handle);
    }
}
