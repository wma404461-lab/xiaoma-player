using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Signers;

class NativeUpdater {
 readonly string root,profile,appRoot,version;readonly Action<object> notify;
 readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=40000};readonly Dictionary<string,object> config;
 readonly SemaphoreSlim downloadGate=new SemaphoreSlim(1);Dictionary<string,object> info;DateTime checkedAt;
 CancellationTokenSource downloading;string helper,stage,readyVersion;Dictionary<string,object> metrics;
 public NativeUpdater(string application,string data,string current,Action<object> progress){root=application;profile=data;version=current;appRoot=Path.Combine(root,"resources","app");notify=progress;config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(appRoot,"src","update-config.json")));ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
 async Task<HttpWebResponse> Request(string url,CancellationToken cancellation){
  var request=(HttpWebRequest)WebRequest.Create(url);request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=30000;request.Headers[HttpRequestHeader.AcceptEncoding]="identity";request.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
  using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){timeout.CancelAfter(15000);using(timeout.Token.Register(()=>request.Abort()))try{return (HttpWebResponse)await request.GetResponseAsync();}catch(WebException){cancellation.ThrowIfCancellationRequested();if(timeout.IsCancellationRequested)throw new IOException("更新服务器连接超时，请重试");throw;}}
 }
 void Artifact(Dictionary<string,object> value){
  var url=new Uri(Convert.ToString(value["url"]));var origin=new Uri((string)config["manifestUrl"]);long size=Convert.ToInt64(value["size"]);
  if(url.Scheme!="https"||url.Authority!=origin.Authority||!url.AbsolutePath.StartsWith("/tools/xiaoma-player/releases/",StringComparison.Ordinal)||url.UserInfo!=""||Uri.UnescapeDataString(url.AbsolutePath).Split('/').Any(p=>p==".."||p==".")||!System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(value["sha256"]),"^[a-fA-F0-9]{64}$")||size<1000||size>600L*1024*1024)throw new Exception("更新信息格式不正确");
 }
 public Dictionary<string,object> Validate(string raw){
  var envelope=json.Deserialize<Dictionary<string,object>>(raw);string encoded=(string)envelope["payload"];if(encoded.Length>20000)throw new Exception("更新信息过大");byte[] payload=Convert.FromBase64String(encoded),signature=Convert.FromBase64String((string)envelope["signature"]);string pem=((string)config["publicKey"]).Replace("-----BEGIN PUBLIC KEY-----","").Replace("-----END PUBLIC KEY-----","").Trim();var key=PublicKeyFactory.CreateKey(Convert.FromBase64String(pem));var verifier=new Ed25519Signer();verifier.Init(false,key);verifier.BlockUpdate(payload,0,payload.Length);if(!verifier.VerifySignature(signature))throw new Exception("更新信息签名校验失败");
  var result=json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(payload));Artifact(result);Version remote;if(!Version.TryParse((string)result["version"],out remote))throw new Exception("更新版本无效");
  if(result.ContainsKey("incremental")){var small=result["incremental"] as Dictionary<string,object>;if(small==null||Convert.ToInt32(small["schema"])!=1)throw new Exception("精简更新信息无效");Artifact(small);Version minimum;if(!Version.TryParse(Convert.ToString(small["minUpdaterVersion"]),out minimum))throw new Exception("更新器版本无效");UpdateResources.Parse(small["inherit"]);}
  result["available"]=remote>new Version(version);result["installed"]=version;return result;
 }
 public async Task<object> Check(){
  string raw;using(var response=await Request((string)config["manifestUrl"],CancellationToken.None))using(var reader=new StreamReader(response.GetResponseStream())){char[] chars=new char[40001];int count=0,read;while(count<chars.Length&&(read=await reader.ReadAsync(chars,count,chars.Length-count))>0)count+=read;if(count>40000)throw new Exception("更新信息过大");raw=new string(chars,0,count);}
  var result=Validate(raw);info=result;checkedAt=DateTime.UtcNow;return result;
 }
 public object Cancel(){var active=downloading;if(active!=null)try{active.Cancel();}catch(ObjectDisposedException){}return true;}
 void SaveMetrics(string directory){File.WriteAllText(Path.Combine(directory,"download-metrics.json"),json.Serialize(metrics),Encoding.UTF8);}
 async Task<string> Fetch(Dictionary<string,object> artifact,string cache,CancellationToken cancellation){
  long expected=Convert.ToInt64(artifact["size"]);string sha=Convert.ToString(artifact["sha256"]).ToLowerInvariant(),file=Path.Combine(cache,sha+".zip"),partial=file+".part";UpdateResources.NoLinks(root,file);UpdateResources.NoLinks(root,partial);var verify=Stopwatch.StartNew();bool cached=await Task.Run(()=>UpdateTransfer.Cached(file,expected,sha));metrics["cacheVerifyMs"]=verify.Elapsed.TotalMilliseconds;metrics["cacheHit"]=cached;metrics["url"]=artifact["url"];metrics["bytes"]=expected;
  if(cached){metrics["downloadMs"]=0;metrics["ttfbMs"]=0;notify(new{phase="prepare",percent=100});return file;}
  var watch=Stopwatch.StartNew();using(var response=await Request(Convert.ToString(artifact["url"]),cancellation))using(cancellation.Register(()=>response.Close())){
   metrics["ttfbMs"]=watch.Elapsed.TotalMilliseconds;metrics["ttfbDefinition"]="request to response headers";
   if(response.StatusCode!=HttpStatusCode.OK||(response.ContentLength>=0&&response.ContentLength!=expected)||(!String.IsNullOrEmpty(response.ContentEncoding)&&response.ContentEncoding!="identity"))throw new IOException("更新服务器返回的文件大小或格式不正确");
   var last=Stopwatch.StartNew();notify(new{phase="download",percent=0});using(var input=response.GetResponseStream())await UpdateTransfer.WriteVerified(input,partial,expected,sha,total=>{if(last.ElapsedMilliseconds>=250){last.Restart();notify(new{phase="download",percent=(int)(total*100/expected)});}},cancellation,()=>response.Close());
  }
  cancellation.ThrowIfCancellationRequested();if(File.Exists(file))File.Delete(file);File.Move(partial,file);metrics["downloadMs"]=watch.Elapsed.TotalMilliseconds;metrics["megabytesPerSecond"]=expected/1000000d/(watch.Elapsed.TotalSeconds);return file;
 }
 async Task Prepare(string zip,string directory,Dictionary<string,object> manifest,Dictionary<string,object> selected,bool reuse,CancellationToken cancellation){
  if(Directory.Exists(stage)){UpdateResources.NoLinks(root,stage);Directory.Delete(stage,true);}string arguments="prepare "+NativeExporter.Quote(zip)+" "+NativeExporter.Quote(stage)+" "+NativeExporter.Quote(Convert.ToString(selected["sha256"]));
  if(reuse){string recipe=Path.Combine(directory,"inherit.json");File.WriteAllText(recipe,json.Serialize(new{version=manifest["version"],inherit=selected["inherit"]}),Encoding.UTF8);arguments="prepare-reuse "+NativeExporter.Quote(zip)+" "+NativeExporter.Quote(stage)+" "+NativeExporter.Quote(Convert.ToString(selected["sha256"]))+" "+NativeExporter.Quote(root)+" "+NativeExporter.Quote(recipe);}
  notify(new{phase="prepare",percent=100});var watch=Stopwatch.StartNew();using(var process=Process.Start(new ProcessStartInfo(helper,arguments){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true}))using(cancellation.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}})){
   var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await Task.Run(()=>process.WaitForExit());string raw=await output;await error;cancellation.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new IOException("更新包解压或继承资源校验失败："+raw.Trim());
   try{metrics["preparation"]=json.DeserializeObject(raw);}catch{}metrics["prepareMs"]=watch.Elapsed.TotalMilliseconds;
  }
 }
 public async Task<object> Download(){
  await downloadGate.WaitAsync();try{
   if(info==null||(DateTime.UtcNow-checkedAt).TotalMinutes>2)await Check();var manifest=info;if(!(bool)manifest["available"])return new{current=true};
   if(!String.Equals(profile,Path.Combine(root,".data"),StringComparison.OrdinalIgnoreCase))throw new Exception("请从正常的成品目录运行更新");
   if(readyVersion==Convert.ToString(manifest["version"])&&stage!=null&&Directory.Exists(stage))return new{ready=true,version=readyVersion,cached=true,alreadyPrepared=true,metrics=metrics};
   downloading=new CancellationTokenSource();var cancellation=downloading.Token;var total=Stopwatch.StartNew();string updates=Path.Combine(profile,"updates"),cache=Path.Combine(updates,"cache"),directory=Path.Combine(updates,Guid.NewGuid().ToString("N"));UpdateResources.NoLinks(root,cache);UpdateResources.NoLinks(root,directory);Directory.CreateDirectory(cache);Directory.CreateDirectory(directory);helper=Path.Combine(directory,"UpdateHelper.exe");stage=Path.Combine(directory,"stage");File.Copy(Path.Combine(appRoot,"build","UpdateHelper.exe"),helper);
   metrics=new Dictionary<string,object>{{"from",version},{"to",manifest["version"]},{"kind","full"},{"fallbacks",0},{"retries",0}};Dictionary<string,object> selected=manifest;bool reuse=false;var verify=Stopwatch.StartNew();
   try{
    if(manifest.ContainsKey("incremental")){var candidate=(Dictionary<string,object>)manifest["incremental"];if(new Version(version)>=new Version(Convert.ToString(candidate["minUpdaterVersion"]))){var resources=UpdateResources.Parse(candidate["inherit"]);reuse=await Task.Run(()=>resources.All(resource=>UpdateResources.Matches(root,resource)));if(reuse)selected=candidate;else metrics["fallbackReason"]="installed resources missing or hash mismatch";}}
    metrics["resourceVerifyMs"]=verify.Elapsed.TotalMilliseconds;metrics["kind"]=reuse?"incremental":"full";string zip=await Fetch(selected,cache,cancellation);
    try{await Prepare(zip,directory,manifest,selected,reuse,cancellation);}catch(Exception error){if(!reuse||cancellation.IsCancellationRequested)throw;metrics["fallbacks"]=1;metrics["fallbackReason"]=error.Message;metrics["kind"]="full";selected=manifest;zip=await Fetch(selected,cache,cancellation);await Prepare(zip,directory,manifest,selected,false,cancellation);}
    readyVersion=Convert.ToString(manifest["version"]);metrics["readyMs"]=total.Elapsed.TotalMilliseconds;metrics["success"]=true;SaveMetrics(directory);return new{ready=true,version=readyVersion,kind=metrics["kind"],size=selected["size"],cached=metrics["cacheHit"],metrics=metrics};
   }catch(Exception error){metrics["success"]=false;metrics["cancelled"]=cancellation.IsCancellationRequested;metrics["error"]=error.Message;metrics["readyMs"]=total.Elapsed.TotalMilliseconds;SaveMetrics(directory);throw;}finally{downloading.Dispose();downloading=null;}
  }finally{downloadGate.Release();}
 }
 public object Install(){if(helper==null||readyVersion==null||!Directory.Exists(stage))throw new Exception("请先下载更新");File.WriteAllText(Path.Combine(Path.GetDirectoryName(stage),"install-requested.json"),json.Serialize(new{at=DateTime.UtcNow.ToString("o"),version=readyVersion}));Process.Start(new ProcessStartInfo(helper,"install "+NativeExporter.Quote(stage)+" "+NativeExporter.Quote(root)+" "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});return true;}
}
