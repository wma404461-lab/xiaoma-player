using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Overlay geometry uses physical pixels and is not capped to the primary monitor.
class RecorderOverlayForm : Form {
 Rectangle desired;
 [StructLayout(LayoutKind.Sequential)]struct WindowPosition{public IntPtr window,after;public int x,y,width,height;public uint flags;}
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var value=base.CreateParams;if(!desired.IsEmpty){value.X=desired.X;value.Y=desired.Y;value.Width=desired.Width;value.Height=desired.Height;}return value;}}
 public void Place(Rectangle bounds){desired=bounds;if(IsHandleCreated)SetWindowPos(Handle,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x14);else Bounds=bounds;}
 protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0x46&&!desired.IsEmpty){var position=(WindowPosition)Marshal.PtrToStructure(message.LParam,typeof(WindowPosition));if((position.flags&2)==0){position.x=desired.X;position.y=desired.Y;}if((position.flags&1)==0){position.width=desired.Width;position.height=desired.Height;}Marshal.StructureToPtr(position,message.LParam,false);}}
}
