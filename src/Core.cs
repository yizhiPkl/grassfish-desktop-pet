using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DesktopKitten {
    public sealed class Settings {
        public int SchemaVersion = 1;
        public bool MonitorCodex = true, FollowMouse = true, Animations = true, WaterIntervalEnabled = true, WaterTimesEnabled = false, MealEnabled = true;
        public bool ShowCompletionPopup = true, ShowTaskPopup = true, ShowReminderPopup = true;
        public int WaterMinutes = 30, FollowRadius = 320;
        public string WaterTimes = "10:00, 15:00", MealTimes = "11:30, 18:30";
        public string PetSize = "small";
        public double X = -1, Y = -1;
        public DateTime WaterNextUtc = DateTime.MinValue;
        public List<string> Fired = new List<string>();
        public List<Reminder> Pending = new List<Reminder>();
        public List<ScheduleRule> CustomSchedules = new List<ScheduleRule>();
        public List<Reminder> ReminderHistory = new List<Reminder>();
        public DateTime QuietUntilUtc = DateTime.MinValue, QuietBypassUntilUtc = DateTime.MinValue;
        public bool QuietDailyEnabled;
        public bool QuietSummaryPending;
        public string QuietStart = "22:30", QuietEnd = "08:00";
    }
    public sealed class Reminder {
        public string Key, Kind, Title, Message, ScheduleId;
        public DateTime Due;
        public DateTime SnoozeUntilUtc=DateTime.MinValue, HandledUtc=DateTime.MinValue;
        public string Outcome;
        public bool AllowDuringQuiet;
    }
    public sealed class Store {
        public readonly string DirectoryPath, FilePath;
        public string Warning;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        public Store(string directory) { DirectoryPath = directory; FilePath = Path.Combine(directory, "settings.json"); }
        public Settings Load() {
            try {
                if (!File.Exists(FilePath)) return new Settings();
                if (new FileInfo(FilePath).Length > 1024 * 1024) throw new InvalidDataException("设置文件过大");
                var s = Json.Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (s == null || s.SchemaVersion != 1) throw new InvalidDataException("设置版本无法识别");
                s.WaterMinutes = Math.Max(1, Math.Min(1440, s.WaterMinutes));
                s.FollowRadius = Math.Max(100, Math.Min(1000, s.FollowRadius));
                s.PetSize = PetSizing.Normalize(s.PetSize);
                s.Fired = s.Fired ?? new List<string>(); s.Pending = s.Pending ?? new List<Reminder>();
                s.ReminderHistory=s.ReminderHistory??new List<Reminder>();
                s.Pending=s.Pending.Where(r=>r!=null).ToList();
                s.ReminderHistory=s.ReminderHistory.Where(r=>r!=null).OrderBy(r=>r.HandledUtc).ToList();
                if(s.ReminderHistory.Count>200)s.ReminderHistory=s.ReminderHistory.Skip(s.ReminderHistory.Count-200).ToList();
                foreach(var r in s.Pending.Concat(s.ReminderHistory)){
                    if(r.Due!=DateTime.MinValue)r.Due=WallClock(r.Due);
                    if(string.IsNullOrEmpty(r.Key))r.Key="legacy:"+Guid.NewGuid().ToString("N");
                    if(r.Kind=="water"||r.Kind=="meal")r.Title=ReminderWorkflow.Title(r.Kind);
                }
                try{QuietPolicy.Validate(s.QuietStart,s.QuietEnd);}catch(FormatException){s.QuietDailyEnabled=false;s.QuietStart="22:30";s.QuietEnd="08:00";Warning="安静时段配置无效，已关闭每日免打扰，其他设置保留。";}
                ParseTimes(s.WaterTimes); ParseTimes(s.MealTimes);
                s.CustomSchedules=s.CustomSchedules??new List<ScheduleRule>();
                var ids=new HashSet<string>();
                foreach(var rule in s.CustomSchedules.Where(x=>x!=null)){
                    // JavaScriptSerializer reads its /Date()/ format as UTC. Restore local wall-clock fields.
                    if(rule.DueLocal!=DateTime.MinValue)rule.DueLocal=WallClock(rule.DueLocal);
                    if(rule.LastFiredLocal!=DateTime.MinValue)rule.LastFiredLocal=WallClock(rule.LastFiredLocal);
                    Guid id;if(!Guid.TryParse(rule.Id,out id)||!ids.Add(rule.Id)){rule.Id=Guid.NewGuid().ToString("N");ids.Add(rule.Id);}
                    try{rule.Validate();}catch(FormatException){rule.Enabled=false;Warning="部分自定义提醒配置无效，已暂时关闭，其他设置保留。";}
                }
                s.CustomSchedules=s.CustomSchedules.Where(x=>x!=null).Take(100).ToList();
                return s;
            } catch (Exception e) {
                Warning = "设置读取失败，已使用默认值。原文件未删除：" + e.GetType().Name;
                return new Settings();
            }
        }
        public void Save(Settings settings) {
            Directory.CreateDirectory(DirectoryPath);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, Json.Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
        }
        static DateTime WallClock(DateTime value){return DateTime.SpecifyKind(value.Kind==DateTimeKind.Utc?value.ToLocalTime():value,DateTimeKind.Unspecified);}
        public static List<TimeSpan> ParseTimes(string input) {
            var result = new List<TimeSpan>();
            foreach (string part in (input ?? "").Replace('，', ',').Replace('；', ',').Replace(';', ',').Split(',')) {
                var value = part.Trim(); if (value.Length == 0) continue;
                DateTime dt;
                if (!DateTime.TryParseExact(value, new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    throw new FormatException("时间请写成 11:30，多个时间用逗号分开。");
                if (!result.Contains(dt.TimeOfDay)) result.Add(dt.TimeOfDay);
            }
            if (result.Count > 24) throw new FormatException("每项最多设置24个时间点。");
            return result.OrderBy(x => x).ToList();
        }
        public static string NormalizeTimes(string text) { return string.Join(", ", ParseTimes(text).Select(x => x.ToString(@"hh\:mm"))); }
    }
    public static class Startup {
        const string Name = "DesktopKitten";
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled(string exe) {
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && string.Equals(key.GetValue(Name) as string, "\"" + exe + "\" --background", StringComparison.OrdinalIgnoreCase);
        }
        public static void Set(bool enabled, string exe) {
            using (var key = Registry.CurrentUser.CreateSubKey(Key)) {
                if (enabled) key.SetValue(Name, "\"" + exe + "\" --background", RegistryValueKind.String);
                else {
                    // Only remove our own launch registration; do not erase an unrelated value with the same name.
                    var current = key.GetValue(Name) as string;
                    if (current != null && current.StartsWith("\"" + exe + "\"", StringComparison.OrdinalIgnoreCase)) key.DeleteValue(Name, false);
                }
            }
        }
    }
    public sealed partial class Scheduler {
        readonly Settings settings;
        DateTime lastLocal;
        public Scheduler(Settings s, DateTime local, DateTime utc) {
            settings = s; lastLocal = local;
            if (s.WaterNextUtc == DateTime.MinValue) s.WaterNextUtc = utc.AddMinutes(s.WaterMinutes);
            lastReady=Current(local,utc)==null?null:Current(local,utc).Key;lastQuiet=QuietPolicy.IsQuiet(s,local,utc);
        }
        void Add(Reminder r) {
            if (settings.Fired.Contains(r.Key)||settings.ReminderHistory.Any(h=>h.Key==r.Key)) return;
            settings.Fired.Add(r.Key); settings.Pending.Add(r);
        }
        void Fixed(string kind, string text, DateTime local) {
            // Resume from sleep: catch today's missed due times once; never flood old days.
            foreach (var time in Store.ParseTimes(text)) {
                var due = local.Date.Add(time);
                if (due > local || due <= lastLocal) continue;
                string key = kind + ":" + due.ToString("yyyy-MM-ddTHH:mm");
                Add(new Reminder { Key = key, Kind = kind, Due = due,
                    Title = ReminderWorkflow.Title(kind),
                    Message = kind == "water" ? "陪小猫喝口水吧，慢慢来就好。" : "先把忙碌放一放，好好吃一顿饭吧。" });
            }
        }
        public bool Tick(DateTime local, DateTime utc) {
            int before = settings.Pending.Count;
            int historyBefore=settings.ReminderHistory.Count;if(historyPruneDay!=utc.Date){TrimHistory(utc);historyPruneDay=utc.Date;}
            if (settings.WaterIntervalEnabled && utc >= settings.WaterNextUtc) {
                Add(new Reminder { Key = "interval:" + settings.WaterNextUtc.ToString("o"), Kind = "water", Due = local,
                    Title = ReminderWorkflow.Title("water"), Message = "已经忙了一会儿，喝口水再继续喵。" });
                settings.WaterNextUtc = utc.AddMinutes(settings.WaterMinutes);
            }
            if (local >= lastLocal) {
                if (settings.WaterTimesEnabled) Fixed("water", settings.WaterTimes, local);
                if (settings.MealEnabled) Fixed("meal", settings.MealTimes, local);
            }
            lastLocal = local;
            // Keep today's recent keys and pending reminders, without an indefinitely growing dedupe set.
            if (settings.Fired.Count > 240) {
                var pendingKeys = new HashSet<string>(settings.Pending.Select(x => x.Key));
                settings.Fired = settings.Fired.Where(x => pendingKeys.Contains(x)).Concat(settings.Fired.Skip(settings.Fired.Count - 120)).Distinct().ToList();
            }
            bool changed=CustomTick(local,utc);
            bool presentation=PresentationChanged(local,utc);return settings.Pending.Count != before||changed||presentation||historyBefore!=settings.ReminderHistory.Count;
        }
        public void ResetInterval(DateTime utc) { settings.WaterNextUtc = utc.AddMinutes(settings.WaterMinutes); }
        public void AddPreview(string kind) {
            Add(new Reminder { Key = "preview:" + Guid.NewGuid(), Kind = kind, Due = DateTime.Now,
                Title = ReminderWorkflow.Title(kind),
                Message = kind == "water" ? "陪小猫喝口水吧，慢慢来就好。" : "先把忙碌放一放，好好吃一顿饭吧。" });
        }
        public void Dismiss() { if (settings.Pending.Count > 0) Resolve(settings.Pending[0].Key,"closed",DateTime.UtcNow); }
        public void PreviewSchedule(ScheduleRule rule){Add(new Reminder{Key="preview:"+Guid.NewGuid(),Kind="custom",Title=rule.Title,Message=rule.Message,ScheduleId=rule.Id,Due=DateTime.Now,AllowDuringQuiet=rule.AllowDuringQuiet});}
        bool CustomTick(DateTime local,DateTime utc){
            bool changed=false;
            foreach(var r in settings.CustomSchedules.Where(x=>x!=null&&x.Enabled&&!x.Completed)){
                DateTime? due=null;
                if(r.Mode=="interval"){
                    if(r.NextUtc==DateTime.MinValue){r.NextUtc=utc.AddMinutes(r.Minutes);changed=true;}
                    else if(utc>=r.NextUtc){due=local;r.NextUtc=utc.AddMinutes(r.Minutes);changed=true;}
                }else if(r.Mode=="once"){
                    if(local>=r.DueLocal){due=r.DueLocal;r.Completed=true;r.Enabled=false;changed=true;}
                }else if(r.Mode=="daily"||r.Mode=="weekly"){
                    var time=Store.ParseTimes(r.Time).Single();var candidate=local.Date.Add(time);
                    if(candidate<=local&&candidate>r.LastFiredLocal&&(r.Mode=="daily"||r.Days.Contains((int)local.DayOfWeek))){due=candidate;r.LastFiredLocal=candidate;changed=true;}
                }
                // One outstanding paper note per custom rule; long sleep or absence cannot flood the queue.
                if(due.HasValue&&!settings.Pending.Any(p=>p.ScheduleId==r.Id))Add(new Reminder{Key="custom:"+r.Id+":"+due.Value.ToString("yyyy-MM-ddTHH:mm:ss")+":"+(r.Mode=="interval"?r.NextUtc.Ticks.ToString():""),Kind="custom",Title=r.Title,Message=string.IsNullOrWhiteSpace(r.Message)?"草鱼提醒你：到约定的时间啦。":r.Message,ScheduleId=r.Id,Due=due.Value,AllowDuringQuiet=r.AllowDuringQuiet});
            }
            return changed;
        }
        public string NextWaterText(DateTime utc) {
            return settings.WaterIntervalEnabled ? "下次间隔提醒约 " + settings.WaterNextUtc.ToLocalTime().ToString("HH:mm") : "间隔提醒已关闭";
        }
    }
    public static class Motion {
        public const int GazeDirectionCount=32;
        public static readonly int[] Idle = { 1680, 660, 660, 840, 840, 1920 };
        public static readonly Dictionary<string, int[]> Durations = new Dictionary<string, int[]> {
            { "idle", Idle }, { "running", new[] {120,120,120,120,120,220} },
            { "running-right", new[] {120,120,120,120,120,120,120,220} }, { "running-left", new[] {120,120,120,120,120,120,120,220} },
            { "waving", new[] {140,140,140,280} }, { "jumping", new[] {140,140,140,140,280} },
            { "failed", new[] {140,140,140,140,140,140,140,240} },
            { "waiting", new[] {150,150,150,150,150,260} }, { "review", new[] {150,150,150,150,150,280} }
        };
        public static readonly Dictionary<string,int> Rows = new Dictionary<string,int> {
            {"idle",0},{"running-right",1},{"running-left",2},{"waving",3},{"jumping",4},{"failed",5},{"waiting",6},{"running",7},{"review",8}
        };
        public static int Frame(int[] durations, double ms) {
            double time = ((ms % durations.Sum()) + durations.Sum()) % durations.Sum();
            for (int i = 0; i < durations.Length; i++) { if (time < durations[i]) return i; time -= durations[i]; }
            return 0;
        }
        public static int BlinkPhase(double ms) {
            var t = ms % 4800; return t < 4000 ? 0 : t < 4065 ? 1 : t < 4150 ? 2 : t < 4225 ? 3 : 0;
        }
        public static int Direction(double dx, double dy, int previous, double radius, double deadzone = 38) {
            double distance = Math.Sqrt(dx * dx + dy * dy); if (distance <= deadzone || distance > radius) return -1;
            double a = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            if (previous >= 0) {
                double difference = Math.Abs(a - previous * (360.0/GazeDirectionCount)); difference = Math.Min(difference, 360 - difference);
                if (difference < 180.0/GazeDirectionCount+3) return previous;
            }
            return ((int)Math.Floor(a / (360.0/GazeDirectionCount) + .5)) % GazeDirectionCount;
        }
    }
}
