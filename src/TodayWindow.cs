using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace DesktopKitten {
    public sealed class ReminderCommand : ICommand {
        readonly Action<object> run;public ReminderCommand(Action<object> action){run=action;}
        public bool CanExecute(object parameter){return true;}public void Execute(object parameter){run(parameter);}
        public event EventHandler CanExecuteChanged {add{}remove{}}
    }
    public sealed class AgendaRow {
        public string Title {get;private set;}public string TimeText {get;private set;}public string StateText {get;private set;}
        public Visibility ActionsVisibility {get;private set;}public Visibility RestoreVisibility {get;private set;}public Visibility EditVisibility {get;private set;}
        public Visibility SnoozeVisibility {get;private set;}
        public ICommand Complete {get;private set;}public ICommand Skip {get;private set;}public ICommand Snooze {get;private set;}public ICommand Restore {get;private set;}public ICommand Edit {get;private set;}
        public AgendaRow(KittenApp app,AgendaItem item,DateTime local,DateTime utc){
            var r=item.Reminder;Title=r.Title;string state=item.State;
            StateText=state=="done"?"已完成":state=="skipped"?"已跳过":state=="closed"?"已收起":state=="snoozed"?"稍后提醒":item.Upcoming?"未到时间":"待处理";
            var due=state=="snoozed"?ReminderWorkflow.Local(r.SnoozeUntilUtc,local,utc):r.Due;
            TimeText=due.ToString(due.Date==local.Date?"HH:mm":"MM月dd日 HH:mm")+(r.AllowDuringQuiet?" · 重要约定":"")+(state=="snoozed"?" 再提醒":"");
            bool active=state=="pending"||state=="snoozed";ActionsVisibility=active||item.Upcoming?Visibility.Visible:Visibility.Collapsed;SnoozeVisibility=active?Visibility.Visible:Visibility.Collapsed;RestoreVisibility=state=="closed"?Visibility.Visible:Visibility.Collapsed;
            EditVisibility=!string.IsNullOrEmpty(r.ScheduleId)&&app.Config.CustomSchedules.Any(s=>s.Id==r.ScheduleId)?Visibility.Visible:Visibility.Collapsed;
            Complete=new ReminderCommand(_=>app.ResolveAgenda(r.Key,"done",item.Upcoming));Skip=new ReminderCommand(_=>app.ResolveAgenda(r.Key,"skipped",item.Upcoming));
            Snooze=new ReminderCommand(value=>{int minutes;if(value!=null&&int.TryParse(value.ToString(),out minutes))app.SnoozeReminder(r.Key,minutes);else SnoozeDialog.Open(app,r.Key);});
            Restore=new ReminderCommand(_=>app.RestoreReminder(r.Key));Edit=new ReminderCommand(_=>app.OpenSchedule(r.ScheduleId));
        }
    }
    public sealed class SnoozeDialog : Window {
        public static void Open(KittenApp app,string key){
            if(!app.Config.Pending.Any(r=>r.Key==key))return;
            var w=new SnoozeDialog(app,key);w.Show();w.Activate();
        }
        SnoozeDialog(KittenApp app,string key){
            Title="稍后再提醒";Width=330;Height=220;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(248,250,242));Resources=GrassFishUi.Create();ShowInTaskbar=true;
            var stack=new StackPanel{Margin=new Thickness(22)};Content=stack;stack.Children.Add(GrassFishUi.Text("过多少分钟再提醒？",16,true));
            var input=new TextBox{Text="10",MaxLength=4,Margin=new Thickness(0,12,0,8)};stack.Children.Add(input);var note=GrassFishUi.Text("可填写1～1440分钟；只延后这一次。",11);stack.Children.Add(note);
            var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};stack.Children.Add(buttons);
            var save=GrassFishUi.Button("稍后提醒",()=>{int minutes;if(!int.TryParse(input.Text,out minutes)||minutes<1||minutes>1440){note.Text="请填写1～1440的整数分钟。";return;}if(!app.SnoozeReminder(key,minutes)){note.Text="这条提醒已经处理，无需再次延后。";return;}Close();});save.IsDefault=true;save.Margin=new Thickness(0,0,8,0);buttons.Children.Add(save);var cancel=GrassFishUi.Button("取消",()=>Close());cancel.IsCancel=true;buttons.Children.Add(cancel);Loaded+=(s,e)=>{input.Focus();input.SelectAll();};
        }
    }
    public sealed class TodayWindow : Window {
        readonly KittenApp app;public readonly Grid Root;readonly ListBox list;readonly ComboBox filter;readonly TextBlock summary,quietStatus,notes;
        readonly TextBox until,start,end;readonly CheckBox daily;string signature;
        public const string Layout=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Margin='22'>
 <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
 <Grid Margin='0,0,0,12'><StackPanel><TextBlock Text='草鱼的今日小事' FontSize='23' FontWeight='SemiBold'/><TextBlock Name='Summary' FontSize='11' Foreground='#82907C' Margin='0,7,110,0' TextWrapping='Wrap'/></StackPanel><Button Name='AddToday' Content='+ 临时小事' HorizontalAlignment='Right' VerticalAlignment='Top' Padding='12,9'/></Grid>
 <Border Grid.Row='1' Background='#ECF1E3' CornerRadius='16' Padding='14' Margin='0,0,0,12'><StackPanel>
  <TextBlock Name='QuietStatus' Text='正常提醒中' FontWeight='SemiBold'/>
  <WrapPanel Margin='0,10,0,0'><Button Name='Quiet30' Content='安静30分钟' FontSize='11' Padding='10,7' Margin='0,0,7,7'/><Button Name='Quiet60' Content='安静1小时' FontSize='11' Padding='10,7' Margin='0,0,7,7'/><TextBox Name='QuietUntil' Width='64' MaxLength='5' Margin='0,0,6,7' Padding='7,5'/><Button Name='QuietTo' Content='安静到此时' FontSize='11' Padding='10,7' Margin='0,0,7,7'/><Button Name='Resume' Content='恢复提醒' FontSize='11' Padding='10,7' Margin='0,0,0,7'/></WrapPanel>
  <WrapPanel><CheckBox Name='QuietDaily' Content='每天定时安静' Margin='0,5,12,0'/><TextBox Name='QuietStart' Width='64' MaxLength='5' Padding='7,5'/><TextBlock Text=' — ' VerticalAlignment='Center'/><TextBox Name='QuietEnd' Width='64' MaxLength='5' Padding='7,5'/><Button Name='SaveQuiet' Content='保存时段' Margin='10,0,0,0' Padding='10,7' FontSize='11'/></WrapPanel>
  <TextBlock Text='只手动或按时段安静，不检测全屏、演示或锁屏。重要约定可例外；Codex任务通知不受影响。' FontSize='10' Foreground='#89977E' TextWrapping='Wrap' Margin='0,8,0,0'/>
 </StackPanel></Border>
 <ComboBox Name='Filter' Grid.Row='2' Margin='0,0,0,10' HorizontalAlignment='Left' Width='150'><ComboBoxItem Content='全部小事'/><ComboBoxItem Content='待处理'/><ComboBoxItem Content='已完成'/><ComboBoxItem Content='稍后提醒'/><ComboBoxItem Content='已跳过'/><ComboBoxItem Content='已收起'/></ComboBox>
 <ListBox Name='Items' Grid.Row='3' BorderThickness='0' Background='Transparent' ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Auto' ScrollViewer.CanContentScroll='True' VirtualizingStackPanel.IsVirtualizing='True' VirtualizingStackPanel.VirtualizationMode='Recycling'>
  <ListBox.ItemTemplate><DataTemplate><Border Background='White' CornerRadius='14' Padding='14' Margin='0,0,0,8' BorderBrush='#E4EBDD' BorderThickness='1'><StackPanel>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Title}' FontSize='14' FontWeight='SemiBold' TextWrapping='Wrap' Margin='0,0,10,0'/><TextBlock Grid.Column='1' Text='{Binding StateText}' Foreground='#7C9472' FontSize='11'/></Grid>
   <TextBlock Text='{Binding TimeText}' FontSize='11' Foreground='#8A9682' Margin='0,6,0,8'/>
   <WrapPanel><WrapPanel Visibility='{Binding ActionsVisibility}'><Button Content='完成' Command='{Binding Complete}' FontSize='11' Padding='11,6' Margin='0,0,7,0'/><Button Content='稍后10分钟' Command='{Binding Snooze}' Visibility='{Binding SnoozeVisibility}' CommandParameter='10' FontSize='11' Padding='11,6' Margin='0,0,7,0'/><Button Content='自定义稍后' Command='{Binding Snooze}' Visibility='{Binding SnoozeVisibility}' FontSize='11' Padding='11,6' Margin='0,0,7,0'/><Button Content='这次跳过' Command='{Binding Skip}' FontSize='11' Padding='11,6' Margin='0,0,7,0'/></WrapPanel><Button Content='重新提醒' Command='{Binding Restore}' Visibility='{Binding RestoreVisibility}' FontSize='11' Padding='11,6' Margin='0,0,7,0'/><Button Content='修改约定' Command='{Binding Edit}' Visibility='{Binding EditVisibility}' FontSize='11' Padding='11,6'/></WrapPanel>
  </StackPanel></Border></DataTemplate></ListBox.ItemTemplate>
 </ListBox>
 <TextBlock Name='Notes' Grid.Row='4' FontSize='10' Foreground='#8D9B82' TextWrapping='Wrap' Margin='0,10,0,0' Text='已处理记录按7天、最多200条清理；未处理和稍后项保留到你处理。×只收起，不计完成。'/>
</Grid>";
        public TodayWindow(KittenApp app){
            this.app=app;Title="草鱼的今日小事";Width=700;Height=Math.Min(740,SystemParameters.WorkArea.Height-24);MinWidth=570;MinHeight=Math.Min(540,Height);WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(248,250,242));Resources=GrassFishUi.Create();
            Root=(Grid)XamlReader.Parse(Layout);Content=Root;list=(ListBox)Root.FindName("Items");filter=(ComboBox)Root.FindName("Filter");summary=(TextBlock)Root.FindName("Summary");quietStatus=(TextBlock)Root.FindName("QuietStatus");notes=(TextBlock)Root.FindName("Notes");until=(TextBox)Root.FindName("QuietUntil");start=(TextBox)Root.FindName("QuietStart");end=(TextBox)Root.FindName("QuietEnd");daily=(CheckBox)Root.FindName("QuietDaily");
            until.Text=DateTime.Now.AddHours(1).ToString("HH:mm");start.Text=app.Config.QuietStart;end.Text=app.Config.QuietEnd;daily.IsChecked=app.Config.QuietDailyEnabled;
            Button("AddToday").Click+=(s,e)=>app.OpenSchedule(null);Button("Quiet30").Click+=(s,e)=>app.SetQuietMinutes(30);Button("Quiet60").Click+=(s,e)=>app.SetQuietMinutes(60);Button("Resume").Click+=(s,e)=>app.ResumeReminders();
            Button("QuietTo").Click+=(s,e)=>{try{var times=Store.ParseTimes(until.Text);if(times.Count!=1)throw new FormatException("请填写一个结束时间，例如20:30。");var now=DateTime.Now;var when=now.Date.Add(times[0]);if(when<=now)when=when.AddDays(1);app.SetQuietUntil(DateTime.UtcNow.Add(when-now));notes.Text="已设置："+QuietPolicy.Description(app.Config,DateTime.Now,DateTime.UtcNow);}catch(FormatException ex){notes.Text=ex.Message;}};
            Button("SaveQuiet").Click+=(s,e)=>{try{QuietPolicy.Validate(start.Text,end.Text);app.Config.QuietStart=Store.NormalizeTimes(start.Text);app.Config.QuietEnd=Store.NormalizeTimes(end.Text);app.Config.QuietDailyEnabled=daily.IsChecked==true;app.Config.QuietBypassUntilUtc=DateTime.MinValue;app.LocalRemindersChanged();notes.Text="安静时段已保存。";}catch(FormatException ex){notes.Text=ex.Message;}};
            filter.SelectionChanged+=(s,e)=>Refresh(true);filter.SelectedIndex=0;Refresh(true);
        }
        Button Button(string name){return(Button)Root.FindName(name);}
        public void Refresh(bool force=false){
            var local=DateTime.Now;var utc=DateTime.UtcNow;var items=app.Scheduler.Today(local,utc);quietStatus.Text=QuietPolicy.Description(app.Config,local,utc);
            var future=items.Where(r=>r.Upcoming).OrderBy(r=>r.Reminder.Due).FirstOrDefault();
            summary.Text=local.ToString("MM月dd日")+" · 已完成 "+items.Count(r=>r.State=="done")+" 件 · 待处理 "+items.Count(r=>r.State=="pending"||r.State=="snoozed")+" 件"+(future==null?"":" · 下一件 "+future.Reminder.Due.ToString("HH:mm")+"（约"+Math.Max(1,(int)Math.Ceiling((future.Reminder.Due-local).TotalMinutes))+"分钟后）");
            string sig=local.Date.Ticks+";"+filter.SelectedIndex+";"+string.Join("|",items.Select(r=>r.Reminder.Key+":"+r.State+":"+r.Reminder.Title+":"+r.Reminder.SnoozeUntilUtc.Ticks+":"+r.Reminder.AllowDuringQuiet));
            if(!force&&sig==signature)return;signature=sig;
            var states=new[]{"all","pending","done","snoozed","skipped","closed"};string selected=states[Math.Max(0,filter.SelectedIndex)];
            list.ItemsSource=items.Where(r=>selected=="all"||(selected=="pending"?(r.State=="pending"||r.Upcoming):r.State==selected)).Select(r=>new AgendaRow(app,r,local,utc)).ToList();
        }
    }
}
