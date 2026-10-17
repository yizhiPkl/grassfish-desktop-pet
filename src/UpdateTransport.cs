using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    public interface IUpdateFeed {UpdateOffer Check(Version current,CancellationToken cancel);string Download(UpdateOffer offer,string stage,CancellationToken cancel,Action<int> progress);}
    public sealed class GitHubUpdateFeed:IUpdateFeed {
        // No tokens, cookies, default credentials, arbitrary mirrors or TLS bypass.
        public static long Fetch(string address,Stream target,long limit,CancellationToken cancel,Action<long> progress){
            var deadline=System.Diagnostics.Stopwatch.StartNew();Uri uri=new Uri(address);for(int redirects=0;redirects<=5;redirects++){
                if(!UpdatePolicy.AllowedNetworkUri(uri))throw new InvalidDataException("更新下载地址不受信任");cancel.ThrowIfCancellationRequested();
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                var request=(HttpWebRequest)WebRequest.Create(uri);request.UserAgent="GrassFish-Desktop-Pet/"+AppVersion.Text;request.Accept=uri.DnsSafeHost=="api.github.com"?"application/vnd.github+json":"application/octet-stream";request.UseDefaultCredentials=false;request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
                if(uri.DnsSafeHost=="api.github.com")request.Headers["X-GitHub-Api-Version"]="2022-11-28";
                using(cancel.Register(()=>request.Abort()))using(var response=(HttpWebResponse)request.GetResponse()){
                    int status=(int)response.StatusCode;
                    if(status>=300&&status<400){string location=response.Headers["Location"];if(string.IsNullOrEmpty(location))throw new IOException("更新重定向缺少地址");uri=new Uri(uri,location);continue;}
                    if(status!=200||response.ContentLength>limit)throw new InvalidDataException("更新响应状态或大小不正确");
                    long total=0;byte[] buffer=new byte[65536];using(var input=response.GetResponseStream()){int count;while((count=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();if(deadline.Elapsed>TimeSpan.FromMinutes(15))throw new TimeoutException("更新下载时间过长，请稍后重试");total+=count;if(total>limit)throw new InvalidDataException("更新下载超过大小限制");target.Write(buffer,0,count);if(progress!=null)progress(total);}}return total;
                }
            }throw new IOException("更新下载重定向次数过多");
        }
        static byte[] Bytes(string address,int max,CancellationToken cancel){using(var stream=new MemoryStream()){Fetch(address,stream,max,cancel,null);return stream.ToArray();}}
        public UpdateOffer Check(Version current,CancellationToken cancel){
            object release;
            try{release=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(Encoding.UTF8.GetString(Bytes(UpdatePolicy.LatestApi,1024*1024,cancel)));}
            catch(WebException e){var response=e.Response as HttpWebResponse;if(response!=null&&(int)response.StatusCode==404){response.Dispose();return null;}if(response!=null)response.Dispose();throw;}
            string tag=UpdatePolicy.ReleaseTag(release);if(UpdatePolicy.ParseVersion(tag.Substring(1))<=current)return null;
            UpdatePolicy.Asset(release,tag,UpdatePolicy.ManifestName);UpdatePolicy.Asset(release,tag,UpdatePolicy.SignatureName);
            byte[] manifest=Bytes(UpdatePolicy.AssetUrl(tag,UpdatePolicy.ManifestName),65536,cancel);
            byte[] signature=Convert.FromBase64String(Encoding.ASCII.GetString(Bytes(UpdatePolicy.AssetUrl(tag,UpdatePolicy.SignatureName),2048,cancel)).Trim());
            return UpdatePolicy.FromRelease(release,manifest,signature);
        }
        public string Download(UpdateOffer offer,string stage,CancellationToken cancel,Action<int> progress){
            Directory.CreateDirectory(stage);UpdatePolicy.NoLinks(Path.GetDirectoryName(stage),Path.Combine(stage,"placeholder"));
            string file=Path.Combine(stage,offer.Manifest.installerFile),partial=file+".part";
            try{using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Fetch(offer.InstallerUrl,output,offer.Manifest.size,cancel,n=>progress((int)Math.Min(99,n*100/offer.Manifest.size)));}cancel.ThrowIfCancellationRequested();UpdatePolicy.VerifyInstaller(partial,offer.Manifest);File.Move(partial,file);
                File.WriteAllBytes(Path.Combine(stage,UpdatePolicy.ManifestName),offer.Bytes);File.WriteAllText(Path.Combine(stage,UpdatePolicy.SignatureName),Convert.ToBase64String(offer.Signature));progress(100);return file;
            }finally{if(File.Exists(partial))File.Delete(partial);}
        }
    }
}
