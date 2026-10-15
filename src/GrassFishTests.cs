using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
namespace DesktopKitten {
    public static class GrassFishTests {
        static Settings Empty(){return new Settings{WaterIntervalEnabled=false,WaterTimesEnabled=false,MealEnabled=false};}
        public static void Run(Action<bool,string> check){
            var local=new DateTime(2026,10,14,8,59,0);var utc=DateTime.SpecifyKind(local.AddHours(-8),DateTimeKind.Utc);
            var cfg=Empty();var daily=new ScheduleRule{Title="运动",Time="09:00"};cfg.CustomSchedules.Add(daily);var s=new Scheduler(cfg,local,utc);
            check(!s.Tick(local,utc),"custom before daily due");
            check(s.Tick(local.AddMinutes(1),utc.AddMinutes(1))&&cfg.Pending.Count==1,"daily exact due");
            check(cfg.Pending[0].ScheduleId==daily.Id&&cfg.Pending[0].Message.Contains("草鱼"),"custom rule identity and fallback text");
            s.Dismiss();s.Tick(local.AddMinutes(2),utc.AddMinutes(2));check(cfg.Pending.Count==0,"dismissed daily no duplicate");
            s.Tick(local.AddHours(-1),utc.AddHours(-1));s.Tick(local.AddHours(2),utc.AddHours(2));check(cfg.Pending.Count==0,"daily clock rollback dedupe");
            s.Tick(local.AddDays(1).AddHours(3),utc.AddDays(1).AddHours(3));check(cfg.Pending.Count==1,"daily next day catches today once");
            s.Tick(local.AddDays(4).AddHours(3),utc.AddDays(4).AddHours(3));check(cfg.Pending.Count==1,"one pending paper note per rule across absence");
            s.Dismiss();s.Tick(local.AddDays(5).AddHours(3),utc.AddDays(5).AddHours(3));check(cfg.Pending.Count==1,"custom future reminders continue after dismiss");
            cfg=Empty();cfg.CustomSchedules.Add(new ScheduleRule{Title="周三开会",Mode="weekly",Days=new[]{3},Time="10:00"});s=new Scheduler(cfg,local,utc);
            s.Tick(local.AddHours(2),utc.AddHours(2));check(cfg.Pending.Count==1,"weekly selected weekday");s.Dismiss();
            s.Tick(local.AddDays(1).AddHours(2),utc.AddDays(1).AddHours(2));check(cfg.Pending.Count==0,"weekly skips other days");
            s.Tick(local.AddDays(7).AddHours(2),utc.AddDays(7).AddHours(2));check(cfg.Pending.Count==1,"weekly next occurrence");
            cfg=Empty();var once=new ScheduleRule{Title="一次",Mode="once",DueLocal=local.AddMinutes(1)};cfg.CustomSchedules.Add(once);s=new Scheduler(cfg,local,utc);
            check(!s.Tick(local,utc),"once future waits");s.Tick(local.AddHours(5),utc.AddHours(5));check(cfg.Pending.Count==1&&!once.Enabled&&once.Completed,"once sleep catch completes and disables");s.Dismiss();s.Tick(local.AddDays(1),utc.AddDays(1));check(cfg.Pending.Count==0,"once never repeats");
            cfg=Empty();var every=new ScheduleRule{Title="休息",Mode="interval",Minutes=20};cfg.CustomSchedules.Add(every);s=new Scheduler(cfg,local,utc);
            check(s.Tick(local,utc)&&cfg.Pending.Count==0&&every.NextUtc==utc.AddMinutes(20),"custom interval initialized and persisted");
            s.Tick(local.AddMinutes(19),utc.AddMinutes(19));check(cfg.Pending.Count==0,"custom interval before due");s.Tick(local.AddHours(8),utc.AddHours(8));check(cfg.Pending.Count==1&&every.NextUtc==utc.AddHours(8).AddMinutes(20),"custom interval sleep one fire no flood");
            s.Tick(local.AddHours(9),utc.AddHours(9));check(cfg.Pending.Count==1,"unclosed interval note coalesced");s.Dismiss();s.Tick(local.AddHours(10),utc.AddHours(10));check(cfg.Pending.Count==1,"interval after dismiss fires again");
            every.Enabled=false;s.Dismiss();s.Tick(local.AddDays(1),utc.AddDays(1));check(cfg.Pending.Count==0,"paused custom no fire");s.PreviewSchedule(every);check(cfg.Pending.Count==1&&!every.Enabled,"preview does not enable paused rule");
            foreach(var bad in new[]{new ScheduleRule{Title=""},new ScheduleRule{Title="a",Mode="interval",Minutes=0},new ScheduleRule{Title="a",Mode="weekly",Days=new[]{9}},new ScheduleRule{Title="a",Time="09:00,10:00"},new ScheduleRule{Title="a",Mode="once"}}){bool throws=false;try{bad.Validate();}catch(FormatException){throws=true;}check(throws,"invalid custom rule rejected");}
            string dir=Path.Combine(Path.GetTempPath(),"grass-fish-tests-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{
                var store=new Store(dir);File.WriteAllText(store.FilePath,"{\"SchemaVersion\":1,\"WaterMinutes\":47}");var old=store.Load();check(old.CustomSchedules.Count==0&&old.WaterMinutes==47,"v014 migration preserves values");
                cfg=Empty();cfg.PetSize="medium";daily=new ScheduleRule{Title="晨间伸展",Time="09:00",LastFiredLocal=local.Date.AddHours(9)};once=new ScheduleRule{Title="会面",Mode="once",DueLocal=local.AddDays(1)};cfg.CustomSchedules.Add(daily);cfg.CustomSchedules.Add(once);store.Save(cfg);var restored=store.Load();
                check(restored.CustomSchedules[0].LastFiredLocal==daily.LastFiredLocal&&restored.CustomSchedules[1].DueLocal==once.DueLocal,"local wall-clock fields survive JSON UTC conversion");
                var restart=new Scheduler(restored,local.AddHours(2),utc.AddHours(2));restart.Tick(local.AddHours(2),utc.AddHours(2));check(restored.Pending.Count==0,"daily restart duplicate suppressed");
                restart.Tick(local.AddDays(1),utc.AddDays(1));check(restored.Pending.Count==1&&restored.Pending[0].Title=="会面","roundtrip once wall clock due");store.Save(restored);restored=store.Load();check(restored.Pending.Count==1&&restored.CustomSchedules[1].Completed,"pending once and completion persist");
                restored.CustomSchedules.Add(new ScheduleRule{Title="坏星期",Mode="weekly",Days=new[]{99}});store.Save(restored);restored=store.Load();check(!restored.CustomSchedules.Last().Enabled&&restored.PetSize=="medium"&&store.Warning!=null,"bad individual schedule preserves valid settings");check(restored.CustomSchedules.Last().Description!=null,"invalid weekdays can be displayed without crash");
                restored.CustomSchedules.Add(restored.CustomSchedules[0].Copy());store.Save(restored);restored=store.Load();check(restored.CustomSchedules.Select(r=>r.Id).Distinct().Count()==restored.CustomSchedules.Count,"duplicate rule IDs normalized");
            }finally{foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir,false);}
            foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-1920,-120,1920,1040),new Rect(0,0,1280,680),new Rect(-1536,0,1536,824)})
            foreach(var catSize in new[]{new Size(136,124),new Size(204,186),new Size(272,248)})
            for(int x=0;x<=10;x++)for(int y=0;y<=10;y++){
                var cat=new Rect(area.Left+(area.Width-catSize.Width)*x/10,area.Top+(area.Height-catSize.Height)*y/10,catSize.Width,catSize.Height);var pose=PopupPlacement.Place(cat,new Size(304,214),area);
                var safe=new Rect(area.Left+10,area.Top+10,area.Width-20,area.Height-20);check(safe.Contains(pose.Bounds),"popup inside monitor safe area");var overlap=Rect.Intersect(cat,pose.Bounds);check(overlap.IsEmpty||overlap.Width*overlap.Height==0,"popup avoids artwork everywhere with room");check(new[]{"top","bottom","left","right"}.Contains(pose.Side),"popup valid tail side");
            }
            var tiny=PopupPlacement.Place(new Rect(0,0,190,200),new Size(304,214),new Rect(0,0,220,230));check(new Rect(0,0,220,230).Contains(tiny.Bounds),"tiny work area clamps and scales");
            check(PopupPlacement.Badge(1)=="①"&&PopupPlacement.Badge(2)=="②"&&PopupPlacement.Badge(20)=="⑳"&&PopupPlacement.Badge(21)=="21"&&PopupPlacement.Badge(100)=="99+","circled badge boundaries");
            var j=new JavaScriptSerializer();
            object list=j.DeserializeObject("{\"pinnedThreads\":[{\"id\":\"A\",\"kind\":\"codex\",\"hostId\":\"local\",\"title\":\"原标题\",\"status\":\"active\"}],\"threads\":[{\"id\":\"A\",\"kind\":\"codex\",\"hostId\":\"local\"},{\"id\":\"B\",\"kind\":\"codex\",\"hostId\":\"local\",\"status\":{\"type\":\"needsAttention\"}},{\"id\":\"C\",\"kind\":\"codex\",\"hostId\":\"local\",\"status\":\"idle\",\"isUnread\":true},{\"id\":\"D\",\"kind\":\"codex\",\"hostId\":\"remote\",\"status\":\"active\"},{\"id\":\"E\",\"kind\":\"chatgpt\",\"hostId\":\"local\"}]}");
            var parsed=CodexConnection.ParseTasks(list);check(parsed.Tasks.Count==3&&parsed.Count==3&&parsed.Running==1&&parsed.Waiting==1&&parsed.Unread==1,"task dedupe local filtering object/string states");check(parsed.Tasks[0].Title=="原标题","source title retained verbatim");
            var doc=j.DeserializeObject("{\"turns\":[{\"items\":[{\"type\":\"agentMessage\",\"phase\":\"final_answer\",\"text\":\"新回复\"}]},{\"items\":[{\"type\":\"userMessage\",\"content\":[{\"type\":\"text\",\"text\":\"旧问题\"}]},{\"type\":\"commandExecution\",\"text\":\"不应显示\"}]}]}");var transcript=CodexConnection.Transcript(doc);check(transcript.IndexOf("旧问题")<transcript.IndexOf("新回复")&&!transcript.Contains("不应显示"),"transcript chronology and tool exclusion");
            check(GrassFishUi.Create()[typeof(System.Windows.Controls.Button)] is System.Windows.Style,"grass fish WPF theme parses");
            PipeFixture(check,false);PipeFixture(check,true);NavigationFailureFixture(check);
            using(var bridge=new CodexConnection("unused","unused")){var task=new CodexTask{Id="existing",HostId="local",Kind="codex",CanReply=true};bool rejected=false;try{bridge.Send(task,"text").GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}check(rejected,"disconnected send gate");rejected=false;try{bridge.Open(task).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}check(rejected,"disconnected navigation gate");bridge.Current.Connected=true;bridge.Enable(false);rejected=false;try{bridge.Send(task,"text").GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}check(rejected,"disabled send gate");rejected=false;try{bridge.Open(task).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}check(rejected,"disabled navigation gate");}
            string appPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps/OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0/app/ChatGPT.exe");
            check(CodexWindowActivation.IsCodexDesktop(appPath),"verified existing Codex package window accepted");
            check(!CodexWindowActivation.IsCodexDesktop("C:/tmp/ChatGPT.exe")&&!CodexWindowActivation.IsCodexDesktop(appPath.Replace("OpenAI.Codex_","OpenAI.ChatGPT_")),"unrelated ChatGPT executable rejected");
        }
        static void Exact(Stream p,byte[] data){int offset=0;while(offset<data.Length){int n=p.Read(data,offset,data.Length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}}
        static void NavigationFailureFixture(Action<bool,string> check){
            string name="codex-browser-use-fixture-"+Guid.NewGuid().ToString("N");var json=new JavaScriptSerializer();
            using(var ready=new ManualResetEvent(false)){
                var server=Task.Run(()=>{using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
                    ready.Set();pipe.WaitForConnection();var h=new byte[4];Exact(pipe,h);var b=new byte[BitConverter.ToInt32(h,0)];Exact(pipe,b);var request=json.DeserializeObject(Encoding.UTF8.GetString(b));
                    var response=new{id=JsonValue.Number(request,"id"),jsonrpc="2.0",result=new{success=true,contentItems=new[]{new{type="inputText",text="{\"navigated\":false}"}}}};
                    var bytes=Encoding.UTF8.GetBytes(json.Serialize(response));var size=BitConverter.GetBytes(bytes.Length);pipe.Write(size,0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();
                }});
                ready.WaitOne(3000);using(var bridge=new CodexConnection("fixture-caller",@"\\.\pipe\"+name,true)){
                    bridge.Current.Connected=true;bool failed=false;try{bridge.Open(new CodexTask{Id="existing-chat",HostId="local",Kind="codex"}).GetAwaiter().GetResult();}catch(IOException){failed=true;}
                    check(failed,"unconfirmed page navigation is surfaced as failure");
                }
                check(server.Wait(5000),"navigation fixture completes without launching Codex");
            }
        }
        static void PipeFixture(Action<bool,string> check,bool failure){
            string name="codex-browser-use-fixture-"+Guid.NewGuid().ToString("N");var requests=new List<object>();var json=new JavaScriptSerializer();
            // The fixture owns an isolated pipe. Never routes test sends to the real desktop app.
            using(var ready=new ManualResetEvent(false)){
                var server=Task.Run(()=>{for(int i=0;i<(failure?1:4);i++)using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){ready.Set();pipe.WaitForConnection();byte[] h=new byte[4];Exact(pipe,h);var b=new byte[BitConverter.ToInt32(h,0)];Exact(pipe,b);object req=json.DeserializeObject(Encoding.UTF8.GetString(b));requests.Add(req);var args=JsonValue.Value(req,"params");string tool=JsonValue.Text(args,"tool");
                    object doc=tool=="list_threads"?new{pinnedThreads=new object[0],threads=new[]{new{ id="existing-chat",kind="codex",hostId="local",title="fixture",status="active"}}}:(object)new{thread=new{id="fixture-caller"},turns=new object[0]};
                    object response=failure?new{id=JsonValue.Number(req,"id"),jsonrpc="2.0",error=new{message="fixture rejected; not sent"}}:(object)new{id=JsonValue.Number(req,"id"),jsonrpc="2.0",result=new{success=true,contentItems=new[]{new{type="inputText",text=tool=="send_message_to_thread"?"Fixture accepted without JSON reply":tool=="navigate_to_codex_page"?json.Serialize(new{navigated=true}):json.Serialize(doc)}}}};var bytes=Encoding.UTF8.GetBytes(json.Serialize(response));var size=BitConverter.GetBytes(bytes.Length);pipe.Write(size,0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();
                }});
                ready.WaitOne(3000);using(var bridge=new CodexConnection("fixture-caller",@"\\.\pipe\"+name,true)){var task=new CodexTask{Id="existing-chat",HostId="local",Kind="codex",CanReply=true};
                    if(!failure){check(JsonValue.Bool(json.DeserializeObject(json.Serialize(bridge.Probe())),"ok"),"C# real framing fixture list/read");bridge.Send(task,"只发送到原聊天").GetAwaiter().GetResult();bridge.Open(task).GetAwaiter().GetResult();}
                    else {bridge.Current.Connected=true;bool failed=false;try{bridge.Send(task,"retain draft").GetAwaiter().GetResult();}catch(Exception){failed=true;}check(failed,"fixture rejected send surfaced");}
                }
                check(server.Wait(5000),"bounded fixture server completes");check(requests.Count==(failure?1:4),"no automatic retry or chat creation");
                foreach(var req in requests){var p=JsonValue.Value(req,"params");check(JsonValue.Text(p,"namespace")=="codex_app"&&JsonValue.Text(p,"threadId")=="fixture-caller","caller context retained");check(JsonValue.Text(req,"method")=="tools/call","only existing tool protocol");if(JsonValue.Text(p,"tool")=="send_message_to_thread"){var a=JsonValue.Object(JsonValue.Value(p,"arguments"));check(JsonValue.Text(a,"threadId")=="existing-chat"&&!a.ContainsKey("model")&&!a.ContainsKey("thinking"),"same chat send without model/settings override");}if(JsonValue.Text(p,"tool")=="navigate_to_codex_page"){var a=JsonValue.Object(JsonValue.Value(p,"arguments"));check(JsonValue.Text(a,"threadId")=="existing-chat"&&a.Count==1,"navigation selects the clicked original conversation only");}}
            }
        }
    }
}
