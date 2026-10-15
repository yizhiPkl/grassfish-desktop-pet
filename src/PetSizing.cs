using System.Windows;

namespace DesktopKitten {
    public static class PetSizing {
        public static string Normalize(string preset) { return preset=="medium"||preset=="large"?preset:"small"; }
        public static double Scale(string preset) { return Normalize(preset)=="large"?1:Normalize(preset)=="medium"?.75:.5; }
        public static Size Dimensions(string preset) { double scale=Scale(preset);return new Size(192*scale,208*scale); }
        public static string Label(string preset) { return Normalize(preset)=="large"?"大猫":Normalize(preset)=="medium"?"中猫":"小猫"; }
        public static Rect ResizeKeepingFeet(Rect old,string preset) {
            var size=Dimensions(preset);return new Rect(old.Left+(old.Width-size.Width)/2,old.Bottom-size.Height,size.Width,size.Height);
        }
        public static Rect BubbleBounds(Rect pet,Size bubble,Rect area) {
            double scale=pet.Width/192;
            double left=pet.Left+pet.Width/2+4*scale,top=pet.Top-bubble.Height+46*scale;
            if(left+bubble.Width>area.Right)left=pet.Left-bubble.Width+pet.Width/2-6*scale;
            left=System.Math.Max(area.Left,System.Math.Min(left,area.Right-bubble.Width));
            top=System.Math.Max(area.Top,System.Math.Min(top,area.Bottom-bubble.Height));
            return new Rect(left,top,bubble.Width,bubble.Height);
        }
    }
}
