using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopKitten {
    public static class Tests {
        public static string Run() {
            int count=0;var groups=new List<string>();Action<bool,string> check=(value,name)=>{if(!value)throw new Exception(name);count++;};
            string a="11111111-1111-4111-8111-111111111111",b="22222222-2222-4222-8222-222222222222";
            Func<string,string,string,int,Signal> signal=(thread,turn,kind,second)=>new Signal{Thread=thread,Turn=turn,Kind=kind,At=DateTimeOffset.Parse("2026-10-11T00:00:00Z").AddSeconds(second),Reason="interrupted",Call="input"};
            var reducer=new TaskReducer();reducer.Apply(signal("A",a,"task_started",0));check(reducer.Running==1,"start");
            reducer.Apply(signal("B",b,"task_started",1));check(reducer.Running==2,"parallel");reducer.Apply(signal("A",a,"waiting",2));check(reducer.Running==1&&reducer.Waiting==1,"waiting still other running");
            reducer.Apply(signal("A",a,"tool_result",3));check(reducer.Running==2,"resume input");reducer.Apply(signal("B",b,"task_complete",4));check(reducer.Running==1,"one completes");
            reducer.Apply(signal("A",a,"turn_aborted",5));check(reducer.Running==0&&reducer.LatestEnd=="idle","cancel");
            reducer.Apply(signal("A",b,"task_started",6));reducer.Apply(signal("A",a,"task_complete",7));check(reducer.Running==1,"old completion ignored");
            reducer.Apply(signal("A",b,"waiting",8));reducer.Apply(signal("A",b,"task_started",6));check(reducer.Waiting==1,"out of order start");
            reducer.Apply(signal("A",b,"tool_result",9));reducer.Apply(signal("A",b,"task_complete",10));check(reducer.Running==0&&reducer.LatestEnd=="review","complete");groups.Add("task reducer");
            check(Motion.Frame(Motion.Durations["running"],820*100+121)==1,"running loops indefinitely");
            check(Motion.Frame(Motion.Idle,0)==0&&Motion.Frame(Motion.Idle,6600)==0,"idle loop");
            check(Motion.BlinkPhase(4050)==1&&Motion.BlinkPhase(4100)==2&&Motion.BlinkPhase(4200)==3&&Motion.BlinkPhase(4800)==0,"blink timing");
            check(Motion.Direction(0,-100,-1,320)==0,"look up");check(Motion.Direction(100,0,-1,320)==8,"look right");check(Motion.Direction(-100,0,-1,320)==24,"look left");
            check(Motion.Direction(0,100,-1,320)==16,"look down");check(Motion.Direction(10,10,-1,320)==-1,"deadzone");check(Motion.Direction(500,0,-1,320)==-1,"outside range");
            check(Motion.Direction(Math.Sin(7.5*Math.PI/180)*100,-Math.Cos(7.5*Math.PI/180)*100,0,320)==0,"hysteresis");groups.Add("native frames and approved directions");
            check(new Settings().PetSize=="small","default small cat");
            check(PetSizing.Dimensions("small")==new System.Windows.Size(96,104),"small half-size");
            check(PetSizing.Dimensions("medium")==new System.Windows.Size(144,156),"medium three-quarter-size");
            check(PetSizing.Dimensions("large")==new System.Windows.Size(192,208),"large original size");
            check(PetSizing.Normalize(null)=="small"&&PetSizing.Normalize("invalid")=="small","invalid size fallback");
            var full=new System.Windows.Rect(100,300,192,208);var resized=PetSizing.ResizeKeepingFeet(full,"small");
            check(resized.Bottom==full.Bottom&&resized.Left+resized.Width/2==full.Left+full.Width/2,"resize holds feet and horizontal center");
            check(PetSizing.ResizeKeepingFeet(resized,"large")==full,"resize roundtrip");
            foreach(var preset in new[]{"small","medium","large"}){
                var size=PetSizing.Dimensions(preset);check(size.Width/size.Height==192.0/208,"aspect ratio "+preset);
                var pet=new System.Windows.Rect(1800,900,size.Width,size.Height);var area=new System.Windows.Rect(0,0,1920,1080);
                var bubble=PetSizing.BubbleBounds(pet,new System.Windows.Size(286,176),area);
                check(area.Contains(bubble),"bubble clamped "+preset);
                var leftArea=new System.Windows.Rect(-1920,0,1920,1080);
                check(leftArea.Contains(PetSizing.BubbleBounds(new System.Windows.Rect(-100,40,size.Width,size.Height),new System.Windows.Size(286,176),leftArea)),"secondary monitor bubble "+preset);
            }
            check(Motion.Direction(25,0,-1,320,38*.5)==8,"small cat scaled gaze deadzone");
            check(Motion.Direction(25,0,-1,320)==-1,"large gaze deadzone unchanged");
            check(CuteUi.Create()["SizeChoice"] is System.Windows.Style,"size selector style parses");groups.Add("three size presets and scale-aware geometry");
            var panelLayout=(System.Windows.Controls.Grid)System.Windows.Markup.XamlReader.Parse(PanelWindow.Layout);
            check(panelLayout.FindName("SizeSmall") is System.Windows.Controls.RadioButton&&panelLayout.FindName("SizeMedium") is System.Windows.Controls.RadioButton&&panelLayout.FindName("SizeLarge") is System.Windows.Controls.RadioButton,"standalone control panel XAML parses with all three presets");
            check(Store.NormalizeTimes("8:00，11:30;18:30,11:30")=="08:00, 11:30, 18:30","times normalized");
            bool invalid=false;try{Store.ParseTimes("24:00");}catch(FormatException){invalid=true;}check(invalid,"invalid hour");
            var local=new DateTime(2026,10,11,11,29,55);var utc=new DateTime(2026,10,11,3,29,55,DateTimeKind.Utc);
            var cfg=new Settings{WaterMinutes=20,WaterTimesEnabled=true,WaterTimes="11:30",MealTimes="11:30"};
            var scheduler=new Scheduler(cfg,local,utc);check(!scheduler.Tick(local.AddSeconds(3),utc.AddSeconds(3)),"before fixed");
            check(scheduler.Tick(local.AddSeconds(6),utc.AddSeconds(6))&&cfg.Pending.Count==2,"water and meal together queued");
            check(!scheduler.Tick(local.AddSeconds(7),utc.AddSeconds(7))&&cfg.Pending.Count==2,"fixed no duplicate");
            check(scheduler.Tick(local.AddMinutes(20),utc.AddMinutes(20))&&cfg.Pending.Count==3,"interval");
            scheduler.Tick(local.AddMinutes(20),utc.AddMinutes(20));check(cfg.Pending.Count==3,"interval no duplicate");
            scheduler.Dismiss();check(cfg.Pending.Count==2,"dismiss one only");scheduler.AddPreview("meal");check(cfg.Pending.Count==3,"preview queues");
            var sleep=new Settings{WaterIntervalEnabled=false,MealTimes="11:30, 18:30"};var resumed=new Scheduler(sleep,local,utc);resumed.Tick(local.AddHours(8),utc.AddHours(8));check(sleep.Pending.Count==2,"sleep catches today");
            var rollback=new Settings{WaterIntervalEnabled=false,MealTimes="11:30"};var backwards=new Scheduler(rollback,local,utc);backwards.Tick(local.AddSeconds(6),utc.AddSeconds(6));backwards.Tick(local.AddMinutes(-10),utc.AddMinutes(-10));backwards.Tick(local.AddSeconds(6),utc.AddSeconds(6));check(rollback.Pending.Count==1,"clock rollback dedupe");groups.Add("reminder schedule and persistent queue");
            string folder=Path.Combine(Path.GetTempPath(),"kitten-test-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try {
                cfg.PetSize="medium";var store=new Store(folder);store.Save(cfg);var loaded=store.Load();check(loaded.Pending.Count==3&&loaded.WaterMinutes==20,"settings roundtrip");check(loaded.PetSize=="medium","size choice persists");store.Save(loaded);check(File.Exists(store.FilePath+".bak"),"atomic backup");
                File.WriteAllText(store.FilePath,"{\"SchemaVersion\":1,\"WaterMinutes\":20}",Encoding.UTF8);var legacy=store.Load();check(legacy.PetSize=="small"&&legacy.WaterMinutes==20&&store.Warning==null,"legacy settings default small without wiping other settings");
                cfg.PetSize="bad";store.Save(cfg);check(store.Load().PetSize=="small","stored invalid size normalizes");
                var file=Path.Combine(folder,"rollout-"+a+".jsonl");string line="{\"timestamp\":\"2026-10-11T00:00:00Z\",\"ordinal\":5275,\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_attribution\":{\"turn_id\":\""+a+"\"},\"turn_id\":\""+a+"\"}}";
                File.WriteAllText(file,line.Substring(0,30),new UTF8Encoding(false));var cursor=new TailCursor();var seen=new List<Signal>();RolloutReader.Read(file,cursor,s=>seen.Add(s));check(seen.Count==0,"partial line");
                File.AppendAllText(file,line.Substring(30)+"\n",new UTF8Encoding(false));RolloutReader.Read(file,cursor,s=>seen.Add(s));check(seen.Count==1,"native ordinal read");
                RolloutReader.Read(file,cursor,s=>seen.Add(s));check(seen.Count==1,"incremental no repeats");
                var wait=RolloutReader.Parse("{\"timestamp\":\"2026-10-11T00:00:01Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"functions.request_user_input\",\"call_id\":\"q1\"}}","A");check(wait!=null&&wait.Kind=="waiting"&&wait.Call=="q1","request input recognized");
                check(RolloutReader.Parse("{\"timestamp\":\"2026-10-11T00:00:01Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"text\":\"task_started\"}}","A")==null,"never infer text");
                File.WriteAllText(store.FilePath,"invalid",Encoding.UTF8);check(store.Load().WaterMinutes==30&&store.Warning!=null,"corrupt settings safe");
            }finally{foreach(var f in Directory.GetFiles(folder))File.Delete(f);Directory.Delete(folder,false);}
            groups.Add("settings and incremental reader");
            var art=new Assets(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),"assets"));
            var work=art.Work;check(work!=null,"new working clip installed");
            check(work.Width==272&&work.Height==248&&work.OriginX==-24&&work.OriginY==0,"work canvas keeps cat scale with left tail margin");
            check(work.Entry.Length>1&&work.Loop.Length>6,"entrance and multi-pose loop separated");
            check(work.Sample(0,true).IsEntry&&work.Sample(work.EntryDurationMs-1,true).IsEntry,"entrance plays once");
            check(!work.Sample(work.EntryDurationMs,true).IsEntry&&work.Sample(work.EntryDurationMs,true).Index==0,"exact entrance to loop boundary");
            check(!work.Sample(work.EntryDurationMs+work.LoopDurationMs*1000,true).IsEntry&&work.Sample(work.EntryDurationMs+work.LoopDurationMs*1000,true).Index==0,"working loop never replays entrance");
            check(work.Sample(work.EntryDurationMs+work.LoopDurationMs+130,true).Key==work.Sample(work.EntryDurationMs+130,true).Key,"working cadence stable across cycles");
            check(!work.Sample(0,false).IsEntry&&work.Sample(12345,false).Index==0,"reduced motion shows keyboard work pose without entering");
            check(work.Sample(-1,true).Index==0&&work.Sample(double.NaN,true).Index==0,"invalid elapsed clamped");
            foreach(var f in work.Entry.Concat(work.Loop)){
                var pose=new WorkPose{File=f.File,IsEntry=work.Entry.Contains(f),Index=Array.IndexOf(work.Entry.Contains(f)?work.Entry:work.Loop,f)};
                var bitmap=art.WorkFrame(pose);check(bitmap.PixelWidth==work.Width&&bitmap.PixelHeight==work.Height,"all work pictures load "+f.File);
                check(object.ReferenceEquals(bitmap,art.WorkFrame(pose)),"work cache reused "+f.File);
            }
            var entering=art.WorkFrame(work.Sample(0,true));
            var enteringCat=new CroppedBitmap(entering,new System.Windows.Int32Rect(24,0,192,208));
            check(Pixels(enteringCat).SequenceEqual(Pixels(art.Frame(0,0,0))),"entrance starts pixel-identical to original idle");
            var session=new WorkSession();session.Update(true,100);session.Update(true,300);
            check(session.Elapsed(500)==400,"repeated status updates do not reset work clock");
            session.Update(true,1000);check(session.Elapsed(1200)==1100,"drag or hide does not reset task clock");
            session.Update(false,1300);session.Update(true,2000);check(session.Elapsed(2050)==50,"new work after completion or approval has one new entrance");
            bool unsafeWork=false;try{var invalidWork=new WorkAnimation{SchemaVersion=1,Width=272,Height=248,Entry=new[]{new WorkFrame{File="../bad.png",DurationMs=100}},Loop=work.Loop};invalidWork.Validate();}catch(InvalidDataException){unsafeWork=true;}
            check(unsafeWork,"unsafe work paths rejected");groups.Add("one-shot keyboard entrance and persistent working loop");
            for(int i=0;i<16;i++){var f=art.Frame(9+i/8,i%8,0);check(f.PixelWidth==192&&f.PixelHeight==208,"look frame "+i);}
            GazeTests.Run(art,check);groups.Add("32 video-only gaze directions, strict assets and bounded shortest-path following");
            check(object.ReferenceEquals(art.Frame(0,0,0),art.Frame(0,0,2)),"reduced-motion first cell static");
            string assetDirectory=Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),"assets");
            var sheets=new BitmapSource[4];
            for(int phase=0;phase<4;phase++){
                var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(Path.Combine(assetDirectory,phase==0?"base.png":"blink-"+phase+".png"));image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();
                sheets[phase]=image;
            }
            for(int row=0;row<11;row++)for(int col=0;col<8;col++){
                var reference=new CroppedBitmap(sheets[0],new System.Windows.Int32Rect(col*192,row*208,192,208));
                check(Pixels(art.Frame(row,col,0)).SequenceEqual(Pixels(reference)),"exact standalone tile "+row+":"+col);
            }
            for(int phase=1;phase<=3;phase++)for(int col=1;col<6;col++){
                var reference=new CroppedBitmap(sheets[phase],new System.Windows.Int32Rect(col*192,0,192,208));
                check(Pixels(art.Frame(0,col,phase)).SequenceEqual(Pixels(reference)),"exact blink tile "+col+":"+phase);
            }
            groups.Add("103 pixel-exact runtime tiles");
            CompletionTests.Run(art,check);groups.Add("persistent completion notification and aligned one-shot keyboard exit");
            PettingTests.Run(art,check);groups.Add("calibrated enjoyment and approved natural recovery player");
            GrassFishTests.Run(check);groups.Add("custom schedules migration and restart dedupe, 1452 popup positions, local task filtering and isolated named-pipe send fixtures");
            ReminderWorkflowTests.Run(check);groups.Add("complete-snooze-skip-close history, today agenda, manual/daily quiet, restart and legacy migration");
            NoticePriorityTests.Run(check);groups.Add("unified reminder/task arbitration, important preemption, priority editing, snooze, quiet summary and restart");
            DistributionTests.Run(check);groups.Add("clean-user Codex profile discovery, package identity, component integrity and pet menu activation lifecycle");
            CodexCompatibilityTests.Run(check);groups.Add("capability-based endpoint discovery, bounded candidates, version-independent verified cache, legacy migration, corruption rejection and unknown-build fallback");
            NativeOverlayTests.Run(check);groups.Add("official notification lifecycle, activity summaries and multi-screen task overlay geometry");
            UpdateTests.Run(check);groups.Add("signed update trust, numeric versions, daily checks, opt-out, skip, cancellation, busy gate, invalid downloads, draft guards, bounded backup and fixture-only install recovery");
            return new JavaScriptSerializer().Serialize(new{ok=true,assertions=count,groups,imagesGenerated=0,registryModified=false,kind="automated fixtures; not all real desktop interactions"});
        }
        public static byte[] Pixels(BitmapSource source){var bitmap=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);return data;}
    }
}
