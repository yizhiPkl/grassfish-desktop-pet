using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
namespace DesktopKitten {
    public sealed class PopupPose {public Rect Bounds;public string Side;public Point Anchor;}
    public static class PopupPlacement {
        public static PopupPose Place(Rect cat,Size requested,Rect area){
            double margin=10,gap=16;var safe=new Rect(area.Left+margin,area.Top+margin,Math.Max(1,area.Width-margin*2),Math.Max(1,area.Height-margin*2));
            double w=Math.Min(requested.Width,safe.Width),h=Math.Min(requested.Height,safe.Height),cx=cat.Left+cat.Width/2,cy=cat.Top+cat.Height*.36;
            var candidates=new[]{
                new PopupPose{Bounds=new Rect(cx-w/2,cat.Top-gap-h,w,h),Side="bottom",Anchor=new Point(cx,cat.Top)},
                new PopupPose{Bounds=new Rect(cat.Right+gap,cy-h/2,w,h),Side="left",Anchor=new Point(cat.Right,cy)},
                new PopupPose{Bounds=new Rect(cat.Left-gap-w,cy-h/2,w,h),Side="right",Anchor=new Point(cat.Left,cy)},
                new PopupPose{Bounds=new Rect(cx-w/2,cat.Bottom+gap,w,h),Side="top",Anchor=new Point(cx,cat.Bottom)}
            };
            var clear=candidates.FirstOrDefault(p=>safe.Contains(p.Bounds));if(clear!=null)return clear;
            foreach(var p in candidates){var b=p.Bounds;p.Bounds=new Rect(Math.Max(safe.Left,Math.Min(b.Left,safe.Right-w)),Math.Max(safe.Top,Math.Min(b.Top,safe.Bottom-h)),w,h);}
            return candidates.OrderBy(p=>Overlap(p.Bounds,cat)*1000+Distance(p.Bounds,p.Anchor)).First();
        }
        static double Overlap(Rect a,Rect b){var r=Rect.Intersect(a,b);return r.IsEmpty?0:r.Width*r.Height;}
        static double Distance(Rect r,Point p){return Math.Max(0,Math.Max(r.Left-p.X,p.X-r.Right))+Math.Max(0,Math.Max(r.Top-p.Y,p.Y-r.Bottom));}
        public static string Badge(int count){return count<=0?"·":count<=20?char.ConvertFromUtf32(0x2460+count-1):count>99?"99+":count.ToString();}
    }
}
