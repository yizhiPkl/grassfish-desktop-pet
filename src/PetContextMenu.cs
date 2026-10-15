using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopKitten {
    // The pet refuses activation normally. Permit it only while its menu is
    // open, so WPF can capture/dismiss outside clicks across windows. No global
    // mouse hook, polling, or background service is required.
    public sealed class PetContextMenu : IDisposable {
        readonly Window owner;readonly ContextMenu menu;readonly Func<IDisposable> activate;
        readonly MouseButtonEventHandler outside;
        IDisposable activation;
        bool disposed;
        public bool Active {get{return activation!=null;}}
        public PetContextMenu(Window owner,ContextMenu menu):this(owner,menu,()=>new MenuActivation(owner)){}
        internal PetContextMenu(Window owner,ContextMenu menu,Func<IDisposable> activate){
            this.owner=owner;this.menu=menu;this.activate=activate;
            menu.StaysOpen=false;
            owner.ContextMenuOpening+=Opening;owner.Deactivated+=Deactivated;owner.IsVisibleChanged+=VisibilityChanged;owner.Closed+=OwnerClosed;
            menu.Opened+=Opened;menu.Closed+=Closed;
            outside=(s,e)=>Dismiss();Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(menu,outside);
            menu.PreviewKeyDown+=KeyDown;
        }
        void Opening(object sender,ContextMenuEventArgs e){if(!e.Handled)Begin();}
        void Opened(object sender,RoutedEventArgs e){Begin();if(menu.IsOpen)menu.Focus();}
        void Closed(object sender,RoutedEventArgs e){End();}
        void Deactivated(object sender,EventArgs e){Dismiss();}
        void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){if(!(bool)e.NewValue)Dismiss();}
        void OwnerClosed(object sender,EventArgs e){Dispose();}
        void KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Dismiss();e.Handled=true;}}
        internal void Begin(){if(!disposed&&activation==null)activation=activate();}
        public void Dismiss(){
            // Closed normally releases the lease synchronously. End is also
            // safe for canceled openings, owner hide and repeated shutdown.
            if(menu.IsOpen)menu.IsOpen=false;
            End();
        }
        void End(){var previous=activation;activation=null;if(previous!=null)previous.Dispose();}
        public void Dispose(){
            if(disposed)return;disposed=true;Dismiss();
            owner.ContextMenuOpening-=Opening;owner.Deactivated-=Deactivated;owner.IsVisibleChanged-=VisibilityChanged;owner.Closed-=OwnerClosed;
            menu.Opened-=Opened;menu.Closed-=Closed;menu.PreviewKeyDown-=KeyDown;Mouse.RemovePreviewMouseDownOutsideCapturedElementHandler(menu,outside);
        }
        sealed class MenuActivation : IDisposable {
            const int NoActivate=0x08000000;
            readonly IntPtr handle;readonly bool restoreNoActivate;
            bool disposed;
            [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
            [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr window,int index,int value);
            [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
            public MenuActivation(Window owner){
                handle=new WindowInteropHelper(owner).Handle;
                if(handle==IntPtr.Zero||!IsWindow(handle))return;
                int style=GetWindowLong(handle,-20);restoreNoActivate=(style&NoActivate)!=0;
                if(restoreNoActivate)SetWindowLong(handle,-20,style&~NoActivate);
                // This runs in response to the user's right-click. Do not
                // restore the old foreground window: a click outside gets to
                // choose its own destination, without this pet stealing focus.
                owner.Activate();
            }
            public void Dispose(){if(disposed)return;disposed=true;if(restoreNoActivate&&handle!=IntPtr.Zero&&IsWindow(handle))SetWindowLong(handle,-20,GetWindowLong(handle,-20)|NoActivate);}
        }
    }
}
