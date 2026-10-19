using System;
using System.Linq;
namespace DesktopKitten {
    // The close button acknowledges immediately, while the pet finishes a bounded
    // path from the current tail direction to exact idle. Entry/keyboard are
    // never reversed; tail-only return frames follow the authored path.
    public sealed class CompletionReturn {
        readonly WorkAnimation clip;
        WorkFrame[] track;
        int first;double started,sourceDuration;
        public bool Active {get;private set;}
        public double Duration {get;private set;}
        public CompletionReturn(WorkAnimation clip){this.clip=clip;}
        public bool Begin(WorkPose displayed,double now,bool animate){
            Cancel();if(!animate||clip==null||clip.Recovery==null||displayed==null)return false;
            track=displayed.IsEntry&&clip.EntryRecovery!=null?clip.EntryRecovery:clip.Recovery;
            first=Array.FindIndex(track,f=>f.File==displayed.File);if(first<0)return false;
            sourceDuration=track.Skip(first).Sum(f=>f.DurationMs);
            Duration=Math.Min(displayed.IsEntry?2800:1600,Math.Max(240,sourceDuration));started=now;Active=true;return true;
        }
        public WorkPose Sample(double now){
            if(!Active)return null;
            double t=Math.Max(0,Math.Min(1,(now-started)/Duration));
            // Monotone traversal of the authored recovery track, easing to idle.
            double elapsed=t*t*(3-2*t)*sourceDuration;int index=first;
            while(index<track.Length-1&&elapsed>=track[index].DurationMs){elapsed-=track[index].DurationMs;index++;}
            if(t>=1)Active=false;
            return new WorkPose{IsEntry=false,Index=index,File=track[index].File};
        }
        public void Cancel(){Active=false;}
    }
}
