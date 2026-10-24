using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.ComponentModel;

// One top-down 32-bit surface, owned and reused by the capture thread.
sealed class RecorderDib : IDisposable {
 [StructLayout(LayoutKind.Sequential)]struct BitmapInfo {public uint size;public int width,height;public ushort planes,bitCount;public uint compression,sizeImage;public int xPixels,yPixels;public uint used,important;}
 [StructLayout(LayoutKind.Sequential)]struct CursorInfo {public int size,flags;public IntPtr handle;public Point position;}
 [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
 [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr handle);
 [DllImport("gdi32.dll",SetLastError=true)]static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
 [DllImport("gdi32.dll",SetLastError=true)]static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
 [DllImport("gdi32.dll")]static extern bool GdiFlush();
 [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CursorInfo info);
 [DllImport("user32.dll")]static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,int step,IntPtr brush,int flags);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint thread);
 [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")]static extern bool SetThreadDesktop(IntPtr desktop);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
 readonly Rectangle bounds;readonly int bytes;IntPtr screen,dc,bitmap,old,bits,previousDpi,previousDesktop,inputDesktop;
 public RecorderDib(Rectangle region){bounds=region;bytes=checked(region.Width*region.Height*4);previousDpi=SetThreadDpiAwarenessContext(new IntPtr(-4));try{previousDesktop=GetThreadDesktop(GetCurrentThreadId());inputDesktop=OpenInputDesktop(0,false,0x81);if(inputDesktop!=IntPtr.Zero&&!SetThreadDesktop(inputDesktop)){CloseDesktop(inputDesktop);inputDesktop=IntPtr.Zero;}screen=GetDC(IntPtr.Zero);dc=CreateCompatibleDC(screen);var info=new BitmapInfo{size=40,width=region.Width,height=-region.Height,planes=1,bitCount=32,sizeImage=(uint)bytes};bitmap=CreateDIBSection(screen,ref info,0,out bits,IntPtr.Zero,0);if(screen==IntPtr.Zero||dc==IntPtr.Zero||bitmap==IntPtr.Zero||bits==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法准备屏幕采集缓冲区");old=SelectObject(dc,bitmap);}catch{Dispose();throw;}}
 public void Read(byte[] target,bool cursor){if(target.Length!=bytes)throw new ArgumentException("采集缓冲区尺寸不匹配");if(!BitBlt(dc,0,0,bounds.Width,bounds.Height,screen,bounds.Left,bounds.Top,0x00CC0020))throw new Win32Exception(Marshal.GetLastWin32Error(),"屏幕采集失败");if(cursor){var info=new CursorInfo{size=Marshal.SizeOf(typeof(CursorInfo))};if(GetCursorInfo(ref info)&&(info.flags&1)!=0)DrawIconEx(dc,info.position.X-bounds.Left,info.position.Y-bounds.Top,info.handle,0,0,0,IntPtr.Zero,3);}GdiFlush();Marshal.Copy(bits,target,0,bytes);}
 public void Dispose(){if(dc!=IntPtr.Zero&&old!=IntPtr.Zero)SelectObject(dc,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(dc!=IntPtr.Zero)DeleteDC(dc);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);screen=dc=bitmap=bits=old=IntPtr.Zero;if(inputDesktop!=IntPtr.Zero){if(previousDesktop!=IntPtr.Zero)SetThreadDesktop(previousDesktop);CloseDesktop(inputDesktop);inputDesktop=IntPtr.Zero;}if(previousDpi!=IntPtr.Zero){SetThreadDpiAwarenessContext(previousDpi);previousDpi=IntPtr.Zero;}}
}
