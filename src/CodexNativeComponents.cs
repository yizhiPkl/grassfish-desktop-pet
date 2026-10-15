using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    // Official application code is NOT redistributed in the public installer.
    // Build a per-user compatibility cache from the user's running installation.
    // Every upstream file and resulting export shim must match pinned hashes.
    public static class CodexNativeComponents {
        static readonly object gate=new object();
        static readonly Dictionary<string,string> Exports=new Dictionary<string,string>{
            {"avatar-overlay-native-page-d4760f1b9cf9.js","export {c_ as OfficialActivityPill,i_ as OfficialActivityStack,yg as OfficialSwipeInput,Le as OfficialReact};"},
            {"app-shared-30671c57ec82.js","export {Ode as OfficialReactDom,wHe as OfficialIntlProvider,THe as InitIntl,ult as OfficialScopeProvider,ylt as InitScope,ARt as SetPersistedAtoms,jI as InitPersistedAtoms};"}
        };
        public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static bool SafeName(string name){return !string.IsNullOrEmpty(name)&&name==Path.GetFileName(name)&&name!="."&&name!=".."&&!name.Contains(":")&&!name.Contains("\\")&&!name.Contains("/");}
        public static string Prepare(string shell,string cache){
            lock(gate){
                Exception last=null;foreach(string exe in CodexWindowActivation.RunningExecutables())try{return Extract(Path.Combine(Path.GetDirectoryName(exe),"resources","app.asar"),shell,cache);}catch(Exception e){last=e;}
                throw new IOException("当前 Codex 的原生任务组件未通过兼容检查（已适配 26.1007.9446.0）。日程提醒不受影响；请等待草鱼适配此版本。",last);
            }
        }
        public static string Extract(string archive,string shell,string cache){
            var json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};byte[] manifest=File.ReadAllBytes(Path.Combine(shell,"provenance.json"));
            var document=json.DeserializeObject(Encoding.UTF8.GetString(manifest));var files=JsonValue.Array(JsonValue.Value(document,"files")).ToArray();
            if(files.Length<1||files.Length>256)throw new InvalidDataException("组件清单无效");
            var info=new FileInfo(archive);string identity=info.FullName+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
            string target=Path.Combine(cache,Hash(manifest.Concat(Encoding.UTF8.GetBytes(identity)).ToArray()).Substring(0,24));
            // Verify cached content as well; do not blindly trust an old marker.
            bool valid=Directory.Exists(target);if(valid)foreach(var item in files){string name=JsonValue.Text(item,"file");if(!SafeName(name))throw new InvalidDataException("组件文件名无效");string path=Path.Combine(target,"vendor",name);if(!File.Exists(path)||Hash(File.ReadAllBytes(path))!=JsonValue.Text(item,"sha256")){valid=false;break;}}
            Directory.CreateDirectory(target);Directory.CreateDirectory(Path.Combine(target,"vendor"));
            if(!valid)using(var stream=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(stream)){
                if(stream.Length<16)throw new InvalidDataException("Codex 资源包不完整");uint marker=reader.ReadUInt32(),header=reader.ReadUInt32();reader.ReadUInt32();uint length=reader.ReadUInt32();
                if(marker!=4||header>32*1024*1024||length>header||16L+length>stream.Length)throw new InvalidDataException("不支持的 Codex 资源包");
                var tree=json.DeserializeObject(Encoding.UTF8.GetString(reader.ReadBytes((int)length)));long origin=8L+header;
                var assets=JsonValue.Value(JsonValue.Value(JsonValue.Value(JsonValue.Value(JsonValue.Value(tree,"files"),"webview"),"files"),"assets"),"files");
                foreach(var item in files){string name=JsonValue.Text(item,"file");if(!SafeName(name))throw new InvalidDataException("组件文件名无效");var entry=JsonValue.Value(assets,name);long offset=JsonValue.Number(entry,"offset"),size=JsonValue.Number(entry,"size");
                    if(entry==null||offset<0||size<1||size>24*1024*1024||origin+offset+size>stream.Length||JsonValue.Bool(entry,"unpacked"))throw new InvalidDataException("Codex 组件版本不匹配："+name);
                    stream.Position=origin+offset;var bytes=reader.ReadBytes((int)size);string upstream=JsonValue.Text(item,"upstreamSha256")??JsonValue.Text(item,"sha256");if(Hash(bytes)!=upstream)throw new InvalidDataException("Codex 组件校验未通过："+name);
                    string exports;if(Exports.TryGetValue(name,out exports))bytes=bytes.Concat(Encoding.UTF8.GetBytes("\n"+exports+"\n")).ToArray();
                    if(Hash(bytes)!=JsonValue.Text(item,"sha256"))throw new InvalidDataException("Codex 组件适配校验未通过："+name);
                    File.WriteAllBytes(Path.Combine(target,"vendor",name),bytes);
                }
            }
            // Only our small host shell is bundled. Recopy it for app updates.
            foreach(string name in new[]{"index.html","overlay.js","overlay.css"})File.Copy(Path.Combine(shell,name),Path.Combine(target,name),true);
            return target;
        }
    }
}
