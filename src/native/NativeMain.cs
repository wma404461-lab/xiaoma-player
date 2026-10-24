using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

partial class NativeMain : Form {
 WebView2 view;
 NativeThumbnails thumbnails;
 readonly string root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'),profile,appRoot,version;
 readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};
 NativeServer server;NativeCatalog catalog;NativeExporter exporter;NativeUpdater updater;bool ready,full;string[] pending;Rectangle previousBounds;FormWindowState previousState;readonly bool testing,headless;readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();readonly SemaphoreSlim documentQueue=new SemaphoreSlim(1);
 protected override bool ShowWithoutActivation {get{return headless||fastHomeDormant||directOpening||!homePainted;}}
 [DllImport("shell32.dll")] static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string id);
 [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [StructLayout(LayoutKind.Sequential)] struct MemoryStatus{public uint length,load;public ulong totalPhys,availPhys,totalPage,availPage,totalVirtual,availVirtual,availExtended;}
 public NativeMain(string[] args){
  testing=Environment.GetEnvironmentVariable("YIYAN_TEST_MODE")!=null;headless=testing&&Environment.GetEnvironmentVariable("YIYAN_TEST_VISIBLE")!="1";profile=Environment.GetEnvironmentVariable("YIYAN_TEST_USER_DATA")??Path.Combine(root,".data");appRoot=Path.Combine(root,"resources","app");Directory.CreateDirectory(profile);version=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(appRoot,"package.json")))["version"].ToString();pending=args.Where(p=>Path.IsPathRooted(p)).ToArray();
  ConfigureFastHome(args);ConfigureDirectOpen();Text="小马播放器+录屏";Icon=new Icon(Path.Combine(appRoot,"assets","icon.ico"));AutoScaleMode=AutoScaleMode.None;Bounds=NativeWindowLayout.Current();MinimumSize=new Size(680,480);BackColor=Color.FromArgb(23,26,31);StartPosition=FormStartPosition.Manual;if(headless||fastHomeDormant||directOpening){Location=new Point(-20000,-20000);ShowInTaskbar=false;}
  if(!directOpening)BuildHome();Shown+=(s,e)=>{WriteStartup(directOpening?"direct-ready":"home-ready");if(directOpening)BeginInvoke(new Action(()=>EnsureViewer()));else WarmViewerAfterPaint();WatchFastHome();if(args.Any(a=>a.StartsWith("--home-action=")))BeginInvoke(new Action(()=>HandleFastHomeArguments(args)));};
  HandleCreated+=(s,e)=>{try{int dark=1,background=ColorTranslator.ToWin32(Color.FromArgb(21,22,25)),ink=ColorTranslator.ToWin32(Color.FromArgb(231,232,236));DwmSetWindowAttribute(Handle,20,ref dark,4);DwmSetWindowAttribute(Handle,35,ref background,4);DwmSetWindowAttribute(Handle,36,ref ink,4);}catch{}};
  FormClosed+=(s,e)=>{if(thumbnails!=null)thumbnails.Dispose();if(updater!=null)updater.Cancel();};
  InitializeRecorderLifecycle();
  LocationChanged+=(s,e)=>ApplyUiScale();DpiChanged+=(s,e)=>ApplyUiScale();
  FormClosed+=(s,e)=>{if(viewerWarmup!=null){viewerWarmup.Stop();viewerWarmup.Dispose();}foreach(var watcher in watchers)watcher.Dispose();if(clipper!=null)clipper.Dispose();if(exporter!=null)exporter.Dispose();if(view!=null)view.Dispose();if(server!=null)server.Dispose();};
 }
 async Task Initialize(){
  if(!contentPending)NativeEncoder.Warm(appRoot);
  SetCurrentProcessExplicitAppUserModelID("artist_m.XiaomaPlayer");
  server=new NativeServer(appRoot,profile);catalog=new NativeCatalog(server.UrlFor);exporter=new NativeExporter(appRoot,profile,server.UrlFor,value=>Emit("export-progress",value));server.writeFrame=exporter.Write;updater=new NativeUpdater(root,profile,version,value=>Emit("update-progress",value));
  thumbnails=new NativeThumbnails(appRoot,profile,server.UrlFor,value=>Emit("thumbnail-ready",value));
  clipper=new NativeClipper(appRoot,()=>exporter.SelectCaptureEncoder(),value=>Emit("trim-progress",value));
  WriteStartup("services-ready");
  var options=new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");string port=Environment.GetEnvironmentVariable("YIYAN_NATIVE_DEBUG_PORT");if(testing&&port!=null){options.AdditionalBrowserArguments+=" --remote-debugging-port="+int.Parse(port)+" --remote-debugging-address=127.0.0.1";if(headless)options.AdditionalBrowserArguments+=" --disable-renderer-backgrounding --disable-background-timer-throttling --disable-backgrounding-occluded-windows";}
  var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(profile,"webview"),options);WriteStartup("environment-ready");await view.EnsureCoreWebView2Async(environment);WriteStartup("browser-ready");var core=view.CoreWebView2;await SyncUiScale();
  core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=testing;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;if(testing)core.IsMuted=true;
  core.NewWindowRequested+=(s,e)=>e.Handled=true;core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;core.NavigationStarting+=(s,e)=>{if(e.Uri!=server.baseUrl+"src/index.html")e.Cancel=true;};core.DownloadStarting+=(s,e)=>e.Cancel=true;
  core.WebMessageReceived+=Message;await ImportOriginalPreferences(core);await core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(appRoot,"src","native-preload.js")));core.Navigate(server.baseUrl+"src/index.html");
  File.WriteAllText(Path.Combine(profile,"native-runtime.json"),json.Serialize(new{version=version,url=server.baseUrl,webview=environment.BrowserVersionString,gpu=true}));
 }
 void Emit(string name,object value){if(IsDisposed||!IsHandleCreated)return;Action send=()=>{if(view!=null&&view.CoreWebView2!=null&&!IsDisposed)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{eventName=name,value=value}));};try{if(InvokeRequired)BeginInvoke(send);else send();}catch{}}
 static string S(object value){return Convert.ToString(value);}
 static object[] Args(Dictionary<string,object> data){object value;return data.TryGetValue("args",out value)?((IEnumerable)value).Cast<object>().ToArray():new object[0];}
 async void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e){int id=0;try{
  if(e.Source!=server.baseUrl+"src/index.html")return;var message=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);id=Convert.ToInt32(message["id"]);string method=S(message["method"]);object[] args=Args(message);object result;
  if(method=="file-paths"){result=e.AdditionalObjects.OfType<CoreWebView2File>().Select(f=>f.Path).ToArray();}
  else result=await Dispatch(method,args);view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{id=id,result=result}));if(method=="update-install")BeginInvoke(new Action(Close));
 }catch(Exception error){if(view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{id=id,error=error.Message}));}}
 async Task<object> Dispatch(string method,object[] args){if(method.StartsWith("recorder-",StringComparison.Ordinal)&&recorder==null)recorder=new NativeRecorder(this,profile,testing,value=>Emit("recorder-status",value),path=>OpenPaths(new[]{path}),()=>Emit("recorder-hotkey","start"));switch(method){
  case "scan":{var selected=((IEnumerable)args[0]).Cast<object>().Select(S).ToArray();object result=await Task.Run(()=>catalog.Scan(selected));if(!testing)NativeShellThumbnail.Warm(selected);Watch();return result;}
  case "choose":if(S(args[0])=="folder"){string folder=NativeFolderPicker.Show(this,"选择要查看的文件夹");return folder==null?new string[0]:new[]{folder};}else{using(var dialog=new OpenFileDialog{Title="选择文件",Multiselect=true})return dialog.ShowDialog(this)==DialogResult.OK?dialog.FileNames:new string[0];}
  case "initial-paths":ready=true;ViewerReady();var paths=pending;pending=new string[0];return paths;
  case "content-ready":ContentReady();return true;
  case "window-layout":if(testing){var work=Screen.FromControl(this).WorkingArea;return new{bounds=new{x=Bounds.X,y=Bounds.Y,width=Bounds.Width,height=Bounds.Height},work=new{x=work.X,y=work.Y,width=work.Width,height=work.Height},dpi=DeviceDpi,directOpening=directOpening,contentPending=contentPending,dragRequests=dragRequests};}throw new Exception("未知操作");
  case "app-version":return version;case "window-drag":return DragWindow();
  case "memory-budget":var memory=new MemoryStatus{length=(uint)Marshal.SizeOf(typeof(MemoryStatus))};GlobalMemoryStatusEx(ref memory);return Math.Min(256*1024*1024,Math.Max(32*1024*1024,(long)(memory.availPhys*.05)));
  case "thumbnails-start":return thumbnails.Start(((IEnumerable)args[0]).Cast<object>().Select(id=>catalog.Get(S(id))).ToList(),S(args[1]));
  case "fullscreen":if(!full){previousBounds=Bounds;previousState=WindowState;WindowState=FormWindowState.Normal;FormBorderStyle=FormBorderStyle.None;Rectangle screen=Screen.FromControl(this).Bounds;Bounds=headless?new Rectangle(new Point(-20000,-20000),screen.Size):screen;full=true;}else{FormBorderStyle=FormBorderStyle.Sizable;Bounds=previousBounds;WindowState=previousState;full=false;}Emit("fullscreen-state",full);return full;
  case "refresh-sequence":return await Task.Run(()=>catalog.Refresh(S(args[0])));
  case "reveal":return await NativeShell.Reveal(catalog.Get(S(args[0])).path,testing);
  case "file-info":{var item=catalog.Get(S(args[0]));var stat=new FileInfo(item.path);return new{url=item.url,size=stat.Length,mtime=stat.LastWriteTimeUtc.Ticks};}
  case "subtitles":{var item=catalog.Get(S(args[0]));string stem=Path.Combine(item.folder,Path.GetFileNameWithoutExtension(item.path));foreach(string ext in new[]{".vtt",".srt"})if(File.Exists(stem+ext)&&new FileInfo(stem+ext).Length<5*1024*1024)return new{extension=ext,text=File.ReadAllText(stem+ext)};return null;}
  case "document-preview":return await DocumentPreview(catalog.Get(S(args[0])));
  case "export-start":{if(recorder!=null&&recorder.Busy)throw new Exception("录屏结束后可以合成序列视频");var item=catalog.Get(S(args[0]));if(item.kind!="sequence")throw new Exception("请先打开编号序列");string output=testing?Environment.GetEnvironmentVariable("YIYAN_NATIVE_SAVE_PATH"):null;if(output==null){using(var dialog=new SaveFileDialog{Title="合成 MP4",Filter="MP4 视频|*.mp4",FileName=item.name+".mp4",InitialDirectory=item.folder,OverwritePrompt=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return null;output=dialog.FileName;}}return await exporter.Start(item,output,(Dictionary<string,object>)args[1]);}
  case "export-finish":return await exporter.Finish(S(args[0]));case "export-next-pass":return await exporter.Next(S(args[0]));case "export-cancel":await exporter.Cancel(S(args[0]));return null;case "gpu-status":return await exporter.Status();
  case "player-menu":return null;
  case "update-check":return await updater.Check();case "update-cancel":return updater.Cancel();case "update-download":if(recorder!=null&&recorder.Busy)throw new Exception("录屏结束后即可更新播放器");return await updater.Download();case "update-install":if(recorder!=null&&recorder.Busy)throw new Exception("请先结束录屏，再安装更新");return updater.Install();
  case "recorder-config":return recorder.Config();case "recorder-region":return recorder.SelectRegion(S(args[0]));case "recorder-folder":return recorder.ChooseFolder();case "recorder-open-folder":return await recorder.OpenFolder();case "recorder-start":if(clipper!=null&&clipper.Busy)throw new Exception("片段保存结束后可以录屏");if(exporter.RecordingConflict())throw new Exception("请等待当前合成结束，再开始录屏");return await recorder.Start((Dictionary<string,object>)args[0]);case "recorder-exit":return await recorder.Exit();case "recorder-pause":return await recorder.Pause();case "recorder-stop":return await recorder.Stop();case "recorder-status":return recorder.Status();
  case "recorder-desktop-test":if(testing)return new{bounds=RecorderDesktop.Bounds(),screens=RecorderDesktop.Screens()};throw new Exception("未知操作");case "recorder-desktop-region-test":if(testing)return recorder.TestDesktopRegion((Dictionary<string,object>)args[0]);throw new Exception("未知操作");case "recorder-control-test":if(testing)return recorder.TestControl(S(args[0]));throw new Exception("未知操作");case "recorder-region-test":if(testing)return recorder.TestRegion(S(args[0]));throw new Exception("未知操作");
  case "recorder-show-region":return recorder.ShowRegion();case "recorder-hide-region":if(!recorder.Busy)return recorder.HideRegion();return false;
  case "recorder-reveal":return await recorder.RevealLast();case "recorder-frame-test":if(testing)return recorder.FrameStatus();throw new Exception("未知操作");
  case "trim-video":{if(recorder!=null&&recorder.Busy)throw new Exception("请先结束录屏，再剪辑录像");if(exporter.RecordingConflict())throw new Exception("合成结束后可以剪辑");var item=catalog.Get(S(args[0]));string output=testing?Environment.GetEnvironmentVariable("YIYAN_NATIVE_SAVE_PATH"):null;output=output??NativeClipper.NextOutput(item.path);var result=await clipper.Trim(item,output,(Dictionary<string,object>)args[1]);if(!catalog.items.ContainsKey(item.id))catalog.items[item.id]=item;result.item=catalog.AddFile(result.path);return result;}
  case "trim-cancel":return clipper.Cancel();
  case "recorder-worker-test":if(testing)return recorder.WorkerProcessId;throw new Exception("未知操作");
  case "test-close":if(testing){BeginInvoke(new Action(Close));return true;}throw new Exception("未知操作");
  default:throw new Exception("未知操作");
 }}
 async Task<object> DocumentPreview(Item item){if(item.kind!="document")throw new Exception("不是文档文件");var stat=new FileInfo(item.path);string folder=Path.Combine(profile,"document-cache"),output=Path.Combine(folder,NativeCatalog.Hash(item.path+"@"+stat.LastWriteTimeUtc.Ticks+":"+stat.Length)+".pdf");Directory.CreateDirectory(folder);await documentQueue.WaitAsync();try{if(!File.Exists(output)){using(var p=Process.Start(new ProcessStartInfo(Path.Combine(appRoot,"build","OfficePreview.exe"),NativeExporter.Quote(item.path)+" "+NativeExporter.Quote(output)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})){var read=p.StandardOutput.ReadToEndAsync();bool done=await Task.Run(()=>p.WaitForExit(90000));if(!done){p.Kill();throw new Exception("文档预览超时");}await read;if(p.ExitCode!=0||!File.Exists(output))throw new Exception("文档预览暂不可用；请确认本机 Word、PowerPoint 或 Excel 能正常打开此文件。");}}return new{url=server.UrlFor(output)};}finally{documentQueue.Release();}}
 void ShowPlayerMenu(Dictionary<string,object> options){var menu=new ContextMenuStrip();Action<string,string> add=(label,action)=>{var entry=menu.Items.Add(label);entry.Click+=(s,e)=>Emit("player-action",action);};add(options.ContainsKey("playing")&&(bool)options["playing"]?"暂停":"播放","play");add("上一帧（D）","previous-frame");add("下一帧（F）","next-frame");menu.Items.Add(new ToolStripSeparator());add("上一个文件","previous-item");add("下一个文件","next-item");add("按列表顺序播放","order");menu.Items.Add(new ToolStripSeparator());add("全屏播放（F11）","fullscreen");add("显示播放列表","sidebar");add("媒体信息（Tab）","media-info");add("在文件夹中显示","reveal");menu.Closed+=(s,e)=>menu.Dispose();menu.Show(Cursor.Position);}
 void Watch(){foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();foreach(string folder in catalog.items.Values.Where(i=>i.kind=="sequence").Select(i=>i.folder).Distinct()){var watcher=new FileSystemWatcher(folder);System.Threading.Timer timer=null;FileSystemEventHandler changed=(s,e)=>{catalog.NoteChange(e.FullPath);if(timer!=null)timer.Dispose();timer=new System.Threading.Timer(o=>Emit("media-changed",folder),null,450,Timeout.Infinite);};watcher.Changed+=changed;watcher.Created+=changed;watcher.Deleted+=changed;watcher.Renamed+=(s,e)=>changed(s,e);watcher.EnableRaisingEvents=true;watchers.Add(watcher);}}
 public void OpenPaths(string[] paths){if(InvokeRequired){BeginInvoke(new Action(()=>OpenPaths(paths)));return;}if(testing&&paths.SequenceEqual(new[]{"--test-close"})){Close();return;}if(HandleFastHomeArguments(paths))return;paths=paths.Where(p=>Path.IsPathRooted(p)&&(File.Exists(p)||Directory.Exists(p))).ToArray();if(paths.Length>0)contentPending=true;if(ready){if(paths.Length>0)EnsureViewer();Emit("open-paths",paths);}else{pending=pending.Concat(paths).ToArray();if(paths.Length>0)EnsureViewer();}if(!headless&&!fastHomeDormant){if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();}}
 [STAThread] static int Main(string[] args){if(args.Length==4&&(args[0]=="--recorder-worker"||args[0]=="--recorder-compat")){try{return args[0]=="--recorder-compat"?RecorderCompatWorker.Run(args):RecorderWorker.Run(args);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}string mutexName="Local\\XiaomaPlayerNative-"+NativeCatalog.Hash(AppDomain.CurrentDomain.BaseDirectory+Environment.GetEnvironmentVariable("YIYAN_TEST_USER_DATA"));bool owner;using(var mutex=new Mutex(true,mutexName,out owner)){
  if(!owner){try{using(var client=new NamedPipeClientStream(".",mutexName.Substring(6),PipeDirection.Out)){client.Connect(3000);using(var writer=new StreamWriter(client)){writer.WriteLine(new JavaScriptSerializer().Serialize(args));}}}catch{}return 0;}
  NativeWindowLayout.EnableDpi();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var form=new NativeMain(args);var mainHandle=form.Handle;Task.Run(async()=>{while(!form.IsDisposed)try{using(var pipe=new NamedPipeServerStream(mutexName.Substring(6),PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){await pipe.WaitForConnectionAsync();using(var reader=new StreamReader(pipe)){string line=await reader.ReadLineAsync();if(line!=null&&line.Length<32768)form.OpenPaths(new JavaScriptSerializer().Deserialize<string[]>(line));}}}catch{}});Application.Run(form);return 0;
 }}
}


