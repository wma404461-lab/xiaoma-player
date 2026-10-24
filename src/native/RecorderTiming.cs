using System;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Per-thread timers: no system-wide timeBeginPeriod changes and no busy spinning.
sealed class RecorderWait : IDisposable {
 sealed class TimerHandle : WaitHandle {public TimerHandle(IntPtr handle){SafeWaitHandle=new SafeWaitHandle(handle,true);}}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWaitableTimerExW(IntPtr attributes,string name,uint flags,uint access);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr context,bool resume);
 readonly TimerHandle timer;readonly WaitHandle[] handles;readonly WaitHandle[] signals;
 public bool HighResolution{get;private set;}
 public RecorderWait(params WaitHandle[] interrupts){signals=interrupts;IntPtr handle=CreateWaitableTimerExW(IntPtr.Zero,null,2,0x1F0003);HighResolution=handle!=IntPtr.Zero;if(handle==IntPtr.Zero)handle=CreateWaitableTimerExW(IntPtr.Zero,null,0,0x1F0003);if(handle!=IntPtr.Zero){timer=new TimerHandle(handle);handles=new WaitHandle[interrupts.Length+1];Array.Copy(interrupts,handles,interrupts.Length);handles[interrupts.Length]=timer;}}
 public void Wait(double milliseconds){if(milliseconds<=0)return;if(timer==null){WaitHandle.WaitAny(signals,Math.Max(1,(int)Math.Ceiling(milliseconds)));return;}long due=-Math.Max(1,(long)Math.Ceiling(milliseconds*10000));if(!SetWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle(),ref due,0,IntPtr.Zero,IntPtr.Zero,false)){WaitHandle.WaitAny(signals,Math.Max(1,(int)Math.Ceiling(milliseconds)));return;}WaitHandle.WaitAny(handles);}
 public void Dispose(){if(timer!=null)timer.Dispose();}
}
sealed class RecorderThread : IDisposable {
 [DllImport("avrt.dll",CharSet=CharSet.Unicode)]static extern IntPtr AvSetMmThreadCharacteristicsW(string name,ref uint index);
 [DllImport("avrt.dll")]static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
 readonly IntPtr handle;
 public RecorderThread(string task){uint index=0;try{handle=AvSetMmThreadCharacteristicsW(task,ref index);}catch{}}
 public void Dispose(){if(handle!=IntPtr.Zero)AvRevertMmThreadCharacteristics(handle);}
}
sealed class RecorderMetric {
 readonly long[] bins=new long[1001];long count;double total,max;
 public void Add(double milliseconds){milliseconds=Math.Max(0,milliseconds);bins[Math.Min(1000,(int)Math.Ceiling(milliseconds*10))]++;count++;total+=milliseconds;max=Math.Max(max,milliseconds);}
 double Percentile(double percentile){if(count==0)return 0;long target=(long)Math.Ceiling(count*percentile),sum=0;for(int i=0;i<bins.Length;i++){sum+=bins[i];if(sum>=target)return i/10d;}return max;}
 public object Snapshot(){return new{samples=count,averageMs=Math.Round(count>0?total/count:0,4),p50Ms=Math.Round(Math.Min(max,Percentile(.5)),4),p95Ms=Math.Round(Math.Min(max,Percentile(.95)),4),maxMs=Math.Round(max,4)};}
}
