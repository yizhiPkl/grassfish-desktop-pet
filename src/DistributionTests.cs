using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
namespace DesktopKitten {
    public static class DistributionTests {
        sealed class Lease:IDisposable {readonly Action release;public Lease(Action release){this.release=release;}public void Dispose(){release();}}
        public static void Run(Action<bool,string> check){
            string root=Path.Combine(Path.GetTempPath(),"grassfish-distribution-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                string a=Guid.NewGuid().ToString(),b=Guid.NewGuid().ToString();
                check(CodexLocalProfile.Contexts(root).Length==0,"fresh user without Codex sessions has no fabricated pairing");
                File.WriteAllText(Path.Combine(root,"session_index.jsonl"),"{\"id\":\""+a+"\"}\n{\"id\":\"not-an-id\"}\n{\"id\":\""+b+"\"}\n{broken\n");
                var ids=CodexLocalProfile.Contexts(root);check(ids.SequenceEqual(new[]{b,a}),"another user profile automatically discovers its own recent IDs, ignores malformed lines");
                using(var connection=new CodexConnection(root,"developer-private-id"))check(connection.ContextId==null,"legacy developer pairing is never used by production constructor");
                check(CodexWindowActivation.IsCodexDesktop("D:/WindowsApps/OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0/app/ChatGPT.exe"),"MSIX Codex on another drive is detected");
                check(!CodexWindowActivation.IsCodexDesktop("D:/WindowsApps/OpenAI.Codex_26.1007.9446.0_x64__notpublisher/app/ChatGPT.exe"),"unrecognized package family cannot impersonate Codex");
                check(NativeProcesses.IsCodexBackend("D:/WindowsApps/OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0/app/resources/codex.exe"),"task animation observer supports standard bundled MSIX backend on another drive");
                check(NativeProcesses.IsCodexBackend("C:/Users/another/AppData/Local/OpenAI/Codex/bin/version/codex.exe")&&!NativeProcesses.IsCodexBackend("C:/tmp/codex.exe"),"backend discovery has no developer username dependency");
                check(CodexNativeComponents.SafeName("module.js")&&!CodexNativeComponents.SafeName("../module.js")&&!CodexNativeComponents.SafeName("..\\module.js")&&!CodexNativeComponents.SafeName("C:module.js"),"official component filenames reject traversal and ADS");
                check(CodexNativeComponents.Hash(new byte[0])=="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855","component SHA256 implementation");
                File.Delete(Path.Combine(root,"session_index.jsonl"));string day=Path.Combine(root,"sessions",DateTime.Now.ToString("yyyy/MM/dd"));Directory.CreateDirectory(day);File.WriteAllText(Path.Combine(day,"rollout-test-"+a+".jsonl"),"private content not parsed");check(CodexLocalProfile.Contexts(root).SequenceEqual(new[]{a}),"older Codex without index discovers only local recent rollout filenames");
            }finally{Directory.Delete(root,true);}
            // Pure WPF lifecycle fixture: does not show/activate any desktop window.
            var owner=new Window();var menu=new ContextMenu();int acquisitions=0,releases=0;
            using(var controller=new PetContextMenu(owner,menu,()=>{acquisitions++;return new Lease(()=>releases++);})){check(!menu.StaysOpen,"pet context menu enables standard outside-click dismissal");controller.Begin();controller.Begin();check(controller.Active&&acquisitions==1,"right click acquires activation exactly once");controller.Dismiss();controller.Dismiss();check(!controller.Active&&releases==1,"outside dismissal restores nonactivation once");controller.Begin();controller.Dispose();check(releases==2&&!controller.Active,"menu disposal releases temporary activation");controller.Begin();check(acquisitions==2,"disposed menu cannot reactivate owner");}
        }
    }
}
