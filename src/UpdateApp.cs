using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
namespace DesktopKitten {
    public sealed partial class KittenApp {
        public UpdateManager Updates {get;private set;}
        public bool UpdatePreparing {get;private set;}
        UpdateWindow updateWindow;
        void InitializeUpdates(){
            Updates=new UpdateManager(Store.DirectoryPath,!Qa,action=>{if(!quitting)Dispatcher.BeginInvoke(action);});
            Updates.Changed+=()=>{if(!quitting)RefreshPanel();};
            Updates.OfferFound+=manual=>{if(!quitting)ShowUpdateWindow(manual);};
        }
        public async void CheckUpdates(bool manual){try{await Updates.Check(manual);}catch(Exception){if(manual&&!Qa)MessageBox.Show("检查更新暂未完成，草鱼和日程照常使用。","草鱼的小更新",MessageBoxButton.OK,MessageBoxImage.Information);}}
        public void ShowUpdateWindow(bool manual){
            if(updateWindow==null){updateWindow=new UpdateWindow(Updates,ApplyUpdate,Art.Portrait);updateWindow.Closed+=(s,e)=>updateWindow=null;}
            if(Qa)return;updateWindow.ShowActivated=manual;if(!updateWindow.IsVisible)updateWindow.Show();if(manual)updateWindow.Activate();
        }
        async Task ApplyUpdate(){
            if(Qa){if(updateWindow!=null)updateWindow.Notice("隔离检查不会退出或替换本机草鱼。");return;}
            if(UpdatePreparing)return;
            bool helperStarted=false;try{
                if(Updates.Stage==null&&!await Updates.Download())return;
                if(updateWindow==null||quitting)return;
                string block=UpdatePolicy.InstallBlockReason(Badge.DraftCount,Badge.PendingSendCount,panel!=null&&panel.HasUnsavedLocalEdits);
                if(block!=null){updateWindow.Notice(block);return;}
                UpdatePreparing=true;updateWindow.Preparing=true;updateWindow.Notice("正在安全收好任务界面，随后更新并重启…");Badge.ReleaseImmediately();
                if(!await Badge.WaitReleased(3000))throw new IOException("任务界面还未收好，请稍后重试。草鱼没有退出。");
                block=UpdatePolicy.InstallBlockReason(Badge.DraftCount,Badge.PendingSendCount,panel!=null&&panel.HasUnsavedLocalEdits);
                if(block!=null){updateWindow.Notice(block);return;}
                if(!await UpdateHelper.Launch(Exe,Updates.Stage))throw new IOException("更新辅助程序未就绪，草鱼没有退出，请重试或手动安装。");helperStarted=true;
                block=UpdatePolicy.InstallBlockReason(Badge.DraftCount,Badge.PendingSendCount,panel!=null&&panel.HasUnsavedLocalEdits);
                if(block!=null){UpdateHelper.CancelLaunch(Updates.Stage);updateWindow.Notice(block);return;}
                updateWindow.Preparing=false;Quit();
            }catch(Exception e){if(updateWindow!=null)updateWindow.Notice(e is IOException||e is InvalidDataException?e.Message:"更新暂未开始，当前草鱼没有被替换。请稍后重试。");}
            finally{if(!quitting){if(helperStarted)try{UpdateHelper.CancelLaunch(Updates.Stage);}catch(Exception){/* Cancellation is best-effort; a live parent still prevents installation. */}UpdatePreparing=false;if(updateWindow!=null)updateWindow.Preparing=false;}}
        }
    }
}
