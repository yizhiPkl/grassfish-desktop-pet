using System;
using System.Reflection;
namespace DesktopKitten {
    public static class AppVersion {
        public static readonly Version Current=Version.Parse(((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(typeof(AppVersion).Assembly,typeof(AssemblyFileVersionAttribute))).Version);
        public static string Text {get{return Current.ToString(4);}}
        public static string AboutText {get{return "版本 "+Text+" · Windows 本地桌面伙伴";}}
    }
}
