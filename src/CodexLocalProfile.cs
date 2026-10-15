using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    // Read IDs only from this user's profile. Never read auth.json, modify a
    // database, import the developer's pairing, or manufacture a conversation.
    public static class CodexLocalProfile {
        public static string Home {get{return Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"));}}
        public static string[] Contexts(string home){
            var result=new List<string>();var json=new JavaScriptSerializer{MaxJsonLength=262144};
            try{
                string path=Path.Combine(home,"session_index.jsonl");
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    long start=Math.Max(0,stream.Length-1024*1024);stream.Position=start;
                    using(var reader=new StreamReader(stream,Encoding.UTF8)){if(start>0)reader.ReadLine();string text=reader.ReadToEnd();
                        foreach(var line in text.Split('\n').Reverse())try{string id=JsonValue.Text(json.DeserializeObject(line),"id");Guid value;if(Guid.TryParse(id,out value)&&!result.Contains(id))result.Add(id);if(result.Count==16)break;}catch(ArgumentException){}catch(InvalidOperationException){}
                    }
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
            // Older desktop versions may have no index. Only inspect filenames
            // in recent date directories, never parse message text or scan disks.
            if(result.Count==0)for(int day=0;day<14&&result.Count<16;day++)try{
                var date=DateTime.Now.Date.AddDays(-day);string folder=Path.Combine(home,"sessions",date.ToString("yyyy"),date.ToString("MM"),date.ToString("dd"));
                if(!Directory.Exists(folder))continue;
                foreach(var path in Directory.EnumerateFiles(folder,"rollout-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(64)){
                    var match=Regex.Match(Path.GetFileName(path),@"([0-9a-fA-F-]{36})\.jsonl$");Guid value;
                    if(match.Success&&Guid.TryParse(match.Groups[1].Value,out value)&&!result.Contains(value.ToString()))result.Add(value.ToString());if(result.Count==16)break;
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
            return result.ToArray();
        }
    }
}
