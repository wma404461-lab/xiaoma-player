using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

static class UpdateTransfer {
 public static bool Cached(string path,long size,string sha){try{return new FileInfo(path).Length==size&&String.Equals(UpdateResources.Hash(path),sha,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 public static async Task<long> WriteVerified(Stream input,string partial,long expected,string sha,Action<long> progress,CancellationToken cancellation,Action abort){
  long total=0;try{
   using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.SequentialScan))using(var digest=SHA256.Create()){
    byte[] buffer=new byte[65536];while(true){cancellation.ThrowIfCancellationRequested();var readTask=input.ReadAsync(buffer,0,buffer.Length,cancellation);int read;
     using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
      var delay=Task.Delay(30000,timeout.Token);if(await Task.WhenAny(readTask,delay)!=readTask){abort();cancellation.ThrowIfCancellationRequested();throw new IOException("更新下载长时间无响应，请重试");}timeout.Cancel();read=await readTask;
     }
     if(read==0)break;total+=read;if(total>expected)throw new IOException("更新包超过签名清单大小");digest.TransformBlock(buffer,0,read,null,0);await output.WriteAsync(buffer,0,read,cancellation);progress(total);
    }
    digest.TransformFinalBlock(new byte[0],0,0);string actual=BitConverter.ToString(digest.Hash).Replace("-","").ToLowerInvariant();if(total!=expected||!String.Equals(actual,sha,StringComparison.OrdinalIgnoreCase))throw new IOException("更新包校验失败，请重新下载");await output.FlushAsync(cancellation);output.Flush(true);
   }return total;
  }catch{try{File.Delete(partial);}catch{}throw;}
 }
}
