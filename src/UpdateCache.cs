using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace DesktopKitten {
    internal static class UpdateCache {
        internal static string[] Files(string root,int limit,long maxBytes){
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);var files=new List<string>();var pending=new Stack<string>();pending.Push(root);long size=0;int folders=0;
            while(pending.Count>0){string dir=pending.Pop();if(++folders>4096||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新目录含链接或过多子目录");
                foreach(string f in Directory.GetFiles(dir)){UpdatePolicy.NoLinks(root,f);size+=new FileInfo(f).Length;if(files.Count>=limit||size>maxBytes)throw new InvalidDataException("更新目录超过安全范围");files.Add(f);}
                foreach(string child in Directory.GetDirectories(dir)){UpdatePolicy.NoLinks(root,child);pending.Push(child);}
            }return files.ToArray();
        }
        static string[] Stages(string root){if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)return new string[0];return Directory.GetDirectories(root).Take(128).Where(d=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d),"^[a-f0-9]{32}$")).ToArray();}
        static object Read(string root,string stage){
            string marker=Path.Combine(stage,".grassfish-update"),result=Path.Combine(stage,"finished.json");UpdatePolicy.NoLinks(root,marker);UpdatePolicy.NoLinks(root,result);
            if(!File.Exists(marker)||new FileInfo(marker).Length>8||File.ReadAllText(marker)!="v1"||!File.Exists(result)||new FileInfo(result).Length>8192)return null;
            return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(result));
        }
        internal static string LatestResult(string root){
            try{foreach(string stage in Stages(root).OrderByDescending(d=>Directory.GetLastWriteTimeUtc(d))){var value=Read(root,stage);if(value==null)continue;string phase=JsonValue.Text(value,"phase");if(phase=="installed")return "上次更新已完成，当前版本 "+AppVersion.Text+"。";if(phase=="restored")return "上次更新未完成，已恢复备份的旧文件；可以重试。";if(phase=="failed"||phase=="restore-failed")return "上次安装未完成；请重试或手动安装。旧文件备份保留在更新目录。";}
            }catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}return null;
        }
        internal static void Prune(string root,DateTime now){
            // Retain two newest stages and all unresolved/failed installations. Never follow links.
            try{foreach(string stage in Stages(root).OrderByDescending(d=>Directory.GetLastWriteTimeUtc(d)).Skip(2)){
                try{var value=Read(root,stage);string phase=JsonValue.Text(value,"phase");if(phase!="installed"&&phase!="download-failed"&&phase!="cancelled")continue;if(now-Directory.GetLastWriteTimeUtc(stage)<TimeSpan.FromDays(14))continue;
                    string full=UpdatePolicy.Inside(root,stage);UpdatePolicy.NoLinks(root,full);var files=Files(full,8192,512L*1024*1024);
                    foreach(string file in files){UpdatePolicy.NoLinks(root,file);File.Delete(file);}var dirs=new List<string>();var pending=new Stack<string>();pending.Push(full);
                    while(pending.Count>0){var d=pending.Pop();UpdatePolicy.NoLinks(root,d);dirs.Add(d);foreach(var child in Directory.GetDirectories(d))pending.Push(child);}foreach(var dir in dirs.OrderByDescending(d=>d.Length)){UpdatePolicy.NoLinks(root,dir);Directory.Delete(dir,false);}
                }catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
            }}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
