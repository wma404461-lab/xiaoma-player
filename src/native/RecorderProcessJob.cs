using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Closing one recorder job also closes only that worker's encoder children.
sealed class RecorderProcessJob : IDisposable {
 [StructLayout(LayoutKind.Sequential)] struct BasicLimits{public long processTime,jobTime;public uint flags;public UIntPtr minWorkingSet,maxWorkingSet;public uint activeProcesses;public UIntPtr affinity;public uint priority,scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct IoCounters{public ulong reads,writes,other,readBytes,writeBytes,otherBytes;}
 [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits{public BasicLimits basic;public IoCounters io;public UIntPtr processMemory,jobMemory,peakProcessMemory,peakJobMemory;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int kind,ref ExtendedLimits limits,uint size);
 [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 IntPtr handle;
 public static RecorderProcessJob Attach(Process process){IntPtr job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)return null;var limits=new ExtendedLimits{basic=new BasicLimits{flags=0x2000}};if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(ExtendedLimits)))||!AssignProcessToJobObject(job,process.Handle)){CloseHandle(job);return null;}return new RecorderProcessJob{handle=job};}
 public void Dispose(){IntPtr job=System.Threading.Interlocked.Exchange(ref handle,IntPtr.Zero);if(job!=IntPtr.Zero)CloseHandle(job);}
}
