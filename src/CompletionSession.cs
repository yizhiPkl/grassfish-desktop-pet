using System;

namespace DesktopKitten {
    // A local completion acknowledgement, independent of Codex's short-lived
    // review snapshot. Never store prompts/results or infer acknowledgement
    // from which Codex window the user happens to open.
    public sealed class CompletionSession {
        bool initialized, wasRunning;
        DateTimeOffset observedEnd=DateTimeOffset.MinValue, dismissedEnd=DateTimeOffset.MinValue;
        double started;
        public bool Active { get; private set; }
        public bool AwaitingHandoff { get; private set; }
        public bool Previewing { get; private set; }
        public bool PromptReady { get; private set; }
        public void Update(Snapshot value, double now, bool enabled) {
            if(value==null)return;
            if(!initialized){initialized=true;observedEnd=value.EndAt;wasRunning=value.State=="running";return;}
            if((!enabled&&!Previewing) || value.State=="running" || value.State=="waiting" || value.State=="failed") {
                Active=false;AwaitingHandoff=false;Previewing=false;PromptReady=false;
            } else if(enabled && value.State=="review" && value.EndAt>observedEnd && value.EndAt>dismissedEnd) {
                Active=true;AwaitingHandoff=wasRunning;Previewing=false;PromptReady=false;started=now;
            }
            if(value.EndAt>observedEnd)observedEnd=value.EndAt;
            wasRunning=value.State=="running";
        }
        public bool TryHandoff(WorkPose work, int requiredIndex, bool animate, double now) {
            if(!Active || !AwaitingHandoff)return false;
            if(animate && work!=null && (work.IsEntry || work.Index!=requiredIndex))return false;
            AwaitingHandoff=false;started=now;return true;
        }
        // Called only after the pet has assigned its displayed completion frame.
        // Reveal the note with the first keyboard-exit frame, after the working
        // pose has handed off. No independent timer or wait for the waving loop.
        public bool TryPresentPrompt(WorkPose displayedCompletion) {
            if(!Active || AwaitingHandoff || PromptReady || displayedCompletion==null)return false;
            PromptReady=true;return true;
        }
        public double Elapsed(double now){return Math.Max(0,now-started);}
        public bool SuppressReview(Snapshot value){return value.State=="review" && value.EndAt<=dismissedEnd;}
        public void Dismiss(){if(observedEnd>dismissedEnd)dismissedEnd=observedEnd;Active=false;AwaitingHandoff=false;Previewing=false;PromptReady=false;}
        public void Preview(double now){Active=true;AwaitingHandoff=false;Previewing=true;PromptReady=false;started=now;}
    }
}
