using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
namespace DesktopKitten {
    public sealed class UpdateTicket {public string installDirectory,launchId;public int parentId;public long parentStartUtcTicks;}
    internal sealed class UpdatePlan {internal string Stage,Target,Installer;internal UpdateManifest Manifest;internal UpdateTicket Ticket;}
    public static class UpdateHelper {
        const string TicketName="update-ticket.json",HelperName="GrassFishUpdateHelper.exe";
        const string Registration=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5B2C4677-AF7F-4F5E-A1A5-FF7146CD6253}_is1";
        static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=65536};}
        public static string RuntimeRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopKitten","updates");}}
        static byte[] Small(string file,int max){if(!File.Exists(file)||new FileInfo(file).Length>max)throw new InvalidDataException("更新凭据不完整");return File.ReadAllBytes(file);}
        internal static UpdatePlan Validate(string ticketPath,string root,bool checkParent){
            ticketPath=UpdatePolicy.Inside(root,ticketPath);string stage=Path.GetDirectoryName(ticketPath);
            if(Path.GetFileName(ticketPath)!=TicketName||!string.Equals(Path.GetDirectoryName(stage).TrimEnd('\\'),Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(stage),"^[a-f0-9]{32}$"))throw new InvalidDataException("更新票据目录不正确");
            UpdatePolicy.NoLinks(root,ticketPath);
            UpdatePolicy.NoLinks(root,Path.Combine(stage,UpdatePolicy.ManifestName));UpdatePolicy.NoLinks(root,Path.Combine(stage,UpdatePolicy.SignatureName));
            var ticket=Json().Deserialize<UpdateTicket>(System.Text.Encoding.UTF8.GetString(Small(ticketPath,8192)));
            var manifest=UpdatePolicy.Verify(Small(Path.Combine(stage,UpdatePolicy.ManifestName),65536),Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(Small(Path.Combine(stage,UpdatePolicy.SignatureName),2048)).Trim()));
            if(UpdatePolicy.ParseVersion(manifest.version)<=AppVersion.Current)throw new InvalidDataException("不允许降级或重复安装旧版本");
            if(ticket==null||ticket.parentId<=0||ticket.parentStartUtcTicks<=0||string.IsNullOrWhiteSpace(ticket.installDirectory)||ticket.launchId==null||!System.Text.RegularExpressions.Regex.IsMatch(ticket.launchId,"^[a-f0-9]{32}$"))throw new InvalidDataException("更新票据缺少父进程或本次启动凭据");
            string target=Path.GetFullPath(ticket.installDirectory).TrimEnd('\\');if(target.StartsWith(@"\\",StringComparison.Ordinal)||target.Length<4||target==Path.GetPathRoot(target).TrimEnd('\\'))throw new InvalidDataException("更新安装目录不受支持");
            string exe=Path.Combine(target,"DesktopKitten.exe");if(!File.Exists(exe)||!File.Exists(Path.Combine(target,"assets","cat.ico")))throw new InvalidDataException("更新目标不是完整草鱼目录");
            UpdatePolicy.NoLinks(target,exe);
            if(FileVersionInfo.GetVersionInfo(exe).ProductName!="DesktopKitten")throw new InvalidDataException("更新目标不是草鱼程序");
            if(checkParent){using(var parent=Process.GetProcessById(ticket.parentId)){if(!string.Equals(Path.GetFullPath(parent.MainModule.FileName),exe,StringComparison.OrdinalIgnoreCase)||parent.StartTime.ToUniversalTime().Ticks!=ticket.parentStartUtcTicks)throw new InvalidDataException("更新父进程与安装目录不符");}}
            using(var key=Registry.CurrentUser.OpenSubKey(Registration))if(key!=null){string registered=key.GetValue("InstallLocation") as string;if(!string.IsNullOrEmpty(registered)&&!string.Equals(Path.GetFullPath(registered).TrimEnd('\\'),target,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("当前便携目录与已登记安装位置不同，请手动安装更新，以免覆盖另一份草鱼。");}
            string installer=Path.Combine(stage,manifest.installerFile);UpdatePolicy.NoLinks(root,installer);UpdatePolicy.VerifyInstaller(installer,manifest);
            return new UpdatePlan{Stage=stage,Target=target,Installer=installer,Manifest=manifest,Ticket=ticket};
        }
        public static async Task<bool> Launch(string exe,string stage){
            UpdatePolicy.NoLinks(RuntimeRoot,Path.Combine(stage,TicketName));
            // A transient copy only; no update daemon and no new browser/backend.
            string helper=Path.Combine(stage,HelperName);UpdatePolicy.NoLinks(RuntimeRoot,helper);File.Copy(exe,helper,true);
            foreach(string name in new[]{"Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.Wpf.dll","WebView2Loader.dll"}){UpdatePolicy.NoLinks(RuntimeRoot,Path.Combine(stage,name));File.Copy(Path.Combine(Path.GetDirectoryName(exe),name),Path.Combine(stage,name),true);}
            UpdatePolicy.NoLinks(RuntimeRoot,helper+".config");
            File.Copy(exe+".config",helper+".config",true);
            string launchId=Guid.NewGuid().ToString("N");using(var self=Process.GetCurrentProcess())File.WriteAllText(Path.Combine(stage,TicketName),Json().Serialize(new UpdateTicket{installDirectory=Path.GetDirectoryName(exe),parentId=self.Id,parentStartUtcTicks=self.StartTime.ToUniversalTime().Ticks,launchId=launchId}));
            string ready=Path.Combine(stage,"helper-ready-"+launchId),cancel=Path.Combine(stage,"cancel-update-"+launchId);
            var process=Process.Start(new ProcessStartInfo(helper,"--apply-update "+Quote(Path.Combine(stage,TicketName))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=stage});
            try{for(int n=0;n<100;n++){await Task.Delay(100);if(process.HasExited)break;if(File.Exists(ready))return true;}File.WriteAllText(cancel,"host-kept-running");return false;}finally{process.Dispose();}
        }
        public static void CancelLaunch(string stage){if(stage!=null){UpdatePolicy.NoLinks(stage,Path.Combine(stage,TicketName));var ticket=Json().Deserialize<UpdateTicket>(System.Text.Encoding.UTF8.GetString(Small(Path.Combine(stage,TicketName),8192)));if(ticket!=null&&ticket.launchId!=null&&System.Text.RegularExpressions.Regex.IsMatch(ticket.launchId,"^[a-f0-9]{32}$")){string cancel=Path.Combine(stage,"cancel-update-"+ticket.launchId);UpdatePolicy.NoLinks(stage,cancel);File.WriteAllText(cancel,"user-state-changed");}}}
        static string Quote(string path){if(path.IndexOf('"')>=0||path.IndexOf('\r')>=0||path.IndexOf('\n')>=0)throw new InvalidDataException("更新路径无效");return "\""+path+"\"";}
        internal static string[] OwnedFiles(string target){
            var names=new List<string>();
            foreach(string name in new[]{"DesktopKitten.exe","DesktopKitten.exe.config","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.Wpf.dll","WebView2Loader.dll","SHA256SUMS.txt","使用说明.md","发布与兼容性.md","安装与Codex兼容说明.txt","版本信息.json","安装说明与源码位置.txt","unins000.exe","unins000.dat"})if(File.Exists(Path.Combine(target,name)))names.Add(name);
            var pending=new Stack<string>();foreach(string name in new[]{"assets","third-party"})if(Directory.Exists(Path.Combine(target,name)))pending.Push(Path.Combine(target,name));
            long bytes=names.Sum(name=>new FileInfo(Path.Combine(target,name)).Length);if(bytes>128L*1024*1024)throw new IOException("草鱼应用文件过大，请手动更新");int folders=0;while(pending.Count>0){string folder=pending.Pop();if(++folders>4096)throw new IOException("草鱼目录中的自定义目录过多，请手动更新");UpdatePolicy.NoLinks(target,folder);foreach(string file in Directory.GetFiles(folder)){UpdatePolicy.NoLinks(target,file);bytes+=new FileInfo(file).Length;if(names.Count>=4096||bytes>128L*1024*1024)throw new IOException("草鱼目录中的自定义资源过多，请手动更新");names.Add(file.Substring(Path.GetFullPath(target).TrimEnd('\\').Length+1));}foreach(string child in Directory.GetDirectories(folder))pending.Push(child);}
            return names.ToArray();
        }
        internal static string Backup(string target,string stage){
            string backup=Path.Combine(stage,"backup-app");if(Directory.Exists(backup))throw new IOException("更新备份已存在，不能覆盖");Directory.CreateDirectory(backup);
            foreach(string relative in OwnedFiles(target)){string source=Path.Combine(target,relative);UpdatePolicy.NoLinks(target,source);string dest=UpdatePolicy.Inside(backup,Path.Combine(backup,relative));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(source,dest);}
            File.WriteAllText(Path.Combine(stage,"backup-complete"),"v1");return backup;
        }
        internal static void Restore(string target,string backup){
            var files=UpdateCache.Files(backup,4096,128L*1024*1024);
            foreach(string file in files)UpdatePolicy.NoLinks(target,UpdatePolicy.Inside(target,Path.Combine(target,file.Substring(backup.Length+1))));
            foreach(string file in files){string destination=UpdatePolicy.Inside(target,Path.Combine(target,file.Substring(backup.Length+1)));UpdatePolicy.NoLinks(target,destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(file,destination,true);}
        }
        static void Result(string stage,string phase,string message){File.WriteAllText(Path.Combine(stage,"finished.json"),Json().Serialize(new{phase,message,atUtc=DateTime.UtcNow.ToString("o")}));}
        internal static bool InstallCore(UpdatePlan plan,Func<int> install,Func<bool> installedVersionMatches,Action restart,bool registration){
            string backup=Backup(plan.Target,plan.Stage);string oldDisplay=null,oldVersion=null;
            if(registration)using(var key=Registry.CurrentUser.OpenSubKey(Registration)){if(key!=null){oldDisplay=key.GetValue("DisplayName") as string;oldVersion=key.GetValue("DisplayVersion") as string;}}
            try{
                int exit=install();if(exit!=0&&exit!=3010)throw new IOException("安装程序未完成（"+exit+"）");if(!installedVersionMatches())throw new IOException("安装后的程序版本与更新说明不符");
                Result(plan.Stage,"installed","新版已安装，旧版文件备份保留在本目录。日程设置未被安装器覆盖。");
            }catch(Exception failure){
                try{Restore(plan.Target,backup);
                    if(registration)using(var key=Registry.CurrentUser.OpenSubKey(Registration,true))if(key!=null){if(oldDisplay!=null)key.SetValue("DisplayName",oldDisplay);if(oldVersion!=null)key.SetValue("DisplayVersion",oldVersion);}
                    Result(plan.Stage,"restored","更新未完成，已恢复备份的旧版文件："+failure.GetType().Name);
                }catch(Exception){Result(plan.Stage,"restore-failed","更新未完成，自动恢复也未完成。旧文件备份仍在 backup-app，请手动重新安装。不可视为完整事务回滚。");throw;}
                restart();return false;
            }
            restart();return true;
        }
        public static int Run(string ticketPath){
            string stage=null;UpdatePlan plan=null;bool parentExited=false,installEntered=false;try{
                plan=Validate(ticketPath,RuntimeRoot,true);stage=plan.Stage;
                string helper=System.Reflection.Assembly.GetExecutingAssembly().Location;
                if(!string.Equals(Path.GetFullPath(helper),Path.Combine(stage,HelperName),StringComparison.OrdinalIgnoreCase)||UpdatePolicy.HashFile(helper)!=UpdatePolicy.HashFile(Path.Combine(plan.Target,"DesktopKitten.exe")))throw new InvalidDataException("更新辅助程序与当前草鱼不符");
                using(var parent=Process.GetProcessById(plan.Ticket.parentId)){
                    File.WriteAllText(Path.Combine(stage,"helper-ready-"+plan.Ticket.launchId),"verified");
                    var clock=Stopwatch.StartNew();while(!parent.WaitForExit(250)){if(File.Exists(Path.Combine(stage,"cancel-update-"+plan.Ticket.launchId))){Result(stage,"cancelled","本次更新已取消，原版仍在运行。");return 2;}if(clock.Elapsed.TotalSeconds>=60)throw new IOException("旧版没有正常退出，取消安装");}
                }
                parentExited=true;if(File.Exists(Path.Combine(stage,"cancel-update-"+plan.Ticket.launchId))){Result(stage,"cancelled","更新已取消，原版保持不变。");Restart(plan.Target);return 2;}
                UpdatePolicy.VerifyInstaller(plan.Installer,plan.Manifest);
                using(var locked=new FileStream(plan.Installer,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    bool ok=InstallCore(plan,()=>{
                        installEntered=true;
                        var args="/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /TASKS=\"\" /DIR="+Quote(plan.Target)+" /LOG="+Quote(Path.Combine(stage,"installer.log"));
                        using(var install=Process.Start(new ProcessStartInfo(plan.Installer,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=stage})){install.WaitForExit();return install.ExitCode;}
                    },()=>FileVersionInfo.GetVersionInfo(Path.Combine(plan.Target,"DesktopKitten.exe")).FileVersion==plan.Manifest.version,()=>Restart(plan.Target),true);
                    return ok?0:1;
                }
            }catch(Exception e){if(stage!=null)try{if(!File.Exists(Path.Combine(stage,"finished.json")))Result(stage,"failed",e.GetType().Name+"：更新未完成，未强制结束任何程序。");}catch(IOException){}catch(UnauthorizedAccessException){}if(parentExited&&!installEntered&&plan!=null)try{Restart(plan.Target);}catch(Exception){}return 1;}
        }
        public static int Probe(string output){try{var core=typeof(Microsoft.Web.WebView2.Core.CoreWebView2);var wpf=typeof(Microsoft.Web.WebView2.Wpf.WebView2CompositionControl);File.WriteAllText(output,Json().Serialize(new{ok=core!=null&&wpf!=null,version=AppVersion.Text,probeOnly=true,installed=false,registryModified=false}));return 0;}catch(Exception){return 1;}}
        static void Restart(string target){Process.Start(new ProcessStartInfo(Path.Combine(target,"DesktopKitten.exe"),"--background"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=target});}
    }
}
