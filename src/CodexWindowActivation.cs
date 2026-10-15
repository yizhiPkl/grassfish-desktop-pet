using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
namespace DesktopKitten {
    // Only raises an existing verified Codex desktop window. No protocol launch, backend, or new process.
    public static class CodexWindowActivation {
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
        public static bool IsCodexDesktop(string path){
            if(string.IsNullOrEmpty(path))return false;
            path=path.Replace('/',Path.DirectorySeparatorChar);
            // MSIX packages can be installed on another drive, not only C:.
            return System.Text.RegularExpressions.Regex.IsMatch(path,@"^[A-Za-z]:\\(?:Program Files\\)?WindowsApps\\OpenAI\.Codex_[0-9.]+_(?:x64|arm64)__2p2nqsd0c76g0\\app\\ChatGPT\.exe$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public static string[] RunningExecutables(){
            var result=new System.Collections.Generic.List<string>();foreach(var p in Process.GetProcessesByName("ChatGPT"))try{string path=p.MainModule.FileName;if(IsCodexDesktop(path)&&!result.Contains(path))result.Add(path);}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}finally{p.Dispose();}return result.ToArray();
        }
        public static void ActivateExisting(){
            var windows=Process.GetProcessesByName("ChatGPT").Select(p=>{
                try{return new{Handle=p.MainWindowHandle,Valid=IsCodexDesktop(p.MainModule.FileName)};}
                catch(System.ComponentModel.Win32Exception){return new{Handle=IntPtr.Zero,Valid=false};}
                catch(InvalidOperationException){return new{Handle=IntPtr.Zero,Valid=false};}
                finally{p.Dispose();}
            }).Where(p=>p.Valid&&p.Handle!=IntPtr.Zero).Select(p=>p.Handle).Distinct().ToArray();
            IntPtr foreground=GetForegroundWindow();
            if(windows.Contains(foreground))return;
            if(windows.Length!=1)throw new InvalidOperationException("已切换对话，但未能确定唯一 Codex 窗口，请手动打开 Codex。");
            if(IsIconic(windows[0]))ShowWindow(windows[0],9);
            if(!SetForegroundWindow(windows[0])&&GetForegroundWindow()!=windows[0])throw new InvalidOperationException("已切换对话，但系统没有允许窗口置前，请手动打开 Codex。");
        }
        public static bool IsRunning(){
            return RunningExecutables().Length>0;
        }
    }
}
