using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class NativeMain {
 IntPtr fastHomeWindow;bool fastHomeDormant;Timer fastHomeWatch;
 const uint HomeHandoff=0x8031,HomeBackendClosed=0x8032;
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int size);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out HomeBounds bounds);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [StructLayout(LayoutKind.Sequential)] struct HomeBounds{public int left,top,right,bottom;}
 sealed class HomeOwner:IWin32Window{readonly IntPtr window;public HomeOwner(IntPtr value){window=value;}public IntPtr Handle{get{return window;}}}
 bool ValidFastHome(IntPtr window){
  if(window==IntPtr.Zero||!IsWindow(window))return false;var name=new StringBuilder(180);GetClassName(window,name,name.Capacity);if(!name.ToString().StartsWith("XiaomaFastHome-",StringComparison.Ordinal))return false;
  uint pid;GetWindowThreadProcessId(window,out pid);try{string exe=Process.GetProcessById((int)pid).MainModule.FileName;return new[]{"Xiaoma Player.exe","Xiaoma Player + Recorder.exe"}.Any(file=>String.Equals(exe,Path.Combine(root,file),StringComparison.OrdinalIgnoreCase));}catch{return false;}
 }
 void ConfigureFastHome(string[] args){
  string flag=args.FirstOrDefault(a=>a.StartsWith("--home-window="));long value;if(flag!=null&&long.TryParse(flag.Substring(14),out value)){fastHomeWindow=new IntPtr(value);fastHomeDormant=true;}
 }
 void WatchFastHome(){
  if(!fastHomeDormant)return;fastHomeWatch=new Timer{Interval=250};fastHomeWatch.Tick+=(s,e)=>{if(fastHomeDormant&&!ValidFastHome(fastHomeWindow))Close();};fastHomeWatch.Start();
  FormClosed+=(s,e)=>{if(fastHomeWatch!=null){fastHomeWatch.Stop();fastHomeWatch.Dispose();}if(ValidFastHome(fastHomeWindow))PostMessage(fastHomeWindow,HomeBackendClosed,IntPtr.Zero,IntPtr.Zero);};
 }
 IWin32Window HomeDialogOwner(){return fastHomeDormant&&ValidFastHome(fastHomeWindow)?(IWin32Window)new HomeOwner(fastHomeWindow):this;}
 void RevealFromFastHome(){
  if(!fastHomeDormant)return;HomeBounds area;bool maximized=ValidFastHome(fastHomeWindow)&&IsZoomed(fastHomeWindow);if(ValidFastHome(fastHomeWindow)&&GetWindowRect(fastHomeWindow,out area)){WindowState=FormWindowState.Normal;Bounds=new Rectangle(area.left,area.top,area.right-area.left,area.bottom-area.top);}else{Location=new Point((Screen.PrimaryScreen.WorkingArea.Width-Width)/2,(Screen.PrimaryScreen.WorkingArea.Height-Height)/2);}
  ApplyUiScale();ShowInTaskbar=!headless;fastHomeDormant=false;if(fastHomeWatch!=null)fastHomeWatch.Stop();if(maximized)WindowState=FormWindowState.Maximized;if(!headless&&!testing)Activate();
  if(ValidFastHome(fastHomeWindow))PostMessage(fastHomeWindow,HomeHandoff,IntPtr.Zero,IntPtr.Zero);fastHomeWindow=IntPtr.Zero;
 }
 bool HandleFastHomeArguments(string[] args){
  string windowArg=args.FirstOrDefault(a=>a.StartsWith("--home-window="));long value;
  if(windowArg!=null&&long.TryParse(windowArg.Substring(14),out value)&&ValidFastHome(new IntPtr(value))){
   var incoming=new IntPtr(value);if(ready&&!fastHomeDormant&&(home==null||!home.Visible)){PostMessage(incoming,HomeHandoff,IntPtr.Zero,IntPtr.Zero);if(!headless&&!testing)Activate();}else fastHomeWindow=incoming;
  }
  if(args.Contains("--home-close")){if(fastHomeDormant)Close();return true;}
  string action=args.FirstOrDefault(a=>a.StartsWith("--home-action="));if(action==null)return false;action=action.Substring(14);
  if(action=="files"||action=="folder")HomeChoose(action=="folder");else if(action=="update"||action=="record"){homeAction=action;EnsureViewer();}return true;
 }
}
