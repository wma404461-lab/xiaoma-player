using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
sealed class ClipResult {public string path,sourceId;public long size;public double duration;public Item item,source;}
sealed class NativeClipper : IDisposable {
 readonly string ffmpeg;readonly Func<Task<string>> encoder;readonly Action<object> progress;Process child;bool cancelled;
 public bool Busy{get;private set;}
 public NativeClipper(string appRoot,Func<Task<string>> selectEncoder,Action<object> report){ffmpeg=Path.Combine(appRoot,"build","encoder","ffmpeg.exe");encoder=selectEncoder;progress=report;}
 public static string NextOutput(string source){string stem=Path.Combine(Path.GetDirectoryName(source),Path.GetFileNameWithoutExtension(source)+"_Trim"),output=stem+".mp4";for(int n=2;File.Exists(output)||Directory.Exists(output);n++)output=stem+"_"+n+".mp4";return output;}
 public async Task<ClipResult> Trim(Item item,string output,Dictionary<string,object> options){
  if(Busy)throw new Exception("Another fragment is being saved, please wait");if(item.kind!="video")throw new Exception("Please open the video to be edited first");double start=Convert.ToDouble(options["start"]),end=Convert.ToDouble(options["end"]),duration=Convert.ToDouble(options["duration"]);if(double.IsNaN(start)||double.IsInfinity(start)||double.IsNaN(end)||double.IsInfinity(end)||start<0||end<=start||end>duration+.02||duration>7*86400)throw new Exception("Editing range is incorrect");if(Path.GetFullPath(item.path).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new Exception("Please save as a new file; original recording will be retained");
  Busy=true;cancelled=false;string temporary=Path.Combine(Path.GetDirectoryName(output),".xiaoma-clip-"+Guid.NewGuid().ToString("N")+".mp4");
  try{string selected=await encoder()??"libx264";try{await Encode(item.path,temporary,start,end-start,selected);}catch{if(cancelled||selected=="libx264")throw;File.Delete(temporary);await Encode(item.path,temporary,start,end-start,"libx264");}
   if(cancelled)throw new Exception("Editing canceled, original recording retained");if(!File.Exists(temporary)||new FileInfo(temporary).Length<1000)throw new Exception("Editing file failed");for(int attempt=0;;attempt++){try{File.Move(temporary,output);break;}catch(IOException){if(!(File.Exists(output)||Directory.Exists(output))||attempt>=99)throw;output=NextOutput(item.path);}}progress(new{percent=100});return new ClipResult{path=output,size=new FileInfo(output).Length,duration=end-start,sourceId=item.id,source=item};
  }finally{Busy=false;child=null;try{File.Delete(temporary);}catch{}}
 }
 async Task Encode(string input,string output,double start,double seconds,string selected){
  string N(double value){return value.ToString("0.########",CultureInfo.InvariantCulture);}
  var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-y","-ss",N(start),"-i",input,"-t",N(seconds),"-map","0:v:0","-map","0:a?","-vf","setpts=PTS-STARTPTS,scale=ceil(iw/2)*2:ceil(ih/2)*2","-af","asetpts=PTS-STARTPTS","-c:v",selected};
  if(selected=="h264_nvenc")args.AddRange(new[]{"-preset","p4","-cq","18","-rc","vbr","-b:v","0"});else if(selected=="h264_qsv")args.AddRange(new[]{"-global_quality","18"});else if(selected=="h264_amf")args.AddRange(new[]{"-quality","speed","-qp_i","18","-qp_p","18"});else args.AddRange(new[]{"-preset","veryfast","-crf","18"});args.AddRange(new[]{"-pix_fmt","yuv420p","-c:a","aac","-b:a","192k","-movflags","+faststart","-progress","pipe:1","-stats_period","0.2",output});
  using(var process=new Process{StartInfo=new ProcessStartInfo(NativeEncoder.Ensure(ffmpeg),string.Join(" ",args.Select(NativeExporter.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){child=process;process.Start();var error=process.StandardError.ReadToEndAsync();string line;while((line=await process.StandardOutput.ReadLineAsync())!=null){long microseconds;if(line.StartsWith("out_time_us=")&&long.TryParse(line.Substring(12),out microseconds))progress(new{percent=(int)Math.Max(0,Math.Min(99,microseconds/1000000d/seconds*100))});}await Task.Run(()=>process.WaitForExit());string details=await error;if(cancelled)throw new Exception("Editing canceled, original recording retained");if(process.ExitCode!=0)throw new Exception("Clip save failed: "+details.Substring(0,Math.Min(500,details.Length)));child=null;}
 }
 public object Cancel(){cancelled=true;try{if(child!=null&&!child.HasExited)child.Kill();}catch{}return true;}
 public void Dispose(){Cancel();}
}
