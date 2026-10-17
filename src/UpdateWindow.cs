using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace DesktopKitten {
    public sealed class UpdateWindow:Window {
        readonly UpdateManager manager;readonly Func<Task> apply;
        public readonly Grid Root;bool preparing;string notice;
        public const string Layout=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Margin='22'>
<Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
<Border Background='#EAF0DD' CornerRadius='19' Padding='16'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='64'/></Grid.ColumnDefinitions><StackPanel VerticalAlignment='Center'><TextBlock Text='草鱼的小更新' FontSize='23' FontWeight='SemiBold' Foreground='#375944'/><TextBlock Name='VersionLine' FontSize='11' Margin='0,8,0,0' Foreground='#718568'/></StackPanel><Image Name='Portrait' Grid.Column='1' Height='62'/></Grid></Border>
<TextBlock Name='Headline' Grid.Row='1' FontSize='15' FontWeight='SemiBold' Margin='3,16,3,9' TextWrapping='Wrap'/>
<Border Grid.Row='2' Background='White' CornerRadius='14' Padding='13'><ScrollViewer MaxHeight='175' VerticalScrollBarVisibility='Auto'><TextBlock Name='ReleaseNotes' TextWrapping='Wrap' LineHeight='22' FontSize='12'/></ScrollViewer></Border>
<StackPanel Grid.Row='3' Margin='3,12,3,0'><ProgressBar Name='Progress' Height='7' Minimum='0' Maximum='100' Visibility='Collapsed'/><TextBlock Name='Status' Margin='0,8,0,0' FontSize='11' TextWrapping='Wrap' Foreground='#718568'/></StackPanel>
<TextBlock Grid.Row='4' Text='只更新草鱼，不关闭 Codex。沿用当前安装目录，保留日程和设置；请先保存小窝里的编辑。有回复草稿时不会强退。' TextWrapping='Wrap' FontSize='10' Foreground='#8D9B82' LineHeight='18' Margin='3,12,3,12'/>
<WrapPanel Grid.Row='5' HorizontalAlignment='Right'><Button Name='Skip' Content='跳过此版本' FontSize='11' Padding='9,7' Margin='0,0,7,0'/><Button Name='Later' Content='稍后再说' FontSize='11' Padding='10,7' Margin='0,0,7,0'/><Button Name='Apply' Content='立即更新' FontSize='12' Padding='15,7' Background='#72947B' Foreground='White'/></WrapPanel></Grid>";
        public UpdateWindow(UpdateManager manager,Func<Task> apply,ImageSource portrait){
            this.manager=manager;this.apply=apply;Title="草鱼的小更新";Width=460;SizeToContent=SizeToContent.Height;MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-30);ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Microsoft YaHei UI");Resources=GrassFishUi.Create();Background=new SolidColorBrush(Color.FromRgb(248,250,242));
            Root=(Grid)XamlReader.Parse(Layout);Content=new ScrollViewer{Content=Root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};((Image)Root.FindName("Portrait")).Source=portrait;
            Button("Apply").Click+=async(s,e)=>{notice=null;try{await this.apply();}catch(Exception){Notice("更新没有完成，当前版本仍然保留。请重试。");}Refresh();};
            Button("Later").Click+=(s,e)=>Close();Button("Skip").Click+=(s,e)=>{manager.Skip();Close();};
            manager.Changed+=Refresh;Closing+=(s,e)=>{if(preparing)e.Cancel=true;else if(manager.Phase=="downloading")manager.Cancel();};Closed+=(s,e)=>manager.Changed-=Refresh;Refresh();
        }
        Button Button(string name){return(Button)Root.FindName(name);}TextBlock Text(string name){return(TextBlock)Root.FindName(name);}
        public bool Preparing {get{return preparing;}set{preparing=value;Refresh();}}
        public void Notice(string text){notice=text;Refresh();}
        public void Refresh(){
            var offer=manager.Offer;Text("VersionLine").Text="本机 "+AppVersion.Text+(offer==null?"":"  →  新版 "+offer.Manifest.version);
            Text("Headline").Text=offer==null?"草鱼在这里，等下一个小进步。":"发现新版，给草鱼换一件新本领吧。";
            Text("ReleaseNotes").Text=offer==null?"暂时没有可安装的新版本。":string.IsNullOrWhiteSpace(offer.Manifest.notes)?"本次包含功能改进与问题修复。":offer.Manifest.notes;
            Text("Status").Text=notice??manager.Status;var progress=(ProgressBar)Root.FindName("Progress");progress.Visibility=manager.Phase=="downloading"?Visibility.Visible:Visibility.Collapsed;progress.Value=manager.Progress;
            Button("Apply").IsEnabled=!preparing&&!manager.Busy&&offer!=null;Button("Apply").Content=manager.Stage==null?"立即更新":"安装并重启";Button("Later").IsEnabled=!preparing;Button("Later").Content=manager.Phase=="downloading"?"取消下载":"稍后再说";Button("Skip").IsEnabled=!preparing&&!manager.Busy&&offer!=null;
        }
    }
}
