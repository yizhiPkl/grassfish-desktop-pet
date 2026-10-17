using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    public sealed class UpdateManifest {
        public int schemaVersion;
        public string repository,version,channel,platform,installerFile,sha256,notes;
        public long size;
    }
    public sealed class UpdateOffer {
        public readonly UpdateManifest Manifest;public readonly byte[] Bytes,Signature;public readonly string InstallerUrl;
        internal UpdateOffer(UpdateManifest manifest,byte[] bytes,byte[] signature,string url){Manifest=manifest;Bytes=bytes;Signature=signature;InstallerUrl=url;}
    }
    public static class UpdatePolicy {
        public const string Repository="yizhiPkl/grassfish-desktop-pet";
        public const string LatestApi="https://api.github.com/repos/"+Repository+"/releases/latest";
        public const string ManifestName="grassfish-update.json",SignatureName="grassfish-update.sig";
        public const long MaxInstaller=256L*1024*1024;
        public static Version ParseVersion(string text){
            if(text==null||!Regex.IsMatch(text,@"^[0-9]{1,5}(\.[0-9]{1,5}){1,3}$"))throw new InvalidDataException("版本号格式不受支持");
            Version v;if(!Version.TryParse(text,out v))throw new InvalidDataException("版本号格式不受支持");
            return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
        }
        public static string InstallerName(string version){return "GrassFish-Setup-"+ParseVersion(version).ToString(4)+"-win-x64.exe";}
        public static string AssetUrl(string tag,string name){return "https://github.com/"+Repository+"/releases/download/"+tag+"/"+name;}
        public static string HashFile(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        public static UpdateManifest Verify(byte[] bytes,byte[] signature){return Verify(bytes,signature,UpdateTrust.PublicKeyXml);}
        internal static UpdateManifest Verify(byte[] bytes,byte[] signature,string publicKey){
            if(bytes==null||bytes.Length<2||bytes.Length>65536||signature==null||signature.Length<256||signature.Length>512)throw new InvalidDataException("更新签名或说明大小不正确");
            using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(publicKey);if(!rsa.VerifyData(bytes,CryptoConfig.MapNameToOID("SHA256"),signature))throw new InvalidDataException("更新签名验证失败，未下载或运行安装程序");}
            var m=new JavaScriptSerializer{MaxJsonLength=65536}.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(bytes));
            if(m==null||m.schemaVersion!=1||m.repository!=Repository||m.channel!="stable"||m.platform!="win-x64"||m.size<1024||m.size>MaxInstaller||m.sha256==null||!Regex.IsMatch(m.sha256,"^[a-f0-9]{64}$")||(m.notes??"").Length>8000)throw new InvalidDataException("更新说明不完整或不是草鱼的正式 Windows 版本");
            if(m.installerFile!=InstallerName(m.version)||m.version!=ParseVersion(m.version).ToString(4))throw new InvalidDataException("更新文件名与版本号不符");
            return m;
        }
        internal static object Asset(object release,string tag,string name){
            var matches=JsonValue.Array(JsonValue.Value(release,"assets")).Where(x=>JsonValue.Text(x,"name")==name).ToArray();
            if(matches.Length!=1||JsonValue.Text(matches[0],"browser_download_url")!=AssetUrl(tag,name))throw new InvalidDataException("正式发布的更新文件尚未准备完整");return matches[0];
        }
        public static string ReleaseTag(object release){
            if(!(JsonValue.Value(release,"draft") is bool)||!(JsonValue.Value(release,"prerelease") is bool)||JsonValue.Bool(release,"draft")||JsonValue.Bool(release,"prerelease"))throw new InvalidDataException("不安装草稿或测试发行版");
            string tag=JsonValue.Text(release,"tag_name");if(tag==null||!tag.StartsWith("v",StringComparison.Ordinal))throw new InvalidDataException("正式版本标签格式不受支持");
            if(tag!="v"+ParseVersion(tag.Substring(1)).ToString(4))throw new InvalidDataException("正式版本标签格式不受支持");return tag;
        }
        public static UpdateOffer FromRelease(object release,byte[] bytes,byte[] signature){return FromRelease(release,bytes,signature,UpdateTrust.PublicKeyXml);}
        internal static UpdateOffer FromRelease(object release,byte[] bytes,byte[] signature,string publicKey){
            string tag=ReleaseTag(release);var m=Verify(bytes,signature,publicKey);
            if(tag!="v"+m.version)throw new InvalidDataException("更新说明与发行版本不符");
            var asset=Asset(release,tag,m.installerFile);if(JsonValue.Number(asset,"size")!=m.size)throw new InvalidDataException("更新安装包大小与签名说明不符");
            string digest=JsonValue.Text(asset,"digest");if(!string.IsNullOrEmpty(digest)&&digest!="sha256:"+m.sha256)throw new InvalidDataException("GitHub 文件摘要与签名说明不符");
            return new UpdateOffer(m,bytes,signature,AssetUrl(tag,m.installerFile));
        }
        public static void VerifyInstaller(string path,UpdateManifest m){if(!File.Exists(path)||new FileInfo(path).Length!=m.size||HashFile(path)!=m.sha256)throw new InvalidDataException("安装包校验失败，旧版本未被替换");}
        public static bool AllowedNetworkUri(Uri u){
            if(u==null||u.Scheme!="https"||!u.IsDefaultPort||!string.IsNullOrEmpty(u.UserInfo))return false;
            return new[]{"api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"}.Contains(u.DnsSafeHost.ToLowerInvariant());
        }
        public static string Inside(string root,string path){
            string full=Path.GetFullPath(path),parent=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新路径超出专用目录");return full;
        }
        public static void NoLinks(string root,string path){
            string parent=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),full=Inside(parent,path);
            for(string p=full;p!=null&&p.Length>=parent.Length;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新文件和目录不能是链接");
        }
        public static string InstallBlockReason(int drafts,int pending,bool unsaved){return pending>0?"任务消息仍在发送，请稍后更新。":drafts>0?"任务回复框中还有未发送的草稿。请先处理草稿，草鱼不会强制退出。":unsaved?"小窝中还有未保存的设置或日程编辑，请先保存或清空编辑。":null;}
    }
    public sealed class UpdatePreferences {public bool Automatic=true;public string LastAttemptUtc,SkippedVersion;}
    public sealed class UpdatePreferencesStore {
        readonly string path;public UpdatePreferences Value {get;private set;}
        public UpdatePreferencesStore(string root){path=Path.Combine(root,"update-state.json");Value=new UpdatePreferences();try{if(File.Exists(path)&&new FileInfo(path).Length<=8192)Value=new JavaScriptSerializer().Deserialize<UpdatePreferences>(File.ReadAllText(path))??Value;}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}}
        public bool Due(DateTime now){DateTime last;return Value.Automatic&&(!DateTime.TryParse(Value.LastAttemptUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out last)||last.ToUniversalTime()>now.AddDays(1)||now-last.ToUniversalTime()>=TimeSpan.FromDays(1));}
        public void Save(){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(Value));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
    }
}
