using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

class NativeThumbnails : IDisposable {
 readonly string folder,encoder;readonly Func<string,string> urlFor;readonly Action<object> notify;readonly SemaphoreSlim serial=new SemaphoreSlim(1);CancellationTokenSource active;
 public NativeThumbnails(string appRoot,string profile,Func<string,string> url,Action<object> progress){folder=Path.Combine(profile,"thumbnails");Directory.CreateDirectory(folder);encoder=Path.Combine(appRoot,"build","encoder","ffmpeg.exe");urlFor=url;notify=progress;}
 public object Start(List<Item> items,string batch){if(batch.Length>128)throw new Exception("缩略图任务无效");if(active!=null)try{active.Cancel();}catch(ObjectDisposedException){}var cancel=new CancellationTokenSource();active=cancel;var selected=items.Where(i=>i.kind=="image"||i.kind=="video"||i.kind=="sequence").ToList();Task.Run(async()=>{await serial.WaitAsync();try{int done=0;foreach(var item in selected){if(cancel.IsCancellationRequested)return;string url=null;try{url=await Generate(item,cancel.Token);}catch{}if(cancel.IsCancellationRequested)return;notify(new{batch=batch,id=item.id,url=url,done=++done,total=selected.Count,complete=false});}if(!cancel.IsCancellationRequested)notify(new{batch=batch,done=done,total=selected.Count,complete=true});}finally{serial.Release();cancel.Dispose();}});return new{batch=batch,total=selected.Count};}
 async Task<string> Generate(Item item,CancellationToken token){
  string file=item.kind=="sequence"?item.frames[0].path:item.path,ext=Path.GetExtension(file).ToLowerInvariant();if(ext==".svg")return item.kind=="sequence"?item.frames[0].url:item.url;
  var stat=new FileInfo(file);string key=NativeCatalog.Hash(file.ToLowerInvariant()+"@"+stat.Length+":"+stat.LastWriteTimeUtc.Ticks),output=Path.Combine(folder,key+".jpg");if(File.Exists(output))return urlFor(output);
  string temporary=Path.Combine(folder,key+"-"+Guid.NewGuid().ToString("N")+".jpg");
  try{
   if(item.kind!="video"&&new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff",".ico"}.Contains(ext)){
    var thread=Thread.CurrentThread;var priority=thread.Priority;try{thread.Priority=ThreadPriority.BelowNormal;Picture(file,temporary);}finally{thread.Priority=priority;}
   }else{await Video(file,temporary,item.kind=="video"?.1:0,token);if(!File.Exists(temporary)&&!token.IsCancellationRequested)await Video(file,temporary,0,token);}
   token.ThrowIfCancellationRequested();if(!File.Exists(temporary)||new FileInfo(temporary).Length<100)throw new Exception("暂无缩略图");if(File.Exists(output))File.Delete(temporary);else File.Move(temporary,output);return urlFor(output);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void Picture(string file,string output){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var image=Image.FromStream(stream,true,false)){
  if((long)image.Width*image.Height>100000000)throw new Exception("图片尺寸过大");
  if(image.PropertyIdList.Contains(0x112)){int orientation=image.GetPropertyItem(0x112).Value[0];var rotations=new[]{RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};if(orientation<rotations.Length)image.RotateFlip(rotations[orientation]);}
  using(var bitmap=new Bitmap(256,144,PixelFormat.Format24bppRgb)){
   using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(16,17,20));g.InterpolationMode=InterpolationMode.HighQualityBilinear;double scale=Math.Min(256d/image.Width,144d/image.Height);int width=Math.Max(1,(int)(image.Width*scale)),height=Math.Max(1,(int)(image.Height*scale));g.DrawImage(image,new Rectangle((256-width)/2,(144-height)/2,width,height),0,0,image.Width,image.Height,GraphicsUnit.Pixel);}
   var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);using(var parameters=new EncoderParameters(1)){parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,75L);bitmap.Save(output,codec,parameters);}
  }
 }}
 async Task Video(string file,string output,double seek,CancellationToken token){
  var args=new[]{"-hide_banner","-loglevel","error","-y","-hwaccel","none","-threads","1","-ss",seek.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",file,"-an","-sn","-dn","-frames:v","1","-vf","scale=256:144:force_original_aspect_ratio=decrease,pad=256:144:(ow-iw)/2:(oh-ih)/2:color=0x101114","-threads","1","-q:v","5",output};
  using(var process=new Process{StartInfo=new ProcessStartInfo(NativeEncoder.Ensure(encoder),String.Join(" ",args.Select(NativeExporter.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}}){process.Start();try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}using(token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}})){var errors=process.StandardError.ReadToEndAsync();bool exited=await Task.Run(()=>process.WaitForExit(15000));if(!exited){try{process.Kill();}catch{}await errors;throw new Exception("缩略图读取超时");}await errors;if(process.ExitCode!=0&&File.Exists(output))File.Delete(output);}}
 }
 public void Dispose(){if(active!=null)try{active.Cancel();}catch(ObjectDisposedException){}}
}
