using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace DesktopKitten {
    public sealed class WorkFrame {
        public string File;
        public int DurationMs;
    }
    public sealed class WorkPose {
        public bool IsEntry;
        public int Index;
        public string File;
        public string Key { get { return "work:" + (IsEntry ? "enter:" : "loop:") + Index; } }
    }
    public sealed class WorkAnimation {
        public int SchemaVersion, Width, Height, OriginX, OriginY, HandoffWorkIndex;
        public WorkFrame[] Entry, Loop;
        // Optional completion-only recovery tracks. Working animation is unchanged.
        public WorkFrame[] Recovery;
        public WorkFrame[] EntryRecovery;
        int[] entryTimes, loopTimes;
        public int EntryDurationMs { get { return entryTimes.Sum(); } }
        public int LoopDurationMs { get { return loopTimes.Sum(); } }
        public static WorkAnimation Load(string path) {
            if (!System.IO.File.Exists(path)) return null; // Old portable bundles remain compatible.
            if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("工作动画配置过大");
            var animation = new JavaScriptSerializer().Deserialize<WorkAnimation>(System.IO.File.ReadAllText(path));
            if (animation == null) throw new InvalidDataException("工作动画配置为空");
            animation.Validate();
            return animation;
        }
        public void Validate() {
            if (SchemaVersion != 1 || Width < 192 || Width > 512 || Height < 208 || Height > 512 || HandoffWorkIndex < 0 || HandoffWorkIndex > 127 ||
                OriginX > 0 || OriginX < -128 || OriginY > 0 || OriginY < -128)
                throw new InvalidDataException("工作动画画布配置无效");
            CheckFrames(Entry, "入场"); CheckFrames(Loop, "循环");
            if(Recovery!=null){CheckFrames(Recovery,"恢复");if(Loop.Any(f=>!Recovery.Any(r=>r.File==f.File)))throw new InvalidDataException("完成动画恢复路径缺少循环帧");}
            if(EntryRecovery!=null){CheckFrames(EntryRecovery,"入场恢复");if(Entry.Any(f=>!EntryRecovery.Any(r=>r.File==f.File)))throw new InvalidDataException("完成动画恢复路径缺少入场帧");}
            entryTimes = Entry.Select(f => f.DurationMs).ToArray();
            loopTimes = Loop.Select(f => f.DurationMs).ToArray();
        }
        static void CheckFrames(WorkFrame[] frames, string label) {
            if (frames == null || frames.Length == 0 || frames.Length > 128) throw new InvalidDataException("工作动画" + label + "帧数无效");
            foreach (var f in frames) {
                if (f == null || f.DurationMs < 30 || f.DurationMs > 1000 || string.IsNullOrEmpty(f.File) ||
                    Path.GetFileName(f.File) != f.File || f.File.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                    !f.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("工作动画" + label + "帧配置无效");
            }
        }
        public WorkPose Sample(double elapsedMs, bool animate) {
            if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs)) elapsedMs = 0;
            elapsedMs = Math.Max(0, elapsedMs);
            bool entry = animate && elapsedMs < EntryDurationMs;
            int index = !animate ? 0 : Motion.Frame(entry ? entryTimes : loopTimes, entry ? elapsedMs : elapsedMs - EntryDurationMs);
            return new WorkPose { IsEntry = entry, Index = index, File = (entry ? Entry : Loop)[index].File };
        }
    }
    // Independent from pet interaction states: dragging/hiding must not restart
    // the keyboard entrance while the same aggregated task is still running.
    public sealed class WorkSession {
        bool running;
        double started;
        public void Update(bool value, double nowMs) {
            if (value && !running) started = nowMs;
            running = value;
        }
        public double Elapsed(double nowMs) { return Math.Max(0, nowMs - started); }
    }
}
