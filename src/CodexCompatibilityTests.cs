using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    public static class CodexCompatibilityTests {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        static object Catalog(bool complete){return Json.DeserializeObject(Json.Serialize(new{tools=(complete?new[]{"list_threads","read_thread","send_message_to_thread","navigate_to_codex_page"}:new[]{"list_threads","read_thread"}).Select(x=>new{name=x,@namespace="codex_app"}).ToArray()}));}
        public static void Run(Action<bool,string> check){
            string modern=@"\\.\pipe\codex-app-tools-fixture",legacy=@"\\.\pipe\codex-browser-use-fixture";
            check(CodexConnection.IsTaskPipe(modern)&&CodexConnection.IsTaskPipe(legacy),"new task endpoint and legacy task endpoint accepted");
            foreach(string bad in new[]{@"\\remote\pipe\codex-app-tools-test",@"\\.\pipe\codex-computer-use-test",modern+@"\nested",modern+":ads",@"C:\codex-app-tools-test",null})check(!CodexConnection.IsTaskPipe(bad),"unrelated, remote and malformed task endpoints rejected");
            check(CodexConnection.OrderCandidates(legacy,new[]{legacy,modern,modern.ToUpperInvariant(),@"\\.\pipe\unrelated"}).SequenceEqual(new[]{legacy,modern}),"safe environment hint is prioritized and endpoints deduplicated");
            check(CodexConnection.OrderCandidates("unsafe",new[]{legacy,modern}).SequenceEqual(new[]{modern,legacy}),"modern tools precede browser fallback without a valid hint");
            check(CodexConnection.OrderCandidates(null,Enumerable.Range(0,80).Select(i=>modern+i)).Length==32,"endpoint discovery candidate count bounded");
            check(CodexConnection.HasTaskCapabilities(Catalog(true))&&!CodexConnection.HasTaskCapabilities(Catalog(false)),"capability check requires all four task operations");
            check(!CodexConnection.HasTaskCapabilities(new{tools=new[]{new{name="list_threads",@namespace="other"}}}),"tool names from another namespace cannot authorize a connection");
            var touched=new List<string>();
            var result=CodexConnection.SelectEndpoint(new[]{legacy,modern},new[]{"old-local","current-local"},p=>{touched.Add(p);if(p==legacy)throw new IOException("No handler registered for method: tools/list");return Catalog(true);},(p,c)=>c=="current-local");
            check(result.Item1==modern&&result.Item2=="current-local"&&touched.Count==2,"obsolete browser endpoint failure falls through to new endpoint and existing local caller");
            result=CodexConnection.SelectEndpoint(new[]{modern,legacy},new[]{"local"},p=>p==modern?Catalog(false):Catalog(true),(p,c)=>true);
            check(result.Item1==legacy,"legacy task endpoint retained when modern endpoint lacks required capabilities");
            string issue=null;try{CodexConnection.SelectEndpoint(new[]{modern},new string[0],p=>Catalog(true),(p,c)=>true);}catch(CodexBridgeException e){issue=e.Issue;}check(issue=="no-local-context","fresh user without local conversations gets actionable guidance, no fabricated task");
            issue=null;try{CodexConnection.SelectEndpoint(new string[0],new[]{"local"},p=>Catalog(true),(p,c)=>true);}catch(CodexBridgeException e){issue=e.Issue;}check(issue=="no-endpoint","running Codex without a task endpoint is distinguished from an interface break");
            issue=null;try{CodexConnection.SelectEndpoint(new[]{modern},new[]{"local"},p=>Catalog(false),(p,c)=>true);}catch(CodexBridgeException e){issue=e.Issue;}check(issue=="incompatible","real missing task capabilities are not presented as endless connecting");
            issue=null;try{CodexConnection.SelectEndpoint(new[]{modern},new[]{"local"},p=>Catalog(true),(p,c)=>{throw new TimeoutException("fixture startup delay");});}catch(CodexBridgeException e){issue=e.Issue;}check(issue=="reconnecting","temporary read timeout is not misreported as an incompatible version or missing local conversation");
            using(var connection=new CodexConnection("unused",null)){
                connection.Current=new CodexTasksSnapshot{Connected=true,DesktopRunning=true,Tasks=new List<CodexTask>{new CodexTask{Id="local",CanReply=true}}};
                var failed=connection.RefreshCycle(true,(tool,args)=>{throw new CodexBridgeException("incompatible","fixture capabilities changed");});
                check(!failed.Connected&&failed.Stale&&failed.Issue=="incompatible"&&!failed.Tasks[0].CanReply&&failed.RetryLabel.Contains("更新"),"incompatible snapshot retains display/drafts but cannot send and shows update guidance");
                connection.Current=failed;
                var restored=connection.RefreshCycle(true,(tool,args)=>Json.DeserializeObject("{\"threads\":[]}"));
                check(restored.Connected&&restored.Issue==null&&!restored.Stale,"a subsequent good connection automatically clears incompatibility state");
                var changedFormat=connection.RefreshCycle(true,(tool,args)=>Json.DeserializeObject("{\"unknownFutureFormat\":[]}"));
                check(changedFormat.Issue=="incompatible"&&changedFormat.Detail.Contains("更新"),"unknown task payload format shows compatibility guidance rather than an endless generic reconnect");
                check(!connection.RefreshCycle(false,(tool,args)=>{throw new Exception("must not contact absent Codex");}).Connected,"no endpoint discovery or server start when desktop is absent");
            }
            ComponentFixtures(check);
            UntrustedServerFixture(check);
        }
        static void UntrustedServerFixture(Action<bool,string> check){
            string name="codex-app-tools-untrusted-fixture-"+Guid.NewGuid().ToString("N");
            using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
                var received=Task.Run(()=>{pipe.WaitForConnection();return pipe.ReadByte();});bool refused=false;
                using(var connection=new CodexConnection("fixture-caller",@"\\.\pipe\"+name,false))try{connection.Probe();}catch(IOException){refused=true;}
                check(refused&&received.Wait(3000)&&received.Result==-1,"modern endpoint verifies actual server identity before sending any conversation data");
            }
        }
        static string FixtureShell(string root,string content){
            string shell=Path.Combine(root,"shell");Directory.CreateDirectory(shell);
            foreach(string name in new[]{"index.html","overlay.js","overlay.css"})File.WriteAllText(Path.Combine(shell,name),"trusted host "+name);
            var files=new Dictionary<string,byte[]>{{"component.js",Encoding.UTF8.GetBytes(content)},{"component.css",Encoding.UTF8.GetBytes("fixture css")}};
            string[] names={"OfficialActivityPill","OfficialActivityStack","OfficialSwipeInput","OfficialReact","OfficialReactDom","OfficialIntlProvider","InitIntl","OfficialScopeProvider","InitScope","SetPersistedAtoms","InitPersistedAtoms","OfficialChevron","OfficialAppScope","OfficialBadge","InitBadge"};
            var manifest=new{schemaVersion=2,sourcePackage="fixture-reviewed-build",files=files.Select(x=>new{file=x.Key,sha256=CodexNativeComponents.Hash(x.Value),upstreamSha256=CodexNativeComponents.Hash(x.Value)}).ToArray(),shims=new Dictionary<string,string>(),bridge=names.ToDictionary(x=>x,x=>(object)new{file="component.js",symbol=x}),styles=new[]{"component.css"}};
            File.WriteAllText(Path.Combine(shell,"provenance.json"),Json.Serialize(manifest),new UTF8Encoding(false));
            var assets=new Dictionary<string,object>();long offset=0;foreach(var pair in files){assets.Add(pair.Key,new{offset=offset.ToString(),size=pair.Value.Length});offset+=pair.Value.Length;}
            byte[] tree=Encoding.UTF8.GetBytes(Json.Serialize(new{files=new{webview=new{files=new{assets=new{files=assets}}}}}));
            using(var writer=new BinaryWriter(File.Create(Path.Combine(root,"app.asar")))){writer.Write((uint)4);writer.Write((uint)(tree.Length+8));writer.Write((uint)(tree.Length+4));writer.Write((uint)tree.Length);writer.Write(tree);foreach(var bytes in files.Values)writer.Write(bytes);}
            return shell;
        }
        static void ComponentFixtures(Action<bool,string> check){
            string root=Path.Combine(Path.GetTempPath(),"grassfish-compatibility-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                string shell=FixtureShell(root,"fixture reviewed bytes"),archive=Path.Combine(root,"app.asar"),cache=Path.Combine(root,"cache");
                var profile=CodexComponentProfile.Load(shell)[0];
                string target=CodexNativeComponents.Extract(archive,shell,cache);check(CodexNativeComponents.Verified(target,profile),"complete synthetic archive is verified before use");
                check(File.ReadAllText(Path.Combine(target,"compat.js")).Contains("OfficialActivityStack")&&File.ReadAllText(Path.Combine(target,"official.css")).Contains("component.css"),"stable host bindings and stylesheet are generated only from packaged profile");
                string other=Path.Combine(root,"updated-app.asar");File.Copy(archive,other);File.SetLastWriteTimeUtc(other,DateTime.UtcNow.AddDays(4));
                check(CodexNativeComponents.Extract(other,shell,cache)==target,"identical official bytes reuse cache across changed archive path and timestamp");
                CodexNativeComponents.MarkReady(target);
                File.WriteAllText(Path.Combine(target,"overlay.js"),"untrusted cached host");
                check(CodexNativeComponents.PrepareWithArchives(shell,cache,new[]{Path.Combine(root,"not-installed-anymore.asar")})==target,"validated local cache survives unavailable/new Codex archive without re-extraction");
                check(File.ReadAllText(Path.Combine(target,"overlay.js"))=="trusted host overlay.js","old cache shell is never executed; current packaged host replaces it");
                File.WriteAllText(Path.Combine(target,"vendor/component.js"),"corrupted");
                check(!CodexNativeComponents.Verified(target,profile),"modified official cache fails full integrity checks");
                string repaired=CodexNativeComponents.PrepareWithArchives(shell,cache,new[]{archive});
                check(repaired!=target&&CodexNativeComponents.Verified(repaired,profile),"corrupted cache is repaired from known official archive without overwriting the previous directory");
                check(JsonValue.Text(Json.DeserializeObject(File.ReadAllText(Path.Combine(cache,"last-good.json"))),"directory")==Path.GetFileName(target),"extracting a replacement cannot promote it before an actual renderer mount");
                CodexNativeComponents.MarkReady(repaired);
                File.WriteAllText(Path.Combine(repaired,"vendor/extra.js"),"not in trusted profile");
                check(!CodexNativeComponents.Verified(repaired,profile),"unlisted vendor files are not silently trusted");
                string before=File.ReadAllText(Path.Combine(cache,"last-good.json"));File.WriteAllText(other,"unknown future build");
                bool rejected=false;try{CodexNativeComponents.PrepareWithArchives(shell,cache,new[]{other});}catch(IOException e){rejected=e.Message.Contains("没有完整可用")&&e.Message.Contains("日程提醒");}
                check(rejected&&File.ReadAllText(Path.Combine(cache,"last-good.json"))==before,"unknown build plus unusable caches fails clearly and does not replace last-good marker");
                string oldRoot=Path.Combine(root,"legacy-cache"),oldTarget=Path.Combine(oldRoot,"old-archive-identity");Directory.CreateDirectory(Path.Combine(oldTarget,"vendor"));
                foreach(var item in profile.Files){string name=JsonValue.Text(item,"file");File.Copy(Path.Combine(repaired,"vendor",name),Path.Combine(oldTarget,"vendor",name));}
                File.WriteAllText(Path.Combine(oldRoot,"last-good.json"),"{\"directory\":\"../outside\"}");
                check(CodexNativeComponents.PrepareWithArchives(shell,oldRoot,new string[0])==oldTarget,"legacy archive-identity cache migrates by hashes; traversal in marker is ignored");
                var doc=Json.DeserializeObject(File.ReadAllText(Path.Combine(shell,"provenance.json")));JsonValue.Object(JsonValue.Value(JsonValue.Value(doc,"bridge"),"OfficialActivityPill"))["symbol"]="invalid()";
                rejected=false;try{CodexComponentProfile.Parse(Encoding.UTF8.GetBytes(Json.Serialize(doc)));}catch(InvalidDataException){rejected=true;}check(rejected,"profile binding symbols cannot inject executable code into generated bridge");
            }finally{Directory.Delete(root,true);}
        }
    }
}
