using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    // Official application code is never redistributed. The cache is keyed by
    // a trusted profile, NOT an installed Codex path/version/mtime. All vendor
    // bytes are reverified; old shell/marker files are never executable inputs.
    public static class CodexNativeComponents {
        static readonly object gate=new object();
        public static string Status {get;private set;}
        public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static bool SafeName(string name){return !string.IsNullOrEmpty(name)&&name==Path.GetFileName(name)&&name!="."&&name!=".."&&!name.Contains(":")&&!name.Contains("\\")&&!name.Contains("/")&&name.IndexOfAny(Path.GetInvalidFileNameChars())<0;}
        static bool Regular(string path){return (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
        internal static bool Verified(string path,CodexComponentProfile profile){
            try{
                string vendor=Path.Combine(path,"vendor");if(!Directory.Exists(path)||!Regular(path)||!Directory.Exists(vendor)||!Regular(vendor))return false;
                // A trusted file may import only other pinned files. Reject extra
                // vendor files as well as missing/modified files and junctions.
                if(Directory.GetDirectories(vendor).Length!=0||Directory.GetFiles(vendor).Length!=profile.Files.Length)return false;
                foreach(var item in profile.Files){string file=Path.Combine(vendor,JsonValue.Text(item,"file"));if(!File.Exists(file)||!Regular(file)||new FileInfo(file).Length>24*1024*1024||Hash(File.ReadAllBytes(file))!=JsonValue.Text(item,"sha256"))return false;}
                return true;
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
        public static string Prepare(string shell,string cache){
            string[] archives=CodexWindowActivation.RunningExecutables().Select(exe=>Path.Combine(Path.GetDirectoryName(exe),"resources","app.asar")).ToArray();
            if(archives.Length==0)throw new IOException("Codex 未运行；草鱼和日程提醒仍可独立使用。");
            return PrepareWithArchives(shell,cache,archives);
        }
        internal static string PrepareWithArchives(string shell,string cache,string[] archives){
            lock(gate){
                var profiles=CodexComponentProfile.Load(shell);Directory.CreateDirectory(cache);if(!Regular(cache))throw new IOException("组件缓存目录不能是链接");
                // Prefer the previously mounted profile, but treat its marker as
                // a hint only. Validate every byte against the packaged profile.
                string preferred=null;try{var marker=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(cache,"last-good.json")));string name=JsonValue.Text(marker,"directory");if(SafeName(name))preferred=Path.Combine(cache,name);}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
                var directories=Directory.GetDirectories(cache).Where(p=>!Path.GetFileName(p).StartsWith(".pending-",StringComparison.Ordinal)&&Regular(p)).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(64).ToList();
                if(preferred!=null){directories.Remove(preferred);directories.Insert(0,preferred);}
                foreach(string directory in directories)foreach(var profile in profiles)if(Verified(directory,profile))return Host(directory,shell,profile,"使用已验证的本机官方界面缓存；不依赖当前 Codex 的界面文件名");
                Exception last=null;
                foreach(string archive in archives)foreach(var profile in profiles)try{return ExtractProfile(archive,shell,cache,profile);}catch(InvalidDataException e){last=e;}catch(IOException e){last=e;}catch(UnauthorizedAccessException e){last=e;}catch(ArgumentException e){last=e;}catch(InvalidOperationException e){last=e;}
                throw new IOException("当前 Codex 的任务界面尚未适配，且本机没有完整可用的已验证缓存。请更新草鱼；日程提醒不受影响。已适配：26.1007.9446.0 / 26.1013.3656.0。",last);
            }
        }
        static string Host(string directory,string shell,CodexComponentProfile profile,string status){
            // Only our packaged shell is used. This also safely migrates 0.1.8.1
            // caches without trusting their old overlay.js or provenance files.
            foreach(string name in new[]{"index.html","overlay.js","overlay.css"})WriteHostFile(directory,name,File.ReadAllBytes(Path.Combine(shell,name)));
            WriteHostFile(directory,"compat.js",Encoding.UTF8.GetBytes(profile.BridgeScript()));WriteHostFile(directory,"official.css",Encoding.UTF8.GetBytes(profile.StyleSheet()));
            Status=status+"（"+profile.Source+"）";return directory;
        }
        static void WriteHostFile(string directory,string name,byte[] bytes){string file=Path.Combine(directory,name);if(File.Exists(file)&&!Regular(file))throw new IOException("组件宿主文件不能是链接");File.WriteAllBytes(file,bytes);}
        public static void MarkReady(string directory){
            // Only a known profile which passed both integrity checks and an
            // actual renderer mount can become the preferred cache.
            lock(gate){
                string root=Path.GetDirectoryName(directory);if(!Regular(directory)||!Regular(root))return;
                string file=Path.Combine(root,"last-good.json");if(File.Exists(file)&&!Regular(file))return;
                string temp=Path.Combine(root,"last-good-"+Guid.NewGuid().ToString("N")+".tmp");
                try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new{directory=Path.GetFileName(directory)}));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}catch(IOException){}catch(UnauthorizedAccessException){}finally{if(File.Exists(temp))File.Delete(temp);}
            }
        }
        // Kept for diagnostics/fixtures. Production extraction always uses a
        // running, verified Codex executable; it never searches arbitrary disks.
        public static string Extract(string archive,string shell,string cache){lock(gate){Exception last=null;foreach(var p in CodexComponentProfile.Load(shell))try{return ExtractProfile(archive,shell,cache,p);}catch(InvalidDataException e){last=e;}catch(IOException e){last=e;}throw new IOException("Codex 组件未通过已知版本校验",last);}}
        static string ExtractProfile(string archive,string shell,string cache,CodexComponentProfile profile){
            var json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};
            Directory.CreateDirectory(cache);if(!Regular(cache))throw new IOException("组件缓存目录不能是链接");
            string target=Path.Combine(cache,profile.Key.Substring(0,24));if(Verified(target,profile))return Host(target,shell,profile,"使用已验证的本机官方界面缓存");
            // Stream into a private staging directory instead of keeping all
            // 24 MB of vendor modules in memory. Promote only a complete verified
            // dependency closure. A failed build never touches a usable cache.
            string stage=Path.Combine(cache,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(stage,"vendor"));
            try{
            using(var stream=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(stream)){
                if(stream.Length<16)throw new InvalidDataException("Codex 资源包不完整");uint marker=reader.ReadUInt32(),header=reader.ReadUInt32();reader.ReadUInt32();uint length=reader.ReadUInt32();
                if(marker!=4||header<8||header>32*1024*1024||length==0||length>header||16L+length>stream.Length)throw new InvalidDataException("不支持的 Codex 资源包");
                var tree=json.DeserializeObject(Encoding.UTF8.GetString(reader.ReadBytes((int)length)));long origin=8L+header;
                var assets=JsonValue.Value(JsonValue.Value(JsonValue.Value(JsonValue.Value(JsonValue.Value(tree,"files"),"webview"),"files"),"assets"),"files");long total=0;
                foreach(var item in profile.Files){string name=JsonValue.Text(item,"file");var entry=JsonValue.Value(assets,name);long offset=JsonValue.Number(entry,"offset"),size=JsonValue.Number(entry,"size");total+=size;
                    if(entry==null||offset<0||size<1||size>24*1024*1024||total>96*1024*1024||origin>stream.Length||offset>stream.Length-origin||size>stream.Length-origin-offset||JsonValue.Bool(entry,"unpacked"))throw new InvalidDataException("Codex 组件版本不匹配："+name);
                    stream.Position=origin+offset;var bytes=reader.ReadBytes((int)size);string upstream=JsonValue.Text(item,"upstreamSha256")??JsonValue.Text(item,"sha256");if(Hash(bytes)!=upstream)throw new InvalidDataException("Codex 组件校验未通过："+name);
                    bytes=profile.Adapt(name,bytes);if(Hash(bytes)!=JsonValue.Text(item,"sha256"))throw new InvalidDataException("Codex 组件适配校验未通过："+name);File.WriteAllBytes(Path.Combine(stage,"vendor",name),bytes);
                }
            }
            if(Directory.Exists(target)||File.Exists(target))target=Path.Combine(cache,profile.Key.Substring(0,24)+"-"+Guid.NewGuid().ToString("N"));
            Directory.Move(stage,target);
            return Host(target,shell,profile,"已从本机 Codex 校验并准备官方任务界面");
            }finally{CleanStage(stage,cache);}
        }
        static void CleanStage(string stage,string cache){
            // No recursive deletion, no computed path outside the cache, and no
            // following junctions. Only our flat, randomly named staging tree.
            try{
                string root=Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,path=Path.GetFullPath(stage);
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(path).StartsWith(".pending-",StringComparison.Ordinal)||!Directory.Exists(path)||!Regular(path))return;
                string vendor=Path.Combine(path,"vendor");if(Directory.Exists(vendor)&&Regular(vendor)){foreach(string file in Directory.GetFiles(vendor))if(Regular(file))File.Delete(file);Directory.Delete(vendor,false);}Directory.Delete(path,false);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
