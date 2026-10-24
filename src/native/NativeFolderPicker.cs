using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Windows Common Item Dialog, with its editable address bar and folder-search UI.
static class NativeFolderPicker {
 const uint PickFolders=0x20,ForceFileSystem=0x40,PathMustExist=0x800,NoChangeDirectory=0x8;
 const int Cancelled=unchecked((int)0x800704c7);
 static readonly Guid ShellItemId=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
 static extern int SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);

 public static string Show(IWin32Window owner,string title,string initialFolder=null){
  var dialog=(IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
  IShellItem initial=null,result=null;IntPtr displayName=IntPtr.Zero;
  try{
   uint options;dialog.GetOptions(out options);dialog.SetOptions(options|PickFolders|ForceFileSystem|PathMustExist|NoChangeDirectory);
   dialog.SetTitle(title);dialog.SetOkButtonLabel("Choose folder");
   string folder=ExistingFolder(initialFolder);
   if(folder!=null){Guid iid=ShellItemId;int create=SHCreateItemFromParsingName(folder,IntPtr.Zero,ref iid,out initial);if(create>=0)dialog.SetFolder(initial);}
   int status=dialog.Show(owner==null?IntPtr.Zero:owner.Handle);if(status==Cancelled)return null;Marshal.ThrowExceptionForHR(status);
   dialog.GetResult(out result);result.GetDisplayName(0x80058000,out displayName);
   string selected=Marshal.PtrToStringUni(displayName);if(String.IsNullOrWhiteSpace(selected)||!Directory.Exists(selected))throw new IOException("Please select an existing folder");return Path.GetFullPath(selected);
  }finally{
   if(displayName!=IntPtr.Zero)Marshal.FreeCoTaskMem(displayName);
   if(result!=null)Marshal.FinalReleaseComObject(result);if(initial!=null)Marshal.FinalReleaseComObject(initial);Marshal.FinalReleaseComObject(dialog);
  }
 }
 static string ExistingFolder(string path){
  if(String.IsNullOrWhiteSpace(path))return null;
  try{path=Path.GetFullPath(path);while(!Directory.Exists(path)){string parent=Path.GetDirectoryName(path);if(String.IsNullOrEmpty(parent)||parent==path)return null;path=parent;}return path;}catch{return null;}
 }
 [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IFileDialog {
  [PreserveSig]int Show(IntPtr owner);
  void SetFileTypes(uint count,IntPtr filters);
  void SetFileTypeIndex(uint index);
  void GetFileTypeIndex(out uint index);
  void Advise(IntPtr events,out uint cookie);
  void Unadvise(uint cookie);
  void SetOptions(uint options);
  void GetOptions(out uint options);
  void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)]IShellItem folder);
  void SetFolder([MarshalAs(UnmanagedType.Interface)]IShellItem folder);
  void GetFolder([MarshalAs(UnmanagedType.Interface)]out IShellItem folder);
  void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)]out IShellItem item);
  void SetFileName([MarshalAs(UnmanagedType.LPWStr)]string name);
  void GetFileName(out IntPtr name);
  void SetTitle([MarshalAs(UnmanagedType.LPWStr)]string title);
  void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)]string label);
  void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)]string label);
  void GetResult([MarshalAs(UnmanagedType.Interface)]out IShellItem result);
  void AddPlace([MarshalAs(UnmanagedType.Interface)]IShellItem item,int alignment);
  void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)]string extension);
  void Close(int result);
  void SetClientGuid(ref Guid guid);
  void ClearClientData();
  void SetFilter(IntPtr filter);
 }
 [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellItem {
  void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr value);
  void GetParent([MarshalAs(UnmanagedType.Interface)]out IShellItem parent);
  void GetDisplayName(uint name,out IntPtr value);
  void GetAttributes(uint mask,out uint value);
  void Compare([MarshalAs(UnmanagedType.Interface)]IShellItem other,uint hint,out int order);
 }
}
