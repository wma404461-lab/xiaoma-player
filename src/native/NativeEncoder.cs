using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// The signed download carries a compact payload; the ordinary executable is
// materialized once, verified and reused. Recording never executes packed code.
static class NativeEncoder {
 public static string Ensure(string executable){if(File.Exists(executable))return executable;string folder=Path.GetDirectoryName(executable),payload=Path.Combine(folder,"ffmpeg.exe.xz"),decoder=Path.Combine(folder,"7za.exe"),manifest=Path.Combine(folder,"ffmpeg-payload.json");if(!File.Exists(payload)||!File.Exists(decoder)||!File.Exists(manifest))throw new Exception("编码器文件缺失，请重新解压软件");using(var mutex=new Mutex(false,"Local\\XiaomaEncoder-"+NativeCatalog.Hash(Path.GetFullPath(folder).ToLowerInvariant()))){bool locked=false;try{try{locked=mutex.WaitOne(90000);}catch(AbandonedMutexException){locked=true;}if(!locked)throw new Exception("编码器仍在准备，请稍后重试");if(File.Exists(executable))return executable;var info=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest));long expected=Convert.ToInt64(info["size"]);string hash=Convert.ToString(info["sha256"]),temporary=Path.Combine(folder,".ffmpeg-"+Guid.NewGuid().ToString("N")+".tmp");try{using(var process=new Process{StartInfo=new ProcessStartInfo(decoder,"x "+NativeExporter.Quote(payload)+" -so -bsp0"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){process.Start();var error=process.StandardError.ReadToEndAsync();using(var target=File.Create(temporary)){byte[] buffer=new byte[65536];long bytes=0;int count;while((count=process.StandardOutput.BaseStream.Read(buffer,0,buffer.Length))>0){bytes+=count;if(bytes>expected){process.Kill();throw new Exception("编码器大小校验失败");}target.Write(buffer,0,count);}}if(!process.WaitForExit(90000)){process.Kill();throw new Exception("编码器准备超时");}error.GetAwaiter().GetResult();if(process.ExitCode!=0)throw new Exception("编码器准备失败，请重新解压软件");}if(new FileInfo(temporary).Length!=expected)throw new Exception("编码器大小校验失败");using(var algorithm=SHA256.Create())using(var source=File.OpenRead(temporary)){string actual=BitConverter.ToString(algorithm.ComputeHash(source)).Replace("-","").ToLowerInvariant();if(actual!=hash)throw new Exception("编码器内容校验失败");}File.Move(temporary,executable);return executable;}finally{try{File.Delete(temporary);}catch{}}}finally{if(locked)mutex.ReleaseMutex();}}}
 public static void Warm(string appRoot){string file=Path.Combine(appRoot,"build","encoder","ffmpeg.exe");if(!File.Exists(file)&&File.Exists(file+".xz"))System.Threading.Tasks.Task.Run(()=>{try{Ensure(file);}catch{}});}
}
