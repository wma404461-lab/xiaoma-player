using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

class NativeServer : IDisposable {
 readonly HttpListener listener; readonly string root,token=Guid.NewGuid().ToString("N");
 readonly Dictionary<string,string> roots=new Dictionary<string,string>(); readonly Dictionary<string,string> rootIds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 readonly SemaphoreSlim previews=new SemaphoreSlim(2); readonly Dictionary<string,byte[]> previewCache=new Dictionary<string,byte[]>();readonly Queue<string> cacheOrder=new Queue<string>();long cacheBytes;
 public readonly string origin,baseUrl;public Func<string,int,Stream,Task> writeFrame;
 public NativeServer(string appRoot,string profile){
  root=Path.GetFullPath(appRoot);string portFile=Path.Combine(profile,"server-port.txt");int saved=0;try{int.TryParse(File.ReadAllText(portFile),out saved);}catch{}
  for(int attempt=0;attempt<8;attempt++){
   if(attempt==0&&saved>=1024&&saved<=65535){var probe=new TcpListener(IPAddress.Loopback,saved);probe.ExclusiveAddressUse=true;try{probe.Start();}catch(SocketException){saved=0;}finally{probe.Stop();}}
   int port=saved;if(attempt>0||port<1024||port>65535){var socket=new TcpListener(IPAddress.Loopback,0);try{socket.Start();port=((IPEndPoint)socket.LocalEndpoint).Port;}finally{socket.Stop();}}
   string candidate="http://127.0.0.1:"+port;var pending=new HttpListener();
   // A failed Start can dispose the listener. Every retry needs a fresh instance.
   try{pending.Prefixes.Add(candidate+"/");pending.Start();}
   catch(HttpListenerException){try{pending.Close();}catch{}if(attempt==7)throw;continue;}
   catch{try{pending.Close();}catch{}throw;}
   try{File.WriteAllText(portFile,port.ToString());}catch{pending.Close();throw;}
   listener=pending;origin=candidate;break;
  }
  baseUrl=origin+"/"+token+"/";Task.Run((Func<Task>)Loop);
 }
 public string UrlFor(string file){string folder=Path.GetDirectoryName(Path.GetFullPath(file)),id;lock(roots){if(!rootIds.TryGetValue(folder,out id)){id=Guid.NewGuid().ToString("N");rootIds[folder]=id;roots[id]=folder;}}return baseUrl+"media/"+id+"/"+Uri.EscapeDataString(Path.GetFileName(file));}
 async Task Loop(){while(listener.IsListening){HttpListenerContext context;try{context=await listener.GetContextAsync();}catch{return;}Task.Run(()=>Serve(context));}}
 public static bool Within(string folder,string file){return file.StartsWith(folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
 static void NoLinks(string folder,string file){for(string current=file;Within(folder,current);current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new Exception("Link path cannot be read");}
 static string Mime(string file){switch(Path.GetExtension(file).ToLowerInvariant()){case ".html":return "text/html; charset=utf-8";case ".js":case ".mjs":return "text/javascript";case ".css":return "text/css";case ".png":return "image/png";case ".jpg":case ".jpeg":return "image/jpeg";case ".webp":return "image/webp";case ".gif":return "image/gif";case ".svg":return "image/svg+xml";case ".avif":return "image/avif";case ".mp4":case ".m4v":return "video/mp4";case ".webm":return "video/webm";case ".mov":return "video/quicktime";case ".mkv":return "video/x-matroska";case ".mp3":return "audio/mpeg";case ".wav":return "audio/wav";case ".ogg":case ".opus":return "audio/ogg";case ".pdf":return "application/pdf";case ".wasm":return "application/wasm";default:return "application/octet-stream";}}
 async Task Serve(HttpListenerContext context){var request=context.Request;var response=context.Response;try{
  if(request.Url.Authority!=new Uri(origin).Authority||!request.Url.AbsolutePath.StartsWith("/"+token+"/",StringComparison.Ordinal)){response.StatusCode=403;return;}
  string relative=Uri.UnescapeDataString(request.Url.AbsolutePath.Substring(token.Length+2));
  if(relative.StartsWith("export/")){var parts=relative.Split('/');int index;if(request.HttpMethod!="POST"||parts.Length!=3||!int.TryParse(parts[2],out index)||writeFrame==null||request.Headers["Origin"]!=origin){response.StatusCode=403;return;}await writeFrame(parts[1],index,request.InputStream);response.StatusCode=204;return;}
  if(request.HttpMethod!="GET"&&request.HttpMethod!="HEAD"){response.StatusCode=405;return;}
  string file,folder;
  if(relative.StartsWith("media/")){var parts=relative.Split(new[]{'/'},3);lock(roots){if(parts.Length<3||!roots.TryGetValue(parts[1],out folder)){response.StatusCode=403;return;}}file=Path.GetFullPath(Path.Combine(folder,parts[2].Replace('/','\\')));}
  else {folder=root;file=Path.GetFullPath(Path.Combine(root,relative.Replace('/','\\')));}
  if(!Within(folder,file)){response.StatusCode=403;return;}NoLinks(folder,file);var stat=new FileInfo(file);if(!stat.Exists){response.StatusCode=404;return;}
  response.Headers["Cache-Control"]="no-cache";response.Headers["X-Content-Type-Options"]="nosniff";response.ContentType=Mime(file);
  int edge;if(relative.StartsWith("media/")&&request.HttpMethod=="GET"&&int.TryParse(request.QueryString["preview"],out edge)&&edge>=256&&edge<=4096&&new[]{".png",".jpg",".jpeg",".bmp"}.Contains(stat.Extension.ToLowerInvariant())){
   string key=file+"@"+stat.LastWriteTimeUtc.Ticks+":"+stat.Length+":"+edge;byte[] bytes;lock(previewCache)previewCache.TryGetValue(key,out bytes);
   int width=0,height=0;
   if(bytes==null){await previews.WaitAsync();try{bytes=await Task.Run(()=>Preview(file,edge,out width,out height));lock(previewCache){if(!previewCache.ContainsKey(key)){previewCache[key]=bytes;cacheOrder.Enqueue(key);cacheBytes+=bytes.Length;while(cacheBytes>32*1024*1024&&cacheOrder.Count>1){string old=cacheOrder.Dequeue();cacheBytes-=previewCache[old].Length;previewCache.Remove(old);}}}}finally{previews.Release();}}
   // Dimensions are stored ahead of the PNG so repeated previews need no source decode.
   width=BitConverter.ToInt32(bytes,0);height=BitConverter.ToInt32(bytes,4);response.Headers["X-Image-Width"]=width.ToString();response.Headers["X-Image-Height"]=height.ToString();response.ContentType="image/png";response.ContentLength64=bytes.Length-8;await response.OutputStream.WriteAsync(bytes,8,bytes.Length-8);return;
  }
  long start=0,end=stat.Length-1;string range=request.Headers["Range"];response.Headers["Accept-Ranges"]="bytes";
  if(range!=null){var m=System.Text.RegularExpressions.Regex.Match(range,@"^bytes=(\d*)-(\d*)$");if(!m.Success||(m.Groups[1].Value==""&&m.Groups[2].Value=="")){response.StatusCode=416;response.Headers["Content-Range"]="bytes */"+stat.Length;return;}
   if(m.Groups[1].Length>0){if(!long.TryParse(m.Groups[1].Value,out start)){response.StatusCode=416;return;}if(m.Groups[2].Length>0){long value;if(!long.TryParse(m.Groups[2].Value,out value)){response.StatusCode=416;return;}end=Math.Min(end,value);}}else{long suffix;if(!long.TryParse(m.Groups[2].Value,out suffix)){response.StatusCode=416;return;}start=Math.Max(0,stat.Length-suffix);}
   if(start>end||start>=stat.Length){response.StatusCode=416;response.Headers["Content-Range"]="bytes */"+stat.Length;return;}response.StatusCode=206;response.Headers["Content-Range"]="bytes "+start+"-"+end+"/"+stat.Length;
  }
  long remaining=Math.Max(0,end-start+1);response.ContentLength64=remaining;if(request.HttpMethod=="HEAD"||remaining==0)return;
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true)){stream.Position=start;byte[] buffer=new byte[65536];while(remaining>0){int count=await stream.ReadAsync(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)break;await response.OutputStream.WriteAsync(buffer,0,count);remaining-=count;}}
 }catch(Exception error){try{response.StatusCode=error.Message.Contains("Link")?403:500;byte[] message=Encoding.UTF8.GetBytes("File read failed");await response.OutputStream.WriteAsync(message,0,message.Length);}catch{}}finally{try{response.Close();}catch{}}}
 static byte[] Preview(string file,int edge,out int width,out int height){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var image=Image.FromStream(stream,true,false)){
  if((long)image.Width*image.Height>100000000)throw new Exception("image size is too large");
  if(image.PropertyIdList.Contains(0x112)){int orientation=image.GetPropertyItem(0x112).Value[0];var rotations=new[]{RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};if(orientation<rotations.Length)image.RotateFlip(rotations[orientation]);}
  width=image.Width;height=image.Height;double ratio=Math.Min(1,(double)edge/Math.Max(width,height));using(var bitmap=new Bitmap(Math.Max(1,(int)Math.Round(width*ratio)),Math.Max(1,(int)Math.Round(height*ratio)),PixelFormat.Format32bppArgb)){
   using(var g=Graphics.FromImage(bitmap)){g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(image,new Rectangle(0,0,bitmap.Width,bitmap.Height),0,0,width,height,GraphicsUnit.Pixel);}
   using(var output=new MemoryStream()){byte[] header=new byte[8];Buffer.BlockCopy(BitConverter.GetBytes(width),0,header,0,4);Buffer.BlockCopy(BitConverter.GetBytes(height),0,header,4,4);output.Write(header,0,8);bitmap.Save(output,ImageFormat.Png);return output.ToArray();}
  }
 }}
 public void Dispose(){listener.Close();}
}
