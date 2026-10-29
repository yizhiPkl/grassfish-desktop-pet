using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DesktopKitten {
    public static class GrassFishUi {
        public static Brush Ink=new SolidColorBrush(Color.FromRgb(57,72,68)),Muted=new SolidColorBrush(Color.FromRgb(129,144,133)),Green=new SolidColorBrush(Color.FromRgb(92,130,110));
        public const string Theme=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='TextBlock'><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='Foreground' Value='#394844'/><Setter Property='FontSize' Value='13'/></Style>
 <Style TargetType='Button'><Setter Property='Background' Value='#EFF3E9'/><Setter Property='Foreground' Value='#527363'/><Setter Property='Padding' Value='15,9'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='12'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Name='B' CornerRadius='12' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.65'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#7CA38C'/><Setter TargetName='B' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='CheckBox'><Setter Property='FontSize' Value='12'/><Setter Property='Foreground' Value='#52635B'/><Setter Property='Margin' Value='0,7'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='36'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border Name='Track' Background='#DDE5DA' Height='20' Width='34' CornerRadius='10' VerticalAlignment='Center'><Border Name='Dot' Background='White' Height='14' Width='14' CornerRadius='7' HorizontalAlignment='Left' Margin='3,0,0,0'/></Border><ContentPresenter Grid.Column='1' Margin='9,0,0,0' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#72947E'/><Setter TargetName='Dot' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Dot' Property='Margin' Value='0,0,3,0'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Track' Property='BorderBrush' Value='#405F4E'/><Setter TargetName='Track' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='TextBox'><Setter Property='Background' Value='#F6F8F1'/><Setter Property='Foreground' Value='#394844'/><Setter Property='BorderBrush' Value='#E3E9DB'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='11,8'/><Setter Property='FontSize' Value='13'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10'><ScrollViewer Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#8FAB92'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ComboBox'><Setter Property='Padding' Value='11,8'/><Setter Property='FontSize' Value='12'/><Setter Property='Background' Value='#F6F8F1'/><Setter Property='Foreground' Value='#394844'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='#F6F8F1' BorderBrush='#E3E9DB' BorderThickness='1' CornerRadius='10'><Path Data='M 0,0 L 4,4 L 8,0' Stroke='#82977D' StrokeThickness='1.4' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Margin='11,8,30,8' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' IsHitTestVisible='False'/><Popup Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'><Border Background='White' BorderBrush='#DCE7D4' BorderThickness='1' CornerRadius='12' Padding='5' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'><ScrollViewer MaxHeight='240' CanContentScroll='True'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ComboBoxItem'><Setter Property='Padding' Value='10,7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border Name='B' Padding='{TemplateBinding Padding}' CornerRadius='8' Background='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='B' Property='Background' Value='#E8F0E1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='TabControl'><Setter Property='BorderThickness' Value='0'/><Setter Property='Background' Value='Transparent'/><Setter Property='Padding' Value='12,0,0,0'/></Style>
 <Style TargetType='TabItem'><Setter Property='Foreground' Value='#7D8A7E'/><Setter Property='Padding' Value='18,12'/><Setter Property='Margin' Value='0,3'/><Setter Property='FontSize' Value='13'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'><Border Name='B' CornerRadius='13' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter Name='HeaderText' ContentSource='Header' HorizontalAlignment='Left'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='B' Property='Background' Value='#E5EEE0'/><Setter Property='Foreground' Value='#3F644F'/><Setter TargetName='HeaderText' Property='TextElement.FontWeight' Value='SemiBold'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.72'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style x:Key='SizeChoice' TargetType='RadioButton'><Setter Property='Background' Value='#F0F4EA'/><Setter Property='Foreground' Value='#627469'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Margin' Value='0,0,8,0'/><Setter Property='Padding' Value='18,9'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'><Border Name='B' CornerRadius='11' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='B' Property='Background' Value='#71957D'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#355C41'/><Setter TargetName='B' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ListBoxItem'><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border Name='B' CornerRadius='12' Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}' BorderBrush='Transparent' BorderThickness='1'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='B' Property='Background' Value='#E8F0E1'/><Setter TargetName='B' Property='BorderBrush' Value='#C7D8BD'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#D2DEC9'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#70927A'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ScrollBar'><Setter Property='Width' Value='8'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'><Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb Background='#D5DFCC'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{TemplateBinding Background}' CornerRadius='4' Margin='1,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0'/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>";
        public static ResourceDictionary Create(){return(ResourceDictionary)XamlReader.Parse(Theme);}
        public static TextBlock Text(string value,double size=13,bool bold=false){return new TextBlock{Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};}
        public static Button Button(string value,Action action=null){var b=new Button{Content=value};if(action!=null)b.Click+=(s,e)=>action();return b;}
        public static Border Card(UIElement child,Thickness? padding=null){return new Border{CornerRadius=new CornerRadius(18),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(234,238,226)),BorderThickness=new Thickness(1),Padding=padding??new Thickness(18),Margin=new Thickness(0,0,0,12),Child=child};}
        public static void SaveRender(FrameworkElement element,string path){CuteUi.SaveRender(element,path);}
    }
    public sealed class BubbleWindow : Window {
        readonly KittenApp app;readonly TextBlock title,body,counter,tag;readonly System.Windows.Shapes.Path arrow;readonly Grid root;readonly WrapPanel reminderActions,summaryActions,completionActions;string reminderKey;NoticeKind noticeKind;
        public BubbleWindow(KittenApp app){
            this.app=app;Title="草鱼的小纸条";Width=284;Height=174;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=app.Qa;Resources=GrassFishUi.Create();
            root=(Grid)XamlReader.Parse(@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
 <Border Margin='11' CornerRadius='21' Background='#FFFDF7' BorderBrush='#DCE7D4' BorderThickness='1' Padding='17,14'><Grid>
  <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
  <Border HorizontalAlignment='Left' Background='#EAF1DF' CornerRadius='9' Padding='9,4'><TextBlock Name='Tag' Text='草鱼 · 生活小提醒' FontSize='10' Foreground='#6C865C'/></Border>
  <TextBlock Name='Title' Grid.Row='1' FontSize='16' FontWeight='SemiBold' Margin='0,10,0,0' TextTrimming='CharacterEllipsis'/>
  <ScrollViewer Name='BodyScroll' Grid.Row='2' VerticalScrollBarVisibility='Auto' Margin='0,7,0,12'><TextBlock Name='Body' FontSize='12' TextWrapping='Wrap' LineHeight='20' Foreground='#6A796C'/></ScrollViewer>
  <WrapPanel Name='ReminderActions' Grid.Row='3' Margin='0,0,0,8' Visibility='Collapsed'>
   <Button Name='DoneReminder' Content='完成' FontSize='12' FontWeight='SemiBold' Padding='12,9' Margin='0,0,6,0' Background='#72947B' Foreground='White'/>
   <Button Name='SnoozeReminder' Content='稍后提醒 ▾' FontSize='12' Padding='10,9' Margin='0,0,6,0' Background='#E7EEE1' Foreground='#526D56'/>
   <Button Name='SkipReminder' Content='跳过' FontSize='12' Padding='12,9' Background='#F0EEE4' Foreground='#76816E'/>
  </WrapPanel>
  <WrapPanel Name='SummaryActions' Grid.Row='3' Margin='0,0,0,8' Visibility='Collapsed'>
   <Button Name='ShowSummary' Content='查看小事' FontSize='11' Padding='10,8' Margin='0,0,5,0' Background='#72947B' Foreground='White'/>
   <Button Name='SnoozeSummary' Content='稍后10分' FontSize='11' Padding='10,8' Margin='0,0,5,0'/>
   <Button Name='SkipSummary' Content='这次跳过' FontSize='11' Padding='10,8' Background='#F0EEE4' Foreground='#76816E'/>
  </WrapPanel>
  <WrapPanel Name='CompletionActions' Grid.Row='3' Margin='0,0,0,4' Visibility='Collapsed'><Button Name='Dismiss' Content='知道啦' FontSize='12' Padding='14,8' ToolTip='收好这条任务完成提示'/></WrapPanel>
  <TextBlock Name='Counter' Grid.Row='4' FontSize='10' Foreground='#97A087'/>
 </Grid></Border><Path Name='Arrow' Fill='#FFFDF7' Stroke='#DCE7D4' StrokeThickness='1'/>
</Grid>");
            Content=root;title=(TextBlock)root.FindName("Title");body=(TextBlock)root.FindName("Body");counter=(TextBlock)root.FindName("Counter");tag=(TextBlock)root.FindName("Tag");arrow=(System.Windows.Shapes.Path)root.FindName("Arrow");
            reminderActions=(WrapPanel)root.FindName("ReminderActions");
            summaryActions=(WrapPanel)root.FindName("SummaryActions");
            completionActions=(WrapPanel)root.FindName("CompletionActions");
            ((Button)root.FindName("ShowSummary")).Click+=(s,e)=>app.OpenToday();((Button)root.FindName("SnoozeSummary")).Click+=(s,e)=>app.SnoozeReminderSummary();
            ((Button)root.FindName("SkipSummary")).Click+=(s,e)=>{if(noticeKind==NoticeKind.Summary)app.SkipReminderSummary();};
            ((Button)root.FindName("Dismiss")).Click+=(s,e)=>{if(noticeKind==NoticeKind.Completion)app.DismissCompletion();};
            ((Button)root.FindName("DoneReminder")).Click+=(s,e)=>{if(reminderKey!=null)app.ResolveReminder(reminderKey,"done");};
            ((Button)root.FindName("SkipReminder")).Click+=(s,e)=>{if(reminderKey!=null)app.ResolveReminder(reminderKey,"skipped");};
            ((Button)root.FindName("SnoozeReminder")).Click+=(s,e)=>{
                if(reminderKey!=null)SnoozeDialog.Choose(app,reminderKey,(Button)s);
            };SourceInitialized+=(s,e)=>WindowNative.NoActivate(this,!app.Qa);
        }
        string raisedImportantKey,displayedNoticeKey;
        public void Refresh(){
            if(!app.Pet.IsVisible||!PrepareContent()){reminderKey=null;raisedImportantKey=null;Hide();return;}
            Position(app.Pet);if(!IsVisible)Show();app.PositionBubble();
            var current=app.Scheduler.Current(DateTime.Now,DateTime.UtcNow);var important=noticeKind==NoticeKind.Reminder&&current!=null&&current.AllowDuringQuiet?current.Key:null;
            if(important!=null&&important!=raisedImportantKey)WindowNative.RaiseWithoutActivation(this);raisedImportantKey=important;
        }
        public bool PrepareContent(){
            var notice=NoticePresentation.Select(app.Scheduler,app.Completion,DateTime.Now,DateTime.UtcNow,app.Config);noticeKind=notice.Kind;reminderKey=null;
            string nextKey=noticeKind+":"+(notice.Reminder==null?"":notice.Reminder.Key);
            if(nextKey!=displayedNoticeKey){var menu=((Button)root.FindName("SnoozeReminder")).ContextMenu;if(menu!=null)menu.IsOpen=false;displayedNoticeKey=nextKey;}
            reminderActions.Visibility=noticeKind==NoticeKind.Reminder?Visibility.Visible:Visibility.Collapsed;
            summaryActions.Visibility=noticeKind==NoticeKind.Summary?Visibility.Visible:Visibility.Collapsed;
            completionActions.Visibility=noticeKind==NoticeKind.Completion?Visibility.Visible:Visibility.Collapsed;
            counter.Text="";counter.Visibility=Visibility.Collapsed;
            if(noticeKind==NoticeKind.None)return false;
            if(noticeKind==NoticeKind.Completion){title.Text="做好啦，快来验收喵";body.Text="喵喵喵";tag.Text="草鱼 · 任务完成";}
            else if(noticeKind==NoticeKind.Summary){
                var items=app.Scheduler.SummaryItems(DateTime.UtcNow);title.Text="安静时间结束啦";tag.Text="草鱼 · 小事汇总";body.Text="有 "+items.Count+" 件小事等你："+string.Join("、",items.Take(3).Select(x=>x.Title))+(items.Count>3?"…":"。");
            }else{
                var r=notice.Reminder;reminderKey=r.Key;title.Text=r.Kind=="water"||r.Kind=="meal"?ReminderWorkflow.Title(r.Kind):r.Title;body.Text=r.Message;
                tag.Text=r.AllowDuringQuiet?"草鱼 · 重要提醒":r.Kind=="custom"?"草鱼 · 约定的小事":r.Kind=="water"?"草鱼 · 补充一点水分":"草鱼 · 好好吃饭";
                int waiting=app.Config.Pending.Count(x=>x.Key!=r.Key&&x.SnoozeUntilUtc<=DateTime.UtcNow);counter.Text=waiting>0?"还有 "+waiting+" 件小事待处理":"";counter.Visibility=waiting>0?Visibility.Visible:Visibility.Collapsed;
            }
            return true;
        }
        public void Position(PetWindow pet){
            var visual=new Rect(pet.Left,pet.Top,pet.Width,pet.Height);if(app.Badge!=null&&app.Badge.IsVisible&&!app.Badge.BadgeBounds.IsEmpty){visual=Rect.Union(visual,app.Badge.BadgeBounds);if(!app.Badge.TrayBounds.IsEmpty)visual=Rect.Union(visual,app.Badge.TrayBounds);}var area=WindowNative.WorkArea(pet,new Point(pet.CatBounds.Left+pet.CatBounds.Width/2,pet.CatBounds.Top+pet.CatBounds.Height/2));var pose=PopupPlacement.Place(visual,new Size(284,noticeKind==NoticeKind.Completion?180:222),area);
            Width=pose.Bounds.Width;Height=pose.Bounds.Height;Left=pose.Bounds.Left;Top=pose.Bounds.Top;
            double ax=Math.Max(35,Math.Min(Width-35,pose.Anchor.X-Left)),ay=Math.Max(35,Math.Min(Height-35,pose.Anchor.Y-Top));string geom;
            if(pose.Side=="bottom")geom=string.Format(System.Globalization.CultureInfo.InvariantCulture,"M {0},{1} L {2},{3} L {4},{1}",ax-9,Height-11,ax,Height-1,ax+9);
            else if(pose.Side=="top")geom=string.Format(System.Globalization.CultureInfo.InvariantCulture,"M {0},11 L {1},1 L {2},11",ax-9,ax,ax+9);
            else if(pose.Side=="left")geom=string.Format(System.Globalization.CultureInfo.InvariantCulture,"M 11,{0} L 1,{1} L 11,{2}",ay-9,ay,ay+9);
            else geom=string.Format(System.Globalization.CultureInfo.InvariantCulture,"M {0},{1} L {2},{3} L {0},{4}",Width-11,ay-9,Width-1,ay,ay+9);
            arrow.Data=Geometry.Parse(geom);
        }
    }
}
