using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

class ExportJob {
 public string id,output,temporary,directory,encoder,error="",fallbackReason;public List<Frame> frames;public double fps;public double? targetMB;public long bitrate;public int passes,pass,count,width,height;public bool native,cancelled;public Process child;public Task<object> work;public SemaphoreSlim writing=new SemaphoreSlim(1);
}
class NativeExporter : IDisposable {
 readonly string encoder,folder;readonly Func<string,string> urlFor;readonly Action<object> progress;readonly Dictionary<string,ExportJob> jobs=new Dictionary<string,ExportJob>();readonly Lazy<Task<string>> gpu;
 public NativeExporter(string appRoot,string profile,Func<string,string> url,Action<object> notify){encoder=Path.Combine(appRoot,"build","encoder","ffmpeg.exe");folder=Path.Combine(profile,"exports");urlFor=url;progress=notify;gpu=new Lazy<Task<string>>(()=>Task.Run((Func<Task<string>>)Probe));}
 public static string Quote(string value){return "\""+value.Replace("\\\"","\\\\\"").Replace("\"","\\\"")+"\"";}
 public bool RecordingConflict(){lock(jobs)return jobs.Count>0;}
 public Task<string> SelectCaptureEncoder(){return gpu.Value;}
 Process Spawn(IEnumerable<string> args){var p=new Process{StartInfo=new ProcessStartInfo(NativeEncoder.Ensure(encoder),String.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};p.Start();return p;}
 async Task<string> Probe(){foreach(string name in new[]{"h264_nvenc","h264_qsv","h264_amf"})try{using(var p=Spawn(new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i","color=s=320x180:r=24","-frames:v","1","-c:v",name,"-f","null","NUL"})){p.StandardInput.Close();var read=p.StandardError.ReadToEndAsync();var drain=p.StandardOutput.ReadToEndAsync();bool success=await Task.Run(()=>p.WaitForExit(8000));if(!success){p.Kill();continue;}await read;await drain;if(p.ExitCode==0)return name;}}catch{}return null;}
 public async Task<object> Status(){return new{encoder=await gpu.Value,hardware=(await gpu.Value)!=null};}
 static int BE(byte[] data,int offset){return data[offset]<<24|data[offset+1]<<16|data[offset+2]<<8|data[offset+3];}
 static bool SafePNG(string file,out int width,out int height){width=height=0;try{using(var stream=File.OpenRead(file)){byte[] header=new byte[33];if(stream.Read(header,0,33)!=33||BE(header,0)!=unchecked((int)0x89504e47)||BE(header,4)!=0x0d0a1a0a)return false;width=BE(header,16);height=BE(header,20);if(width<=0||height<=0||(long)width*height>100000000||header[24]!=8||(header[25]!=2&&header[25]!=6))return false;int examined=33;while(examined<1024*1024){byte[] chunk=new byte[8];if(stream.Read(chunk,0,8)!=8)return false;int length=BE(chunk,0);string kind=Encoding.ASCII.GetString(chunk,4,4);if(kind=="IDAT")return true;if(new[]{"iCCP","eXIf","acTL"}.Contains(kind)||length<0||stream.Position+length+4>stream.Length)return false;stream.Position+=length+4;examined+=length+12;}}}catch{}return false;}
 public async Task<object> Start(Item item,string output,Dictionary<string,object> options){
  lock(jobs)if(jobs.Count>0)throw new Exception("已有序列正在合成");double fps=Convert.ToDouble(options["fps"]);int start=Convert.ToInt32(options["start"]),end=Convert.ToInt32(options["end"]);double? target=options.ContainsKey("targetMB")&&options["targetMB"]!=null?(double?)Convert.ToDouble(options["targetMB"]):null;
  if(fps<1||fps>240||start<0||end<start||end>=item.span||Path.GetExtension(output).ToLowerInvariant()!=".mp4"||target<.1||target>100000)throw new Exception("合成参数不正确");
  string id=Guid.NewGuid().ToString("N");var frames=new List<Frame>();int source=0;for(int index=start;index<=end;index++){long number=item.first+index;while(source+1<item.frames.Count&&item.frames[source+1].number<=number)source++;frames.Add(item.frames[source]);}
  long bitrate=target.HasValue?(long)Math.Floor((target.Value*1024*1024*.95-Math.Max(16384,frames.Count*64))*8/(frames.Count/fps)):0;if(target.HasValue&&bitrate<8000)throw new Exception("这段序列较长，目标大小太小，请调大 MB 数值");
  bool direct=item.extension==".png";int w=0,h=0;foreach(var frame in frames.GroupBy(f=>f.path).Select(g=>g.First())){int width,height;if(!direct||!SafePNG(frame.path,out width,out height)){direct=false;break;}if(w!=0&&(width!=w||height!=h))throw new Exception("序列里的图片尺寸不一致");w=width;h=height;}
  string hardware=direct&&target.HasValue&&w<=4096&&h<=4096?await gpu.Value:null;
  var job=new ExportJob{id=id,output=output,temporary=Path.Combine(Path.GetDirectoryName(output),".xiaoma-"+id+".mp4"),directory=Path.Combine(folder,id),frames=frames,fps=fps,targetMB=target,bitrate=bitrate,passes=hardware!=null?1:target.HasValue?2:1,pass=1,width=w,height=h,native=direct,encoder=hardware??"libx264"};Directory.CreateDirectory(job.directory);lock(jobs)jobs[id]=job;
  if(direct)job.work=Task.Run(()=>RunDirect(job));else StartPass(job);
  return new{id=id,frames=frames.Select(f=>new{url=urlFor(f.path),ext=f.ext}).ToArray(),fps=fps,total=frames.Count,passes=job.passes,targetMB=target,native=direct,encoder=job.encoder};
 }
 IEnumerable<string> Args(ExportJob j){bool limited=j.targetMB.HasValue,hardware=j.encoder!="libx264";var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-y","-f","image2pipe","-framerate",j.fps.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i","pipe:0","-an","-c:v",j.encoder};
  if(hardware){if(j.encoder=="h264_nvenc")args.AddRange(new[]{"-preset","p5","-rc","cbr","-multipass","fullres","-spatial-aq","1","-temporal-aq","1","-rc-lookahead","20"});else if(j.encoder=="h264_qsv")args.AddRange(new[]{"-preset","medium"});else args.AddRange(new[]{"-quality","quality"});args.AddRange(new[]{"-b:v",j.bitrate.ToString(),"-maxrate",j.bitrate.ToString(),"-bufsize",j.bitrate.ToString()});}
  else{if(limited)args.AddRange(new[]{"-b:v",j.bitrate.ToString(),"-pass",j.pass.ToString(),"-passlogfile",Path.Combine(j.directory,"stats")});else args.AddRange(new[]{"-crf","0"});args.AddRange(new[]{"-preset","medium","-tune","zerolatency","-refs","1"});}
  string filter=(j.native?"format=rgba,premultiply=inplace=1,format=rgb24,":"")+(limited?"pad=ceil(iw/2)*2:ceil(ih/2)*2:color=black,scale=in_range=pc:out_range=tv:out_color_matrix=bt709":"scale=in_range=pc:out_range=pc:out_color_matrix=bt709");
  args.AddRange(new[]{"-g",Math.Round(j.fps).ToString(),"-vf",filter,"-pix_fmt",limited?"yuv420p":"yuv444p","-colorspace","bt709","-color_primaries","bt709","-color_trc","iec61966-2-1","-color_range",limited?"tv":"pc","-threads","4","-progress","pipe:1","-nostats"});if(!hardware&&limited&&j.pass==1)args.AddRange(new[]{"-f","null","NUL"});else args.AddRange(new[]{"-movflags","+faststart",j.temporary});return args;
 }
 void Notify(ExportJob j,string phase,int done){progress(new{id=j.id,phase=phase,done=done,total=j.frames.Count,pass=j.pass,passes=j.passes,targetMB=j.targetMB,encoder=j.encoder});}
 void StartPass(ExportJob j){if(j.cancelled)throw new Exception("已取消合成");j.count=0;j.error="";j.child=Spawn(Args(j));j.child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)j.error=(j.error+e.Data+"\n");};j.child.OutputDataReceived+=(s,e)=>{int frame;if(e.Data!=null&&e.Data.StartsWith("frame=")&&int.TryParse(e.Data.Substring(6),out frame))Notify(j,"encode",frame);};j.child.BeginErrorReadLine();j.child.BeginOutputReadLine();}
 async Task ClosePass(ExportJob j){if(j.count!=j.frames.Count)throw new Exception("序列没有完整读取");j.child.StandardInput.Close();await Task.Run(()=>j.child.WaitForExit());if(j.cancelled)throw new Exception("已取消合成");if(j.child.ExitCode!=0)throw new Exception("MP4 合成失败："+j.error.Substring(0,Math.Min(600,j.error.Length)));j.child.Dispose();j.child=null;}
 async Task<object> RunDirect(ExportJob j){try{
  try{await FeedAll(j);}catch(Exception error){if(j.cancelled||j.encoder=="libx264")throw;j.fallbackReason=error.Message+" "+j.error;Stop(j);j.encoder="libx264";j.passes=2;j.pass=1;await FeedAll(j);}
  for(int retry=0;j.targetMB.HasValue&&j.encoder!="libx264"&&new FileInfo(j.temporary).Length>j.targetMB.Value*1024*1024&&retry<2;retry++){j.bitrate=(long)(j.bitrate*j.targetMB.Value*1024*1024/new FileInfo(j.temporary).Length*.96);await FeedAll(j);}
  if(j.targetMB.HasValue&&new FileInfo(j.temporary).Length>j.targetMB.Value*1024*1024&&j.encoder!="libx264"){j.fallbackReason="Hardware output exceeded the requested size: "+new FileInfo(j.temporary).Length;j.encoder="libx264";j.passes=2;j.pass=1;await FeedAll(j);}
  return Commit(j);
 }catch{Cleanup(j);throw;}}
 async Task FeedAll(ExportJob j){for(j.pass=1;j.pass<=j.passes;j.pass++){StartPass(j);string previous=null;byte[] bytes=null;foreach(Frame frame in j.frames){if(j.cancelled)throw new Exception("已取消合成");if(previous!=frame.path){bytes=File.ReadAllBytes(frame.path);previous=frame.path;}await j.child.StandardInput.BaseStream.WriteAsync(bytes,0,bytes.Length);j.count++;Notify(j,"read",j.count);}await ClosePass(j);}j.pass=j.passes;}
 ExportJob Get(string id){lock(jobs){ExportJob j;if(!jobs.TryGetValue(id,out j)||j.cancelled)throw new Exception("合成任务已结束");return j;}}
 public async Task Write(string id,int index,Stream data){var j=Get(id);await j.writing.WaitAsync();try{if(j.native||index!=j.count||index>=j.frames.Count)throw new Exception("合成帧数据无效");using(var output=new MemoryStream()){byte[] block=new byte[65536];int read;while((read=await data.ReadAsync(block,0,block.Length))>0){if(output.Length+read>256*1024*1024)throw new Exception("合成图像过大");output.Write(block,0,read);}byte[] bytes=output.ToArray();if(bytes.Length<24||BE(bytes,0)!=unchecked((int)0x89504e47)||BE(bytes,4)!=0x0d0a1a0a)throw new Exception("合成图像无效");int width=BE(bytes,16),height=BE(bytes,20);if(width<=0||height<=0||(long)width*height>100000000||(j.width!=0&&(j.width!=width||j.height!=height)))throw new Exception("序列里的图片尺寸不一致");j.width=width;j.height=height;await j.child.StandardInput.BaseStream.WriteAsync(bytes,0,bytes.Length);j.count++;Notify(j,"read",j.count);}}finally{j.writing.Release();}}
 public async Task<object> Next(string id){var j=Get(id);if(j.native||j.pass>=j.passes)throw new Exception("不需要再次分析");await ClosePass(j);j.pass++;StartPass(j);return new{pass=j.pass};}
 public async Task<object> Finish(string id){var j=Get(id);try{if(j.native)return await j.work;if(j.pass!=j.passes)throw new Exception("视频分析尚未完成");await ClosePass(j);return Commit(j);}catch{Cleanup(j);throw;}finally{lock(jobs)jobs.Remove(id);}}
 object Commit(ExportJob j){long size=new FileInfo(j.temporary).Length;if(j.targetMB.HasValue&&size>j.targetMB.Value*1024*1024*1.03)throw new Exception("目标大小过小，请调大 MB 后重试");if(File.Exists(j.output))File.Replace(j.temporary,j.output,null);else File.Move(j.temporary,j.output);Notify(j,"done",j.frames.Count);Cleanup(j);return new{path=j.output,width=j.targetMB.HasValue?(j.width+1)/2*2:j.width,height=j.targetMB.HasValue?(j.height+1)/2*2:j.height,frames=j.frames.Count,fps=j.fps,size=size,targetMB=j.targetMB,encoder=j.encoder,fallbackReason=j.fallbackReason};}
 void Stop(ExportJob j){try{if(j.child!=null&&!j.child.HasExited)j.child.Kill();j.child.Dispose();}catch{}j.child=null;}
 void Cleanup(ExportJob j){Stop(j);try{File.Delete(j.temporary);foreach(string file in Directory.EnumerateFiles(j.directory,"stats*"))File.Delete(file);Directory.Delete(j.directory);}catch{}}
 public async Task Cancel(string id){ExportJob j;lock(jobs)if(!jobs.TryGetValue(id,out j))return;j.cancelled=true;Stop(j);if(j.work!=null)try{await j.work;}catch{}Cleanup(j);lock(jobs)jobs.Remove(id);}
 public void Dispose(){string[] ids;lock(jobs)ids=jobs.Keys.ToArray();foreach(string id in ids)Cancel(id);}
}
