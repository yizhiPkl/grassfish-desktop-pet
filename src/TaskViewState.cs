using System;
using System.Collections.Generic;
using System.Linq;
namespace DesktopKitten {
    // Memory only. The disposable web page never owns the only copy of a draft or delivery state.
    public sealed class TaskViewState {
        sealed class Draft {public string Text;public long Version;}
        sealed class Delivery {public string Id,Turn,Prompt,Error;public bool Ok;public long Version;}
        readonly Dictionary<string,Draft> drafts=new Dictionary<string,Draft>();
        readonly Dictionary<string,Delivery> pending=new Dictionary<string,Delivery>(),results=new Dictionary<string,Delivery>();
        long revision;
        public int DraftCount {get{return drafts.Values.Count(d=>!string.IsNullOrEmpty(d.Text));}}
        public int PendingCount {get{return pending.Count;}}
        public long Revision {get{return revision;}}
        public string Get(string id){Draft d;return drafts.TryGetValue(id,out d)?d.Text:"";}
        public long Version(string id){Draft d;return drafts.TryGetValue(id,out d)?d.Version:0;}
        public bool Set(string id,string text){
            if(string.IsNullOrEmpty(id)||id.Length>160||text==null||text.Length>20000)return false;
            Draft d;if(!drafts.TryGetValue(id,out d)){
                if(drafts.Count>=100){var empty=drafts.FirstOrDefault(x=>string.IsNullOrEmpty(x.Value.Text)&&!pending.Values.Any(p=>p.Id==x.Key));if(empty.Key==null)return false;drafts.Remove(empty.Key);}
                drafts[id]=d=new Draft{Text=""};
            }
            if(d.Text!=text){d.Text=text;d.Version=++revision;}return true;
        }
        public Dictionary<string,long> Versions(){return drafts.ToDictionary(d=>d.Key,d=>d.Value.Version);}
        public void Import(object state,Dictionary<string,long> before){
            foreach(var d in JsonValue.Array(JsonValue.Value(state,"drafts")).Take(100)){
                string id=JsonValue.Text(d,"id"),text=JsonValue.Text(d,"text");long version;
                if(id!=null&&Version(id)==(before.TryGetValue(id,out version)?version:0))Set(id,text);
            }
        }
        public bool Begin(string id,string turn,string prompt){
            if(string.IsNullOrWhiteSpace(prompt)||prompt.Length>20000||string.IsNullOrEmpty(turn)||pending.Values.Any(d=>d.Id==id))return false;
            if(!Set(id,prompt))return false;string key=id+":"+turn;
            pending[key]=new Delivery{Id=id,Turn=turn,Prompt=prompt,Version=++revision};results.Remove(key);return true;
        }
        public void Finish(string id,string turn,bool ok,string error){
            string key=id+":"+turn;Delivery d;if(!pending.TryGetValue(key,out d))return;pending.Remove(key);
            if(ok&&Get(id).Trim()==d.Prompt.Trim())Set(id,"");
            // Advance even when an empty draft was already present: an older release snapshot must not resurrect it.
            Draft draft;if(drafts.TryGetValue(id,out draft))draft.Version=++revision;
            d.Ok=ok;d.Error=error;d.Version=++revision;results[key]=d;
            if(results.Count>100)results.Remove(results.OrderBy(x=>x.Value.Version).First().Key);
        }
        public object Export(){return new{revision,drafts=drafts.Select(d=>new{id=d.Key,text=d.Value.Text}).ToArray(),pending=pending.Select(d=>d.Key).ToArray(),results=results.Values.Select(d=>new{id=d.Id,turnKey=d.Turn,prompt=d.Prompt,ok=d.Ok,error=d.Error,version=d.Version}).ToArray()};}
    }
}
