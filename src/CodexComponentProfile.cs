using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    // Only packaged, reviewed profiles are trusted. Never load a manifest or
    // executable adapter from the cache, the network, or an unrecognized build.
    internal sealed class CodexComponentProfile {
        internal string Key,Source;internal object[] Files;internal string[] Styles;
        internal readonly Dictionary<string,string> Shims=new Dictionary<string,string>();
        internal readonly Dictionary<string,Tuple<string,string>> Bridge=new Dictionary<string,Tuple<string,string>>();
        static readonly string[] Required={"OfficialActivityPill","OfficialActivityStack","OfficialSwipeInput","OfficialReact","OfficialReactDom","OfficialIntlProvider","InitIntl","OfficialScopeProvider","InitScope","SetPersistedAtoms","InitPersistedAtoms","OfficialChevron","OfficialAppScope","OfficialBadge","InitBadge"};
        static bool Identifier(string value){return value!=null&&Regex.IsMatch(value,@"^[A-Za-z_$][A-Za-z0-9_$]*$");}
        internal static CodexComponentProfile Parse(byte[] bytes){
            var document=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));
            var p=new CodexComponentProfile{Key=CodexNativeComponents.Hash(bytes),Source=JsonValue.Text(document,"sourcePackage"),Files=JsonValue.Array(JsonValue.Value(document,"files")).ToArray()};
            if(p.Files.Length<1||p.Files.Length>256||string.IsNullOrWhiteSpace(p.Source))throw new InvalidDataException("组件清单无效");
            var names=new HashSet<string>();foreach(var file in p.Files){string name=JsonValue.Text(file,"file");if(!CodexNativeComponents.SafeName(name)||!(name.EndsWith(".js")||name.EndsWith(".css"))||!names.Add(name)||!ValidHash(JsonValue.Text(file,"sha256"))||!ValidHash(JsonValue.Text(file,"upstreamSha256")??JsonValue.Text(file,"sha256")))throw new InvalidDataException("组件文件清单无效");}
            if(JsonValue.Number(document,"schemaVersion")==2){
                foreach(var pair in JsonValue.Object(JsonValue.Value(document,"shims")))p.Shims.Add(pair.Key,Convert.ToString(pair.Value));
                foreach(var pair in JsonValue.Object(JsonValue.Value(document,"bridge")))p.Bridge.Add(pair.Key,Tuple.Create(JsonValue.Text(pair.Value,"file"),JsonValue.Text(pair.Value,"symbol")));
                p.Styles=JsonValue.Array(JsonValue.Value(document,"styles")).Select(Convert.ToString).ToArray();
            }else if(JsonValue.Number(document,"schemaVersion")==0&&p.Source=="OpenAI.Codex_26.1007.9446.0_x64"){
                const string avatar="avatar-overlay-native-page-d4760f1b9cf9.js",shared="app-shared-30671c57ec82.js",mascot="avatar-mascot-button-11223c6d3a1d.js";
                p.Shims.Add(avatar,"export {c_ as OfficialActivityPill,i_ as OfficialActivityStack,yg as OfficialSwipeInput,Le as OfficialReact};");
                p.Shims.Add(shared,"export {Ode as OfficialReactDom,wHe as OfficialIntlProvider,THe as InitIntl,ult as OfficialScopeProvider,ylt as InitScope,ARt as SetPersistedAtoms,jI as InitPersistedAtoms};");
                foreach(string name in Required.Take(4))p.Bridge.Add(name,Tuple.Create(avatar,name));
                foreach(string name in Required.Skip(4).Take(7))p.Bridge.Add(name,Tuple.Create(shared,name));
                p.Bridge.Add("OfficialChevron",Tuple.Create(shared,"_T"));p.Bridge.Add("OfficialAppScope",Tuple.Create(shared,"H0t"));p.Bridge.Add("OfficialBadge",Tuple.Create(mascot,"n"));p.Bridge.Add("InitBadge",Tuple.Create(mascot,"r"));
                p.Styles=new[]{"app-shared-6ed15bfa827c.css","app-initial-99e8b4b455fa.css","avatar-overlay-native-page-b380d8acf007.css","avatar-mascot-button-f35a0d712228.css"};
            }else throw new InvalidDataException("不支持的组件清单格式");
            if(p.Shims.Any(x=>!names.Contains(x.Key)||!x.Key.EndsWith(".js")||x.Value.Length>2048)||!Required.All(p.Bridge.ContainsKey)||p.Bridge.Any(x=>!Identifier(x.Key)||!names.Contains(x.Value.Item1)||!x.Value.Item1.EndsWith(".js")||!Identifier(x.Value.Item2))||p.Styles.Length<1||p.Styles.Any(x=>!names.Contains(x)||!x.EndsWith(".css")))throw new InvalidDataException("组件适配绑定无效");
            return p;
        }
        static bool ValidHash(string value){return value!=null&&Regex.IsMatch(value,"^[a-f0-9]{64}$");}
        internal byte[] Adapt(string name,byte[] bytes){string shim;return Shims.TryGetValue(name,out shim)?bytes.Concat(Encoding.UTF8.GetBytes("\n"+shim+"\n")).ToArray():bytes;}
        internal string BridgeScript(){return string.Join("\n",Bridge.OrderBy(x=>x.Key).Select(x=>"export {"+x.Value.Item2+" as "+x.Key+"} from './vendor/"+x.Value.Item1+"';"))+"\n";}
        internal string StyleSheet(){return string.Join("\n",Styles.Select(x=>"@import url('./vendor/"+x+"');"))+"\n";}
        internal static CodexComponentProfile[] Load(string shell){
            var files=new List<string>();string profiles=Path.Combine(shell,"profiles");
            if(Directory.Exists(profiles))files.AddRange(Directory.GetFiles(profiles,"*.json").OrderByDescending(Path.GetFileName).Take(16));
            files.Add(Path.Combine(shell,"provenance.json"));return files.Select(x=>Parse(File.ReadAllBytes(x))).ToArray();
        }
    }
}
