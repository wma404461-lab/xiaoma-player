using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;

static class NativeShell {
 [DllImport("ole32.dll")]static extern int CoInitializeEx(IntPtr reserved,uint mode);
 [DllImport("ole32.dll")]static extern void CoUninitialize();
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHParseDisplayName(string path,IntPtr bind,out IntPtr item,uint mask,out uint attributes);
 [DllImport("shell32.dll")]static extern int SHOpenFolderAndSelectItems(IntPtr item,uint count,IntPtr children,uint flags);
 public static Task<object> Reveal(string file,bool testing){
  string path=Path.GetFullPath(file);bool directory=Directory.Exists(path);if(!File.Exists(path)&&!directory)throw new Exception("File has been moved or deleted");
  var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{
   int initialized=CoInitializeEx(IntPtr.Zero,2);IntPtr item=IntPtr.Zero;
   try{uint attributes;Marshal.ThrowExceptionForHR(SHParseDisplayName(path,IntPtr.Zero,out item,0,out attributes));if(!testing){if(directory)Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true,Verb="open"});else Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item,0,IntPtr.Zero,0));}completion.TrySetResult(new{path=path,folder=directory?path:Path.GetDirectoryName(path),selected=!directory});}
   catch(Exception error){completion.TrySetException(new Exception("File not found: "+error.Message));}
   finally{if(item!=IntPtr.Zero)Marshal.FreeCoTaskMem(item);if(initialized>=0)CoUninitialize();}
  });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
 }
}
