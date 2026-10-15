using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopKitten {
    public sealed class CodexTask {
        public string Id,HostId,Title,Status,Kind,Cwd,TurnKey,ActivityBody;
        public bool Unread,CanReply;
        public long Updated;
        public string Label {get{return Title;}}
        public bool Running {get{return Status=="active"||Status=="running";}}
        public bool Attention {get{return Status=="needsAttention"||Status=="waiting";}}
        public bool Relevant {get{return Running||Attention||Unread;}}
    }
    public sealed class CodexTasksSnapshot {
        public bool Connected,DesktopRunning,Stale;public int DetailFailures;public string Detail;public List<CodexTask> Tasks=new List<CodexTask>();
        public int Count {get{return Tasks.Count(t=>t.Relevant);}}
        public int Running {get{return Tasks.Count(t=>t.Running);}}
        public int Waiting {get{return Tasks.Count(t=>t.Attention);}}
        public int Unread {get{return Tasks.Count(t=>t.Unread);}}
    }
    public static class JsonValue {
        public static Dictionary<string,object> Object(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
        public static object Value(object value,string key){object result;return Object(value).TryGetValue(key,out result)?result:null;}
        public static string Text(object value,string key){var v=Value(value,key);return v==null?null:Convert.ToString(v);}
        public static bool Bool(object value,string key){var v=Value(value,key);return v is bool&&(bool)v;}
        public static IEnumerable<object> Array(object value){return value as object[]??(value as System.Collections.ArrayList??new System.Collections.ArrayList()).Cast<object>().ToArray();}
        public static long Number(object value,string key){long n;return long.TryParse(Text(value,key),out n)?n:0;}
    }
    // Version-checked compatibility adapter to the RUNNING desktop app tools.
    // Never launches app-server, mutates rollout/SQLite, or changes model/auth.
    // The only mutating calls are explicit UI Send and Open-in-Codex actions.
    public sealed class CodexConnection : IDisposable {
        readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
        readonly string home;string endpoint,contextId;int requestId;volatile bool stop,enabled=true;
        readonly bool fixtureConnection;
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
        readonly AutoResetEvent wake=new AutoResetEvent(false);Thread worker;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        readonly Dictionary<string,CodexTask> activityCache=new Dictionary<string,CodexTask>();int activityCursor;
        internal int ListFailures,ReadFailures;int userRequests;
        public event Action<CodexTasksSnapshot> Changed;
        public CodexTasksSnapshot Current=new CodexTasksSnapshot{Detail="正在连接 Codex…"};
        public CodexConnection(string home,string contextId){this.home=home;/* Legacy bundled pairing is deliberately ignored. */}
        public string ContextId {get{return contextId;}}
        internal CodexConnection(string contextId,string fixturePipe,bool fixture){this.contextId=contextId;endpoint=fixturePipe;fixtureConnection=fixture;}
        public void Start(){worker=new Thread(Loop){IsBackground=true,Name="Grass fish Codex bridge"};worker.Start();}
        public void Enable(bool value){enabled=value;wake.Set();}
        public void Refresh(){wake.Set();}
        static string[] CandidatePipes(){
            var result=new List<string>();string env=Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH");
            if(!string.IsNullOrEmpty(env)&&env.StartsWith(@"\\.\pipe\codex-browser-use-",StringComparison.OrdinalIgnoreCase))result.Add(env);
            try{result.AddRange(Directory.GetFiles(@"\\.\pipe\","codex-browser-use-*").Where(p=>!result.Contains(p)));}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(NotSupportedException){}
            return result.Take(32).ToArray();
        }
        static void ReadExact(Stream stream,byte[] data){int offset=0;while(offset<data.Length){int n=stream.Read(data,offset,data.Length-offset);if(n==0)throw new EndOfStreamException("Codex 本地连接已关闭");offset+=n;}}
        object Request(string path,string method,object args,bool send=false){
            if(!path.StartsWith(@"\\.\pipe\codex-browser-use-",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Codex 管道路径无效");
            using(var pipe=new NamedPipeClientStream(".",path.Substring(9),PipeDirection.InOut,PipeOptions.Asynchronous)){
                pipe.Connect(1200);
                if(!fixtureConnection){uint pid;if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out pid))throw new IOException("无法验证 Codex 本地连接进程");using(var server=System.Diagnostics.Process.GetProcessById((int)pid))if(!CodexWindowActivation.IsCodexDesktop(server.MainModule.FileName))throw new IOException("本地管道并非受支持的 Codex 桌面进程");}
                int id=Interlocked.Increment(ref requestId);var payload=Encoding.UTF8.GetBytes(json.Serialize(new{id,jsonrpc="2.0",method,@params=args}));
                if(payload.Length>1024*1024)throw new InvalidDataException("请求过大");
                var io=Task.Run(()=>{var header=BitConverter.GetBytes(payload.Length);pipe.Write(header,0,4);pipe.Write(payload,0,payload.Length);pipe.Flush();ReadExact(pipe,header);int size=BitConverter.ToInt32(header,0);if(size<2||size>8*1024*1024)throw new InvalidDataException("Codex 返回过大");var content=new byte[size];ReadExact(pipe,content);return json.DeserializeObject(Encoding.UTF8.GetString(content));});
                if(!io.Wait(12000)){pipe.Dispose();io.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException(send?"Codex 返回超时；发送结果可能未知，请先查看原对话，不会自动重发。":"Codex 本地连接超时");}
                var response=JsonValue.Object(io.Result);if(JsonValue.Number(response,"id")!=id)throw new InvalidDataException("Codex 响应编号不符");
                if(response.ContainsKey("error"))throw new IOException(JsonValue.Text(response["error"],"message")??"Codex 请求失败");
                return JsonValue.Value(response,"result");
            }
        }
        void Discover(){
            if(!CodexWindowActivation.IsRunning())throw new IOException("Codex 未运行；日程提醒仍可独立使用。");
            var contexts=CodexLocalProfile.Contexts(home);
            if(contexts.Length==0)throw new IOException("请先在本机 Codex 中创建或打开一个本地对话，再重试连接。不会替你创建任务。");
            Exception last=null;
            foreach(var p in CandidatePipes())try{
                var catalog=Request(p,"tools/list",new{threadStartKind="all"});
                var tools=JsonValue.Array(JsonValue.Value(catalog,"tools")).ToArray();
                if(!new[]{"list_threads","read_thread","send_message_to_thread","navigate_to_codex_page"}.All(n=>tools.Any(t=>JsonValue.Text(t,"name")==n&&JsonValue.Text(t,"namespace")=="codex_app")))throw new InvalidDataException("当前 Codex 工具版本不兼容");
                // Validate an existing local caller on the discovered desktop;
                // no cached developer ID is ever a prerequisite for another PC.
                foreach(string candidate in contexts.Take(4))try{
                    var doc=Invoke(p,candidate,"read_thread",new{threadId=candidate,hostId="local",turnLimit=1,includeOutputs=false,maxOutputCharsPerItem=1},true);
                    var thread=JsonValue.Value(doc,"thread");if(JsonValue.Text(thread,"id")!=candidate||JsonValue.Text(thread,"kind")!="codex")continue;
                    endpoint=p;contextId=candidate;return;
                }catch(Exception e){last=e;}
                throw new InvalidDataException("无法验证本机 Codex 会话。请在 Codex 中打开本地对话后重试。");
            }catch(Exception e){last=e;}
            endpoint=null;throw new IOException("尚未连接 Codex。打开 Codex 后再试；不会另起后端。",last);
        }
        object Call(string tool,object arguments,bool parseJson=true){
            if(endpoint==null)Discover();
            if(string.IsNullOrWhiteSpace(contextId))throw new InvalidDataException("尚未发现本机 Codex 会话");
            return Invoke(endpoint,contextId,tool,arguments,parseJson);
        }
        object Invoke(string pipe,string caller,string tool,object arguments,bool parseJson){
            var result=Request(pipe,"tools/call",new{arguments,callerSource="codex",hostId="local",@namespace="codex_app",threadId=caller,tool,turnId="grass-fish-desktop",callId="grass-fish-"+Guid.NewGuid().ToString("N")},tool=="send_message_to_thread");
            string text=string.Join("\n",JsonValue.Array(JsonValue.Value(result,"contentItems")).Where(x=>JsonValue.Text(x,"type")=="inputText").Select(x=>JsonValue.Text(x,"text")));
            if(!JsonValue.Bool(result,"success"))throw new IOException(string.IsNullOrEmpty(text)?"Codex 操作失败":text);
            if(!parseJson)return text;if(string.IsNullOrEmpty(text))return new Dictionary<string,object>();return json.DeserializeObject(text);
        }
        public static CodexTasksSnapshot ParseTasks(object document){
            var fields=JsonValue.Object(document);if(!fields.ContainsKey("threads")&&!fields.ContainsKey("pinnedThreads"))throw new InvalidDataException("Codex 任务列表返回格式不可用");
            var result=new CodexTasksSnapshot{Connected=true,DesktopRunning=true,Detail="已连接 Codex · 本机任务"};
            var all=JsonValue.Array(JsonValue.Value(document,"pinnedThreads")).Concat(JsonValue.Array(JsonValue.Value(document,"threads")));
            var seen=new HashSet<string>();
            foreach(var v in all){string id=JsonValue.Text(v,"id"),kind=JsonValue.Text(v,"kind"),host=JsonValue.Text(v,"hostId");if(string.IsNullOrEmpty(id)||!seen.Add(id)||kind!="codex"||host!="local")continue;
                object raw=JsonValue.Value(v,"status");string status=raw is string?(string)raw:JsonValue.Text(raw,"type");
                result.Tasks.Add(new CodexTask{Id=id,HostId=host,Kind=kind,Title=JsonValue.Text(v,"title")??"未命名对话",Status=status??"unknown",Cwd=JsonValue.Text(v,"cwd"),Unread=JsonValue.Bool(v,"isUnread"),Updated=JsonValue.Number(v,"updatedAt"),CanReply=true});
            }
            result.Tasks=result.Tasks.OrderByDescending(t=>t.Relevant).ThenByDescending(t=>t.Running).ThenByDescending(t=>t.Updated>100000000000?t.Updated:t.Updated*1000).ToList();
            return result;
        }
        object BackgroundCall(string tool,object args){gate.Wait();try{return Call(tool,args);}finally{gate.Release();}}
        internal CodexTasksSnapshot RefreshCycle(bool running,Func<string,object,object> request){
            if(!enabled)return new CodexTasksSnapshot{DesktopRunning=running,Detail="Codex 联动已关闭"};
            if(!running){endpoint=null;activityCache.Clear();return new CodexTasksSnapshot{Detail="Codex 未运行 · 提醒仍可使用"};}
            try{
                var value=ParseTasks(request("list_threads",new{limit=50}));EnrichActivities(value,request);return value;
            }catch(Exception e){
                endpoint=null;ListFailures++;
                // Preserve display-only data, but never authorize an action from a stale snapshot.
                return new CodexTasksSnapshot{DesktopRunning=true,Stale=true,Detail="Codex 任务刷新暂不可用，正在重连 · "+ShortError(e),Tasks=Current.Tasks.Select(t=>new CodexTask{Id=t.Id,HostId=t.HostId,Title=t.Title,Status=t.Status,Kind=t.Kind,Cwd=t.Cwd,TurnKey=t.TurnKey,ActivityBody=t.ActivityBody,Unread=t.Unread,Updated=t.Updated,CanReply=false}).ToList()};
            }
        }
        void Loop(){int failures=0;while(!stop){Current=RefreshCycle(CodexWindowActivation.IsRunning(),BackgroundCall);failures=Current.DesktopRunning&&!Current.Connected?Math.Min(4,failures+1):0;var cb=Changed;if(cb!=null&&!stop)cb(Current);wake.WaitOne(!Current.DesktopRunning?8000:failures==0?3000:Math.Min(30000,3000*(1<<failures)));}}
        internal CodexTasksSnapshot PollForDiagnostics(){Current=RefreshCycle(CodexWindowActivation.IsRunning(),BackgroundCall);return Current;}
        void EnrichActivities(CodexTasksSnapshot snapshot){EnrichActivities(snapshot,(tool,args)=>Call(tool,args));}
        void EnrichActivities(CodexTasksSnapshot snapshot,Func<string,object,object> request){
            foreach(var task in snapshot.Tasks){CodexTask old;if(activityCache.TryGetValue(task.Id,out old)){task.TurnKey=old.TurnKey;task.ActivityBody=old.ActivityBody;}}
            var candidates=snapshot.Tasks.Where(t=>t.Relevant||activityCache.ContainsKey(t.Id)).ToArray();
            int length=Math.Min(4,candidates.Length);
            for(int n=0;n<length&&!stop&&userRequests==0;n++){
                var task=candidates[(activityCursor+n)%candidates.Length];
                // One latest turn only. Never download tool output or an entire transcript for a status label.
                try{var doc=request("read_thread",new{threadId=task.Id,hostId=task.HostId,turnLimit=1,includeOutputs=false,maxOutputCharsPerItem=200});task.ActivityBody=NativeActivityLedger.ActivityBody(doc,task);activityCache[task.Id]=task;}
                catch(Exception){ReadFailures++;snapshot.DetailFailures++;if(task.ActivityBody==null)task.ActivityBody=task.Running?"正在进行 · 详情待刷新":task.Attention?"需要你的输入":"有新活动";}
            }
            if(candidates.Length>0)activityCursor=(activityCursor+length)%candidates.Length;
            if(snapshot.DetailFailures>0)snapshot.Detail="已连接 Codex · 部分任务详情待刷新";
            var ids=new HashSet<string>(snapshot.Tasks.Select(t=>t.Id));foreach(var id in activityCache.Keys.Where(id=>!ids.Contains(id)).ToArray())activityCache.Remove(id);
        }
        public static string ShortError(Exception e){while(e is AggregateException&&e.InnerException!=null)e=e.InnerException;string s=e.Message??"连接错误";return s.Length>180?s.Substring(0,180):s;}
        public async Task<string> Read(CodexTask task){await gate.WaitAsync();try{
            var value=Call("read_thread",new{threadId=task.Id,hostId=task.HostId,turnLimit=5,includeOutputs=false,maxOutputCharsPerItem=6000});return Transcript(value);
        }finally{gate.Release();}}
        public object Probe(){gate.Wait();try{
            Current=ParseTasks(Call("list_threads",new{limit=50}));
            var doc=Call("read_thread",new{threadId=contextId,hostId="local",turnLimit=1,includeOutputs=false,maxOutputCharsPerItem=1000});
            bool same=JsonValue.Text(JsonValue.Value(doc,"thread"),"id")==contextId;
            if(!same)throw new InvalidDataException("读取的会话与连接会话不符");
            return new{ok=true,connected=Current.Connected,localTasks=Current.Tasks.Count,activeOrUnread=Current.Count,statuses=Current.Tasks.GroupBy(t=>t.Status).ToDictionary(g=>g.Key,g=>g.Count()),sameCurrentThreadRead=same,automaticLocalProfileDiscovery=true,bundledPairingUsed=false,liveMessagesSent=0,newThreadsCreated=0,backendProcessesStarted=0};
        }finally{gate.Release();}}
        public object ProbeActivities(){gate.Wait();try{var value=ParseTasks(Call("list_threads",new{limit=50}));EnrichActivities(value);Current=value;return new{ok=true,connected=value.Connected,active=value.Count,enriched=value.Tasks.Count(t=>t.TurnKey!=null),labels=value.Tasks.Where(t=>t.ActivityBody!=null).Select(t=>t.ActivityBody).Distinct().ToArray(),liveMessagesSent=0,backendProcessesStarted=0};}finally{gate.Release();}}
        public static string Transcript(object document){
            var turns=JsonValue.Array(JsonValue.Value(document,"turns")).Reverse();var output=new StringBuilder();
            foreach(var turn in turns)foreach(var item in JsonValue.Array(JsonValue.Value(turn,"items"))){string kind=JsonValue.Text(item,"type");
                if(kind=="userMessage"){var parts=JsonValue.Array(JsonValue.Value(item,"content")).Where(x=>JsonValue.Text(x,"type")=="text").Select(x=>JsonValue.Text(x,"text"));output.AppendLine("你").AppendLine(string.Join("\n",parts)).AppendLine();}
                else if(kind=="agentMessage")output.AppendLine(JsonValue.Text(item,"phase")=="commentary"?"Codex · 进行中":"Codex").AppendLine(JsonValue.Text(item,"text")).AppendLine();
            }
            string result=output.ToString();return result.Length>50000?"（仅显示最近内容）\n"+result.Substring(result.Length-50000):string.IsNullOrWhiteSpace(result)?"这个对话暂时没有可显示的文本。工具输出和附件请在 Codex 查看。":result;
        }
        public async Task Send(CodexTask task,string text){
            if(!enabled||!Current.Connected||task==null||!task.CanReply||string.IsNullOrEmpty(task.Id)||task.HostId!="local"||task.Kind!="codex")throw new InvalidOperationException("尚未连接可续聊的本地 Codex 对话");
            if(string.IsNullOrWhiteSpace(text)||text.Length>20000)throw new ArgumentException("消息请填写1～20000个字符");
            Interlocked.Increment(ref userRequests);try{await gate.WaitAsync();try{if(!enabled||!Current.Connected)throw new InvalidOperationException("连接已关闭，消息未发送");Call("send_message_to_thread",new{threadId=task.Id,hostId=task.HostId,prompt=text},false);}finally{gate.Release();}}finally{Interlocked.Decrement(ref userRequests);}Refresh();
        }
        public async Task Open(CodexTask task){
            if(!enabled||!Current.Connected||task==null||string.IsNullOrEmpty(task.Id)||task.HostId!="local"||task.Kind!="codex")throw new InvalidOperationException("尚未连接可打开的本地 Codex 对话");
            Interlocked.Increment(ref userRequests);try{
            if(!await gate.WaitAsync(5000))throw new TimeoutException("Codex 正在刷新任务，请稍后再点击。");
            try{
                if(!enabled||!Current.Connected)throw new InvalidOperationException("Codex 连接已关闭");
                var value=await Task.Run(()=>Call("navigate_to_codex_page",new{threadId=task.Id}));
                if(!JsonValue.Bool(value,"navigated"))throw new IOException("Codex 没有确认对话跳转，请稍后再试。");
            }finally{gate.Release();}}finally{Interlocked.Decrement(ref userRequests);}
        }
        public void Dispose(){stop=true;wake.Set();if(worker!=null)worker.Join(1500);}
    }
}
