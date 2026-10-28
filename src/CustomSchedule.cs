using System;
using System.Linq;
namespace DesktopKitten {
    public sealed class ScheduleRule {
        public string Id=Guid.NewGuid().ToString("N"),Title="",Message="",Mode="daily",Time="09:00";
        public bool Enabled=true,Completed;
        // Persist the existing field for backwards compatibility. Important
        // schedules now also preempt other notes, not just bypass quiet hours.
        public bool AllowDuringQuiet;
        public int Minutes=60;public int[] Days=new[]{1,2,3,4,5};
        public DateTime DueLocal=DateTime.MinValue,NextUtc=DateTime.MinValue,LastFiredLocal=DateTime.MinValue;
        public void Validate(){
            Guid id;if(!Guid.TryParse(Id,out id)||string.IsNullOrWhiteSpace(Title)||Title.Length>60||(Message??"").Length>500)throw new FormatException("标题请填写1～60字，内容最多500字。");
            if(!new[]{"daily","weekly","once","interval"}.Contains(Mode))throw new FormatException("提醒重复方式无效。");
            if(Mode=="daily"||Mode=="weekly")if(Store.ParseTimes(Time).Count!=1)throw new FormatException("请填写一个提醒时间，例如09:00。");
            if(Mode=="weekly"&&(Days==null||Days.Length==0||Days.Any(n=>n<0||n>6)))throw new FormatException("每周提醒请至少选择一天。");
            if(Mode=="interval"&&(Minutes<1||Minutes>10080))throw new FormatException("间隔请填写1～10080分钟。");
            if(Mode=="once"&&DueLocal==DateTime.MinValue)throw new FormatException("一次性提醒请选择日期和时间。");
        }
        public string Description {get{if(Mode=="interval")return "每 "+Minutes+" 分钟";if(Mode=="once")return DueLocal.ToString("MM月dd日 HH:mm")+(Completed?" · 已完成":"");if(Mode=="weekly")return string.Join("、",(Days??new int[0]).Where(n=>n>=0&&n<=6).Distinct().OrderBy(n=>n==0?7:n).Select(n=>new[]{"周日","周一","周二","周三","周四","周五","周六"}[n]))+" "+Time;return "每天 "+Time;}}
        public ScheduleRule Copy(){return new ScheduleRule{Id=Id,Title=Title,Message=Message,Mode=Mode,Time=Time,Enabled=Enabled,Completed=Completed,AllowDuringQuiet=AllowDuringQuiet,Minutes=Minutes,Days=Days==null?new int[0]:(int[])Days.Clone(),DueLocal=DueLocal,NextUtc=NextUtc,LastFiredLocal=LastFiredLocal};}
    }
}
