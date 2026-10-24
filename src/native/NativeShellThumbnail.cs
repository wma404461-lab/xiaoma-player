using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;

// Populate the Windows thumbnail cache as well as the player's independent preview cache.
static class NativeShellThumbnail {
 [ComImport,Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IImageFactory{[PreserveSig]int GetImage(Size size,uint flags,out IntPtr bitmap);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IImageFactory item);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="SHChangeNotify")]static extern void Notify(uint events,uint flags,string item,IntPtr other);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr bitmap);
 public static void Warm(string[] paths){var files=paths.Where(p=>File.Exists(p)&&new[]{".mp4",".mov",".m4v",".mkv",".webm",".avi",".mpg",".mpeg"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Take(32).ToArray();if(files.Length==0)return;var thread=new Thread(()=>{foreach(var file in files){IImageFactory item=null;IntPtr bitmap=IntPtr.Zero;try{Guid iid=typeof(IImageFactory).GUID;if(SHCreateItemFromParsingName(file,IntPtr.Zero,ref iid,out item)<0)continue;if(item.GetImage(new Size(256,256),8,out bitmap)>=0)Notify(0x2000,0x1005,file,IntPtr.Zero);}catch{}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(item!=null)Marshal.ReleaseComObject(item);}}}){IsBackground=true,Priority=ThreadPriority.BelowNormal};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
}
