namespace DesktopKitten {
    // Only a fresh, 300ms hover acknowledges a hidden completion note.
    // The completion itself never expires; existing hover at handoff is inert.
    public sealed class CompletionAttention {
        public const double HoverDelayMs=300;
        int sequence=-1;bool inside;double hoverStarted=double.NaN;
        public bool Observe(int current,bool ready,bool pointerInside,bool popupVisible,double now,bool pointerMoved=true){
            if(current!=sequence){sequence=current;inside=pointerInside;hoverStarted=double.NaN;return false;}
            bool entered=pointerInside&&!inside;inside=pointerInside;
            if(!ready||popupVisible||!pointerInside){hoverStarted=double.NaN;return false;}
            if(entered&&pointerMoved)hoverStarted=now;
            if(double.IsNaN(hoverStarted)||now-hoverStarted<HoverDelayMs)return false;
            hoverStarted=double.NaN;return true;
        }
    }
}
