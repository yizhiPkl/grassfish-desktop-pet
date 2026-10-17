using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace DesktopKitten {
    public sealed class UpdateManager:IDisposable {
        readonly IUpdateFeed feed;readonly bool networkAllowed;readonly Action<Action> dispatch;
        readonly UpdatePreferencesStore preferences;readonly string root;CancellationTokenSource operation;int busy;bool disposed;
        public event Action Changed;public event Action<bool> OfferFound;
        public UpdateOffer Offer {get;private set;}public string Stage {get;private set;}
        public string Status {get;private set;}public string Phase {get;private set;}public int Progress {get;private set;}
        public bool Busy {get{return Interlocked.CompareExchange(ref busy,0,0)!=0;}}
        public bool Automatic {get{return preferences.Value.Automatic;}}
        public UpdateManager(string data,bool networkAllowed,Action<Action> dispatch):this(data,networkAllowed,dispatch,new GitHubUpdateFeed()){}
        internal UpdateManager(string data,bool networkAllowed,Action<Action> dispatch,IUpdateFeed feed){this.feed=feed;this.networkAllowed=networkAllowed;this.dispatch=dispatch;preferences=new UpdatePreferencesStore(data);root=Path.Combine(data,"updates");Phase="idle";Status=UpdateCache.LatestResult(root)??"可检查草鱼的新版本";}
        void Notify(){if(!disposed)dispatch(()=>{if(!disposed){var cb=Changed;if(cb!=null)cb();}});}
        void Save(){try{preferences.Save();}catch(IOException){Status="更新偏好暂时无法保存，其他功能不受影响。";}catch(UnauthorizedAccessException){Status="更新偏好暂时无法保存，其他功能不受影响。";}}
        public void SetAutomatic(bool value){preferences.Value.Automatic=value;Save();Notify();}
        public void Skip(){if(Offer!=null)preferences.Value.SkippedVersion=Offer.Manifest.version;Save();Status="已跳过这个版本，仍可手动检查。";Notify();}
        public void Cancel(){var current=operation;if(current!=null)try{current.Cancel();}catch(ObjectDisposedException){}}
        public async Task Check(bool manual){
            if(disposed||(!manual&&!preferences.Due(DateTime.UtcNow))||Interlocked.CompareExchange(ref busy,1,0)!=0)return;
            if(!networkAllowed){Interlocked.Exchange(ref busy,0);Status="隔离测试：未联网检查更新";Notify();return;}
            var cancel=new CancellationTokenSource();operation=cancel;Phase="checking";Status="正在检查草鱼的新版本…";Notify();bool found=false;
            try{
                preferences.Value.LastAttemptUtc=DateTime.UtcNow.ToString("o");Save();
                UpdateCache.Prune(root,DateTime.UtcNow);
                var value=await Task.Run(()=>feed.Check(AppVersion.Current,cancel.Token)).ConfigureAwait(false);if(disposed)return;
                Offer=value;Stage=null;Phase=value==null?"idle":"available";Status=value==null?"当前没有比本机更新的正式版本。":"发现草鱼 "+value.Manifest.version+"，可以查看更新。";
                found=value!=null&&(manual||(preferences.Value.Automatic&&preferences.Value.SkippedVersion!=value.Manifest.version));
            }catch(Exception e){if(!Expected(e))throw;Phase="failed";Status=cancel.IsCancellationRequested?"已取消检查，草鱼照常陪你。":"暂时无法检查更新。请稍后重试，日程提醒不受影响。";}
            finally{operation=null;cancel.Dispose();Interlocked.Exchange(ref busy,0);Notify();}
            if(found&&!disposed)dispatch(()=>{if(!disposed){var cb=OfferFound;if(cb!=null)cb(manual);}});
        }
        public async Task<bool> Download(){
            if(disposed||Offer==null||!networkAllowed||Interlocked.CompareExchange(ref busy,1,0)!=0)return false;
            var offer=Offer;var cancel=new CancellationTokenSource();operation=cancel;Phase="downloading";Progress=0;Status="正在下载并验证更新…";Notify();string stage=null;
            try{
                UpdatePolicy.NoLinks(Path.GetDirectoryName(root),Path.Combine(root,"placeholder"));Directory.CreateDirectory(root);stage=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);UpdatePolicy.NoLinks(root,Path.Combine(stage,"placeholder"));File.WriteAllText(Path.Combine(stage,".grassfish-update"),"v1");
                await Task.Run(()=>feed.Download(offer,stage,cancel.Token,n=>{if(Progress!=n){Progress=n;Notify();}})).ConfigureAwait(false);
                if(disposed||cancel.IsCancellationRequested)throw new OperationCanceledException();
                // Recheck even if a future feed implementation changes its checks.
                UpdatePolicy.VerifyInstaller(Path.Combine(stage,offer.Manifest.installerFile),offer.Manifest);
                Stage=stage;Phase="ready";Status="更新已下载并通过签名、大小和文件校验。";return true;
            }catch(Exception e){if(!Expected(e))throw;Phase="failed";Status=cancel.IsCancellationRequested?"下载已取消，旧版本没有改变。":"更新下载或校验失败，旧版本没有改变。请稍后重试。";if(stage!=null)try{File.WriteAllText(Path.Combine(stage,"finished.json"),"{\"phase\":\"download-failed\"}");}catch(IOException){}catch(UnauthorizedAccessException){}return false;}
            finally{operation=null;cancel.Dispose();Interlocked.Exchange(ref busy,0);Notify();}
        }
        static bool Expected(Exception e){return e is IOException||e is InvalidDataException||e is WebException||e is TimeoutException||e is FormatException||e is OperationCanceledException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException||e is System.Security.Cryptography.CryptographicException||e is System.Security.SecurityException;}
        public void Dispose(){disposed=true;Cancel();}
    }
}
