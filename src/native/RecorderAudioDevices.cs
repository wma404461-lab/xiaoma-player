using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
static class RecorderAudioDevices {
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class EnumeratorClass{}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IEnumerator {
  [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out ICollection devices);
  [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
  [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IDevice device);
  [PreserveSig]int RegisterEndpointNotificationCallback(IntPtr callback);
  [PreserveSig]int UnregisterEndpointNotificationCallback(IntPtr callback);
 }
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-C0A7B8A32FCD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ICollection {[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IDevice device);}
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDevice {
  [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr activation,out IntPtr result);
  [PreserveSig]int OpenPropertyStore(uint mode,out IStore store);
  [PreserveSig]int GetId(out IntPtr id);
  [PreserveSig]int GetState(out uint state);
 }
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid format;public uint id;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropertyValue{[FieldOffset(0)]public ushort type;[FieldOffset(8)]public IntPtr text;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IStore {
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropertyValue value);[PreserveSig]int SetValue(ref PropertyKey key,ref PropertyValue value);[PreserveSig]int Commit();
 }
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropertyValue value);
 static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 public static object[] List(bool input){ICollection collection=null;IEnumerator enumerator=null;var result=new List<object>();try{enumerator=(IEnumerator)new EnumeratorClass();if(enumerator.EnumAudioEndpoints(input?1:0,1,out collection)<0)return result.ToArray();uint count;if(collection.GetCount(out count)<0)return result.ToArray();var key=new PropertyKey{format=new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),id=14};
  for(uint i=0;i<count;i++){IDevice device=null;IStore store=null;IntPtr raw=IntPtr.Zero;try{if(collection.Item(i,out device)<0||device.GetId(out raw)<0)continue;string id=Marshal.PtrToStringUni(raw),name=id;if(device.OpenPropertyStore(0,out store)>=0){PropertyValue value;if(store.GetValue(ref key,out value)>=0){try{if(value.type==31&&value.text!=IntPtr.Zero)name=Marshal.PtrToStringUni(value.text);}finally{PropVariantClear(ref value);}}}result.Add(new{id=id,name=name});}finally{if(raw!=IntPtr.Zero)Marshal.FreeCoTaskMem(raw);Release(store);Release(device);}}
 }finally{Release(collection);Release(enumerator);}return result.ToArray();}
}
