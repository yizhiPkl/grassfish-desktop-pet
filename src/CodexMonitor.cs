using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesktopKitten {
    public sealed class Signal {
        public string Thread, Turn, Kind, Call, Reason;
        public DateTimeOffset At;
    }
    public sealed class TaskInfo {
        public string Turn, Status;
        public DateTimeOffset At;
        public HashSet<string> WaitingCalls = new HashSet<string>();
    }
    public sealed class TaskReducer {
        readonly Dictionary<string, TaskInfo> tasks = new Dictionary<string, TaskInfo>();
        public int Running { get { return tasks.Values.Count(t => t.Status == "running"); } }
        public int Waiting { get { return tasks.Values.Count(t => t.Status == "waiting"); } }
        public string LatestEnd = "idle";
        public DateTimeOffset EndAt = DateTimeOffset.MinValue;
        public bool Apply(Signal s) {
            TaskInfo t;
            if (!tasks.TryGetValue(s.Thread, out t)) { t = new TaskInfo { At = DateTimeOffset.MinValue, Status = "idle" }; tasks[s.Thread] = t; }
            if (s.At < t.At) return false;
            if (s.Kind == "task_started") {
                if (s.At == t.At && s.Turn == t.Turn) return true;
                if (s.At == t.At && t.Status != "running" && t.Status != "waiting") return false;
                t.Turn = s.Turn; t.Status = "running"; t.WaitingCalls.Clear();
            } else if (s.Kind == "task_complete" || s.Kind == "turn_aborted") {
                if ((t.Status == "running" || t.Status == "waiting") && s.Turn != t.Turn) return false;
                t.Status = "idle"; t.WaitingCalls.Clear();
                if (s.At >= EndAt) { EndAt = s.At; LatestEnd = s.Kind == "task_complete" ? "review" : s.Reason == "interrupted" || s.Reason == "cancelled" ? "idle" : "failed"; }
            } else if (s.Kind == "waiting" && (t.Status == "running" || t.Status == "waiting")) {
                t.WaitingCalls.Add(s.Call ?? "approval"); t.Status = "waiting";
            } else if (s.Kind == "tool_result" && t.WaitingCalls.Remove(s.Call ?? "")) {
                if (t.WaitingCalls.Count == 0) t.Status = "running";
            } else return false;
            t.At = s.At; return true;
        }
    }
    public sealed class Snapshot {
        public string State, Detail;
        public int Running, Waiting;
        public DateTimeOffset EndAt;
        public bool Connected;
    }
    public sealed class TailCursor {
        public long Offset;
        public bool Skip;
        public readonly List<byte> Prefix = new List<byte>(4096);
    }
    public static class RolloutReader {
        static readonly Regex Envelope = new Regex("^\\s*\\{\\s*\"timestamp\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*(?:\"ordinal\"\\s*:\\s*\\d+\\s*,\\s*)?\"type\"\\s*:\\s*\"(event_msg|response_item)\"\\s*,\\s*\"payload\"\\s*:\\s*\\{\\s*\"type\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex Id = new Regex("([0-9a-fA-F-]{36})\\.jsonl$", RegexOptions.Compiled);
        static string Field(string s, string name) {
            var m = Regex.Match(s, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : null;
        }
        public static Signal Parse(string prefix, string thread) {
            var m = Envelope.Match(prefix); DateTimeOffset at; if (!m.Success || !DateTimeOffset.TryParse(m.Groups[1].Value, out at)) return null;
            string envelope = m.Groups[2].Value, type = m.Groups[3].Value;
            if (envelope == "event_msg" && new[] { "task_started", "task_complete", "turn_aborted" }.Contains(type)) {
                Guid turn; string text = Field(prefix, "turn_id"); if (!Guid.TryParse(text, out turn)) return null;
                return new Signal { Thread = thread, Turn = turn.ToString(), Kind = type, At = at, Reason = Field(prefix, "reason") };
            }
            if (envelope == "response_item" && type == "function_call") {
                string name = Field(prefix, "name");
                if (name == "request_user_input" || (name != null && name.EndsWith(".request_user_input", StringComparison.Ordinal)))
                    return new Signal { Thread = thread, Kind = "waiting", Call = Field(prefix, "call_id"), At = at };
            }
            if (envelope == "response_item" && type == "function_call_output")
                return new Signal { Thread = thread, Kind = "tool_result", Call = Field(prefix, "call_id"), At = at };
            // Forward-compatible explicit approval events; never infer waiting from a slow shell command.
            if (envelope == "event_msg" && new[] { "exec_approval_request", "apply_patch_approval_request", "request_user_input" }.Contains(type))
                return new Signal { Thread = thread, Kind = "waiting", Call = Field(prefix, "call_id"), At = at };
            return null;
        }
        public static long Read(string path, TailCursor cursor, Action<Signal> accept) {
            var id = Id.Match(Path.GetFileName(path)); if (!id.Success) return 0;
            string thread = id.Groups[1].Value; long total = 0;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                long end = f.Length;
                if (cursor.Offset > end) { cursor.Offset = 0; cursor.Prefix.Clear(); cursor.Skip = false; }
                f.Position = cursor.Offset; byte[] buffer = new byte[65536];
                while (f.Position < end) {
                    int n = f.Read(buffer, 0, (int)Math.Min(buffer.Length, end - f.Position)); if (n <= 0) break;
                    cursor.Offset += n; total += n;
                    int index=0;
                    while(index<n) {
                        int newline=Array.IndexOf(buffer,(byte)10,index,n-index);
                        int length=(newline<0?n:newline)-index;
                        if(!cursor.Skip) {
                            int keep=Math.Min(length,4096-cursor.Prefix.Count);
                            for(int j=0;j<keep;j++)cursor.Prefix.Add(buffer[index+j]);
                        }
                        if (newline>=0) {
                            if (!cursor.Skip && cursor.Prefix.Count > 0) { var s = Parse(Encoding.UTF8.GetString(cursor.Prefix.ToArray()), thread); if (s != null) accept(s); }
                            cursor.Prefix.Clear(); cursor.Skip = false;
                            index=newline+1;
                        } else break;
                    }
                }
            }
            return total;
        }
        public static TailCursor Bootstrap(string path, Action<Signal> accept) {
            long len = new FileInfo(path).Length, span = 65536;
            while (true) {
                var c = new TailCursor { Offset = Math.Max(0, len - span), Skip = len > span }; var signals = new List<Signal>();
                Read(path, c, s => signals.Add(s));
                if (signals.Any(s => s.Kind == "task_started" || s.Kind == "task_complete" || s.Kind == "turn_aborted") || span >= len) {
                    foreach (var s in signals) accept(s); return c;
                }
                span = Math.Min(len, span * 2);
            }
        }
    }
    public static class NativeProcesses {
        public static bool IsCodexBackend(string path){
            if(string.IsNullOrEmpty(path))return false;path=path.Replace('/',Path.DirectorySeparatorChar);
            if(path.IndexOf(@"\OpenAI\Codex\bin\",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf(@"\.codex\",StringComparison.OrdinalIgnoreCase)>=0)return path.EndsWith(@"\codex.exe",StringComparison.OrdinalIgnoreCase);
            const string tail=@"\resources\codex.exe";
            return path.EndsWith(tail,StringComparison.OrdinalIgnoreCase)&&CodexWindowActivation.IsCodexDesktop(path.Substring(0,path.Length-tail.Length)+@"\ChatGPT.exe");
        }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct Entry {
            public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string Exe;
        }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref Entry e);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref Entry e);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static Dictionary<uint, DateTime> Find() {
            var result = new Dictionary<uint,DateTime>(); var snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == new IntPtr(-1)) return result;
            try {
                var e = new Entry { Size = (uint)Marshal.SizeOf(typeof(Entry)) };
                if (!Process32FirstW(snap, ref e)) return result;
                do {
                    if (!string.Equals(e.Exe, "codex.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var p = OpenProcess(0x1000, false, e.Pid); if (p == IntPtr.Zero) continue;
                    try {
                        var path = new StringBuilder(1024); int count = path.Capacity; long created, ended, kernel, user;
                        if (QueryFullProcessImageName(p, 0, path, ref count) && GetProcessTimes(p, out created, out ended, out kernel, out user)) {
                            string text = path.ToString();
                            if (IsCodexBackend(text))
                                result[e.Pid] = DateTime.FromFileTimeUtc(created);
                        }
                    } finally { CloseHandle(p); }
                } while (Process32NextW(snap, ref e));
            } finally { CloseHandle(snap); }
            return result;
        }
    }
    public sealed class CodexMonitor : IDisposable {
        public event Action<Snapshot> Changed;
        readonly string sessions;
        readonly object gate = new object();
        readonly HashSet<string> dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread worker;
        FileSystemWatcher watcher;
        volatile bool stop, enabled = true, rescan = true;
        Dictionary<string, TailCursor> cursors = new Dictionary<string, TailCursor>(StringComparer.OrdinalIgnoreCase);
        TaskReducer reducer = new TaskReducer();
        Dictionary<uint, DateTime> processes = new Dictionary<uint,DateTime>();
        DateTime cutoff = DateTime.MaxValue;
        bool recognized, parseFailure;
        public CodexMonitor(string home) {
            sessions = Path.Combine(home, "sessions");
            worker = new Thread(Loop) { IsBackground = true, Name = "Kitten state observer" };
        }
        public void Start() { worker.Start(); }
        public void Enable(bool value) { enabled = value; rescan = true; wake.Set(); }
        void Watch() {
            if (watcher != null || !Directory.Exists(sessions)) return;
            watcher = new FileSystemWatcher(sessions, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            FileSystemEventHandler queue = (s,e) => { lock(gate) dirty.Add(e.FullPath); wake.Set(); };
            watcher.Changed += queue; watcher.Created += queue; watcher.Renamed += (s,e) => { lock(gate) dirty.Add(e.FullPath); wake.Set(); };
            watcher.Error += (s,e) => { rescan = true; wake.Set(); }; watcher.EnableRaisingEvents = true;
        }
        void Reset(DateTime since) { cursors.Clear(); reducer = new TaskReducer(); recognized = false; parseFailure = false; cutoff = since; rescan = true; }
        void Accept(Signal s) { if (s.At.UtcDateTime < cutoff) return; if (s.Kind != "tool_result") recognized = true; reducer.Apply(s); }
        void Loop() {
            DateTime nextProcesses = DateTime.MinValue, nextScan = DateTime.MinValue; string previous = null;
            while (!stop) {
                try {
                    var now = DateTime.UtcNow;
                    if (now >= nextProcesses) {
                        var fresh = enabled&&CodexWindowActivation.IsRunning()?NativeProcesses.Find():new Dictionary<uint,DateTime>(); nextProcesses = now.AddSeconds(5);
                        bool changed = fresh.Count != processes.Count || fresh.Any(x => !processes.ContainsKey(x.Key) || processes[x.Key] != x.Value);
                        processes = fresh;
                        if (changed) Reset(fresh.Count == 0 ? DateTime.MaxValue : fresh.Values.Min());
                    }
                    if (enabled && processes.Count > 0) {
                        Watch();
                        if (rescan || now >= nextScan) {
                            rescan = false; nextScan = now.AddMinutes(2);
                            if (Directory.Exists(sessions)) foreach (var path in Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories))
                                if (File.GetLastWriteTimeUtc(path) >= cutoff) lock(gate) dirty.Add(path);
                        }
                        string[] files; lock(gate) { files = dirty.ToArray(); dirty.Clear(); }
                        foreach (var path in files) {
                            try {
                                if (!File.Exists(path)) continue; TailCursor c;
                                if (!cursors.TryGetValue(path, out c)) cursors[path] = RolloutReader.Bootstrap(path, Accept);
                                else RolloutReader.Read(path, c, Accept);
                            } catch (IOException) { lock(gate) dirty.Add(path); parseFailure = true; }
                            catch (UnauthorizedAccessException) { parseFailure = true; }
                        }
                    } else { if(watcher!=null){watcher.Dispose();watcher=null;}lock(gate) dirty.Clear(); }
                    string state = !enabled || processes.Count == 0 ? "idle" : reducer.Running > 0 ? "running" : reducer.Waiting > 0 ? "waiting" :
                        reducer.EndAt > DateTimeOffset.MinValue && (now - reducer.EndAt.UtcDateTime).TotalSeconds < 8 ? reducer.LatestEnd : "idle";
                    string detail = !enabled ? "Codex 联动已关闭" : processes.Count == 0 ? "Codex 未运行 · 提醒仍可使用" : parseFailure ? "状态读取异常 · 请检查权限" :
                        !recognized ? "已连接 Codex · 等待任务事件" : reducer.Running > 0 ? reducer.Running + " 个任务执行中" : reducer.Waiting > 0 ? reducer.Waiting + " 个任务等待输入" : "Codex 已连接 · 暂无执行任务";
                    if (parseFailure && reducer.Running == 0 && reducer.Waiting == 0) state = "unknown";
                    string signature = state + "|" + detail + "|" + reducer.EndAt.ToString("o");
                    if (signature != previous) { previous = signature; var handler = Changed; if (handler != null) handler(new Snapshot { State=state, Detail=detail, Running=enabled?reducer.Running:0, Waiting=enabled?reducer.Waiting:0, Connected=processes.Count>0, EndAt=reducer.EndAt }); }
                } catch (Exception e) {
                    if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is ArgumentException)) throw;
                    var handler = Changed; if (handler != null) handler(new Snapshot { State="unknown", Detail="暂时无法读取 Codex 状态" });
                }
                wake.WaitOne(500);
            }
        }
        public void Dispose() { stop = true; wake.Set(); if (watcher != null) watcher.Dispose(); if(worker.IsAlive)worker.Join(1500); }
    }
}
