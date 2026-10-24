using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class NativeWindowLayout {
 [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
 public static void EnableDpi(){try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){SetProcessDPIAware();}}
 public static Rectangle Preferred(Rectangle work){int width=Math.Min(work.Width,Math.Max(Math.Min(680,work.Width),(int)Math.Round(work.Width*.9))),height=Math.Min(work.Height,Math.Max(Math.Min(480,work.Height),(int)Math.Round(work.Height*.9)));return new Rectangle(work.Left+(work.Width-width)/2,work.Top+(work.Height-height)/2,width,height);}
 public static Rectangle Current(){return Preferred(Screen.FromPoint(Cursor.Position).WorkingArea);}
 public static double UiZoom(Rectangle screen,double browserScale){double desired=Math.Max(1,Math.Min(2,Math.Min(screen.Width/1920d,screen.Height/1080d)));return Math.Max(.5,Math.Min(2,desired/Math.Max(1,browserScale)));}
}
