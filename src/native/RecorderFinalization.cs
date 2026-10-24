using System;
using System.Diagnostics;
using System.Threading.Tasks;

// Every drain, encoder exit and mux shares one finite save budget.
sealed class RecorderFinalization {
 readonly Stopwatch clock=Stopwatch.StartNew();readonly int budget;
 public RecorderFinalization(int milliseconds){budget=Math.Max(1,milliseconds);}
 public int Remaining{get{return Math.Max(1,budget-(int)clock.ElapsedMilliseconds);}}
 public static int BudgetForBytes(long bytes){return (int)Math.Min(600000,30000+Math.Max(0,bytes)/10000000L*1000);}
 public async Task Wait(Task task,int maximum,string error){int timeout=Math.Min(Math.Max(1,maximum),Remaining);if(await Task.WhenAny(task,Task.Delay(timeout))!=task)throw new TimeoutException(error);await task;}
 public async Task Exit(Process process,int maximum,string error){int timeout=Math.Min(Math.Max(1,maximum),Remaining);bool done=await Task.Run(()=>process.WaitForExit(timeout));if(!done){try{process.Kill();}catch{}throw new TimeoutException(error);}}
}
