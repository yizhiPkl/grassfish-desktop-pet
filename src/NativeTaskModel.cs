using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
namespace DesktopKitten {
    public sealed class NativeActivity {
        public CodexTask Task;public string TurnKey,Body,Level;public bool IsLoading,Retained;
        public string Key {get{return Task.Id+":"+TurnKey;}}
    }
    // A notification lifecycle, not a list of recent chats. No persisted chat content.
    public sealed class NativeActivityLedger {
        readonly Dictionary<string,NativeActivity> entries=new Dictionary<string,NativeActivity>();
        readonly HashSet<string> dismissed=new HashSet<string>();
        public List<NativeActivity> Items {get{return entries.Values.Where(x=>!dismissed.Contains(x.Key)).OrderByDescending(x=>x.Task.Running).ThenByDescending(x=>x.Task.Attention).ThenByDescending(x=>x.Task.Updated).Take(20).ToList();}}
        public void Apply(CodexTasksSnapshot snapshot){
            if(!snapshot.Connected)return;
            var ids=new HashSet<string>();
            foreach(var task in snapshot.Tasks){
                if(task.Kind!="codex"||task.HostId!="local")continue;ids.Add(task.Id);NativeActivity old;
                entries.TryGetValue(task.Id,out old);
                if(!task.Relevant&&old==null)continue;
                string turn=task.TurnKey??(old==null?"pending:"+task.Id:old.TurnKey);
                bool fresh=old==null||old.TurnKey!=turn;
                bool retained=!fresh&&(old.IsLoading||old.Task.Attention||old.Retained)&&!task.Running&&!task.Attention;
                if(!task.Relevant&&!retained){entries.Remove(task.Id);continue;}
                entries[task.Id]=new NativeActivity{Task=task,TurnKey=turn,IsLoading=task.Running,Retained=retained,
                    Body=task.ActivityBody??(task.Running?"正在思考":task.Attention?"需要你的输入":task.Status=="systemError"?"任务出现错误":"已完成"),
                    Level=task.Running?"info":task.Attention?"warning":task.Status=="systemError"?"danger":"success"};
            }
            foreach(var id in entries.Keys.Where(id=>!ids.Contains(id)).ToArray())entries.Remove(id);
            if(dismissed.Count>200){var known=new HashSet<string>(entries.Values.Select(x=>x.Key));dismissed.RemoveWhere(k=>!known.Contains(k));}
        }
        public NativeActivity Find(string id,string turn){if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(turn))return null;NativeActivity r;return entries.TryGetValue(id,out r)&&r.TurnKey==turn&&!dismissed.Contains(r.Key)?r:null;}
        public void Dismiss(string id,string turn){var r=Find(id,turn);if(r!=null)dismissed.Add(r.Key);}
        public void RetainOpened(string id,string turn){var r=Find(id,turn);if(r!=null)r.Retained=true;}
        public static string ActivityBody(object document,CodexTask task){
            var latest=JsonValue.Array(JsonValue.Value(document,"turns")).FirstOrDefault();
            if(latest==null)return task.Running?"正在思考":task.Attention?"需要你的输入":"有新回复";
            task.TurnKey=JsonValue.Text(latest,"id")??task.TurnKey;
            if(task.Attention)return "需要你的输入";
            if(task.Status=="systemError"||JsonValue.Text(latest,"status")=="failed")return "任务出现错误";
            var items=JsonValue.Array(JsonValue.Value(latest,"items")).Reverse();
            foreach(var item in items){string kind=JsonValue.Text(item,"type"),status=JsonValue.Text(item,"status");bool active=status=="inProgress"||status=="in_progress"||status=="running"||status=="started";
                if(kind=="commandExecution")return task.Running&&active?"正在运行命令":"已运行命令";
                if(kind=="fileChange")return task.Running&&active?"正在修改文件":"已修改文件";
                if(kind=="webSearch")return task.Running&&active?"正在搜索网页":"已搜索网页";
                if(kind=="mcpToolCall")return task.Running&&active?"正在使用工具":"已使用工具";
                if(kind=="imageView")return "已查看图片";
                if(kind=="reasoning"&&task.Running)return "正在思考";
                if(kind=="agentMessage"&&JsonValue.Text(item,"phase")=="final_answer")return task.Running?"正在回复":"已完成";
            }
            return task.Running?"正在思考":"有新回复";
        }
    }
    public sealed class NativeTaskPose {public Rect Window,Badge,Tray;public bool TrayAbove;}
    public static class NativeTaskPlacement {
        public static NativeTaskPose Place(Rect cat,Rect area,bool expanded,double contentHeight){
            const double pad=12,badge=24;double width=Math.Min(360,Math.Max(120,area.Width-40)),height=Math.Min(Math.Max(56,contentHeight),Math.Max(56,area.Height-72));
            double x=Math.Max(area.Left+12,Math.Min(cat.Left+cat.Width/2-badge/2,area.Right-12-badge));
            double y=cat.Bottom+5;if(y+badge>area.Bottom-10)y=cat.Top-badge-8;y=Math.Max(area.Top+10,Math.Min(y,area.Bottom-badge-10));
            var b=new Rect(x,y,badge,badge);var tray=Rect.Empty;bool above=false;
            if(expanded){double tx=Math.Max(area.Left+pad,Math.Min(b.Left+b.Width/2-width/2,area.Right-pad-width));
                double belowTop=Math.Max(b.Bottom+14,cat.Bottom+12),aboveBottom=Math.Min(b.Top-14,cat.Top-12);
                double belowSpace=area.Bottom-pad-belowTop,aboveSpace=aboveBottom-area.Top-pad;
                above=belowSpace<height&&aboveSpace>=belowSpace;
                double space=above?aboveSpace:belowSpace;height=Math.Min(height,Math.Max(40,space));
                double ty=above?aboveBottom-height:belowTop;
                if(space<40){ty=Math.Max(area.Top+pad,Math.Min(cat.Top+(cat.Height-height)/2,area.Bottom-pad-height));tx=cat.Right+12+width<=area.Right-pad?cat.Right+12:cat.Left-12-width;tx=Math.Max(area.Left+pad,Math.Min(tx,area.Right-pad-width));}
                tray=new Rect(tx,ty,width,height);
            }
            var total=tray.IsEmpty?b:Rect.Union(b,tray);total.Inflate(pad,pad);
            return new NativeTaskPose{Window=total,Badge=b,Tray=tray,TrayAbove=above};
        }
    }
}
