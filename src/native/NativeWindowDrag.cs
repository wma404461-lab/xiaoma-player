using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
partial class NativeMain {
 int dragRequests;
 [DllImport("user32.dll")]static extern bool ReleaseCapture();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
 object DragWindow(){dragRequests++;if(!testing&&!full)BeginInvoke(new Action(()=>{ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}));return true;}
}
