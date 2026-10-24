using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using ScreenRecorderLib;

sealed class RecorderPreferences {
 public string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Xiaoma Recordings");
 public string preset="native",mode="region",display="",outputDevice="",inputDevice="";
 public bool systemAudio=true,microphone=false,cursor=true,minimize=true;
 public int fps=60,countdown=0,stopMinutes=0,schemaVersion=0;
}
sealed class RecorderSource {
 public string id,name,device;public long handle;public int width,height;public bool primary;
}
sealed class NativeRecorderEngine : IDisposable {
 readonly Form owner;readonly bool testing;readonly string data;readonly Action<object> emit;readonly Action<string> open;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly object gate=new object();
 readonly Timer timer=new Timer{Interval=1000};readonly Stopwatch clock=new Stopwatch();
 RecorderPreferences preferences;Recorder recorder;TaskCompletionSource<object> finish;TaskCompletionSource<bool> started;Task stopTask;
 RecorderBar bar;NotifyIcon tray;ScreenRect region;string regionDisplay,file,error,state="idle";bool busy,paused,stopping,closing,cancelled,controllerControls;
 RecorderMetric callbackMetric;int frames,lastFrames,outputWidth,outputHeight;long previousStamp=-1,lateIntervals;double longestInterval;
 List<RecorderSource> displays=new List<RecorderSource>(),windows=new List<RecorderSource>();
 public bool Busy{get{return busy;}}
 [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateDC(string driver,string device,string output,IntPtr mode);
 [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc,int index);
 [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
 public NativeRecorderEngine(Form window,string profile,bool isTesting,Action<object> notify,Action<string> openResult){
  owner=window;testing=isTesting;data=Path.Combine(profile,"recorder");emit=notify;open=openResult;Directory.CreateDirectory(data);
  try{preferences=json.Deserialize<RecorderPreferences>(File.ReadAllText(Path.Combine(data,"settings.json")));}catch{preferences=new RecorderPreferences();}
  if(testing&&Environment.GetEnvironmentVariable("YIYAN_RECORD_OUTPUT")!=null)preferences.folder=Environment.GetEnvironmentVariable("YIYAN_RECORD_OUTPUT");
  timer.Tick+=(s,e)=>Tick();
 }
 void OnUi(Action action){if(owner.IsDisposed)return;try{if(owner.InvokeRequired)owner.BeginInvoke(action);else action();}catch{}}
 void Save(){File.WriteAllText(Path.Combine(data,"settings.json.tmp"),json.Serialize(preferences));string path=Path.Combine(data,"settings.json");if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
 public object Config(){
  if(!busy){
   var names=Recorder.GetDisplays().ToDictionary(d=>d.DeviceName,d=>d.FriendlyName);
   displays=Screen.AllScreens.OrderByDescending(s=>s.Primary).Select((s,i)=>{int w=s.Bounds.Width,h=s.Bounds.Height;IntPtr dc=CreateDC("DISPLAY",s.DeviceName,null,IntPtr.Zero);if(dc!=IntPtr.Zero){w=GetDeviceCaps(dc,118);h=GetDeviceCaps(dc,117);DeleteDC(dc);}return new RecorderSource{id=s.DeviceName,device=s.DeviceName,name="Screen "+(i+1)+(s.Primary?" (Main screen)":"")+" · "+w+"×"+h,width=w,height=h,primary=s.Primary};}).ToList();
   windows=Recorder.GetWindows().Where(w=>!string.IsNullOrWhiteSpace(w.Title)&&w.Handle!=owner.Handle&&(!w.Pid.HasValue||w.Pid.Value!=Process.GetCurrentProcess().Id)).Select(w=>new RecorderSource{id=w.Handle.ToInt64().ToString(),handle=w.Handle.ToInt64(),name=w.Title}).ToList();
  }
  return new{preferences=preferences,displays=displays,windows=windows,outputs=Recorder.GetSystemAudioLoopbackDevices().Select(d=>new{id=d.DeviceName,name=d.FriendlyName}).ToArray(),inputs=Recorder.GetSystemAudioCaptureDevices().Select(d=>new{id=d.DeviceName,name=d.FriendlyName}).ToArray(),region=RegionInfo(),status=Status()};
 }
 object RegionInfo(){return region==null?null:new{x=(int)region.Left,y=(int)region.Top,width=(int)region.Width,height=(int)region.Height,display=regionDisplay};}
 public object TestRegion(string display){if(!testing||busy)throw new Exception("Available only for testing");Config();var target=displays.First(d=>d.id==display);region=new ScreenRect(0,0,640,360);regionDisplay=target.id;return RegionInfo();}
 public object SelectRegion(string display){
  if(busy)throw new Exception("Please end current recording first");var target=displays.FirstOrDefault(d=>d.id==display)??displays.FirstOrDefault();if(target==null)throw new Exception("No available screens");var screen=Screen.AllScreens.First(s=>s.DeviceName==target.device);
  using(var picker=new RecorderRegionPicker(screen)){if(picker.ShowDialog(owner)==DialogResult.OK){Rectangle box=picker.Selection;double sx=(double)target.width/screen.Bounds.Width,sy=(double)target.height/screen.Bounds.Height;region=new ScreenRect((int)(box.Left*sx),(int)(box.Top*sy),(int)(box.Width*sx)/2*2,(int)(box.Height*sy)/2*2);regionDisplay=target.id;}}
  return RegionInfo();
 }
 public object ChooseFolder(){if(busy)throw new Exception("After recording ends, you can change save location");string selected=NativeFolderPicker.Show(owner,"Where is the video saved?",preferences.folder);if(selected!=null){preferences.folder=selected;Save();}return new{folder=preferences.folder};}
 public object OpenFolder(){Directory.CreateDirectory(preferences.folder);Process.Start(new ProcessStartInfo(preferences.folder){UseShellExecute=true});return true;}
 static T Get<T>(Dictionary<string,object> args,string name,T fallback){object value;if(!args.TryGetValue(name,out value)||value==null)return fallback;return (T)Convert.ChangeType(value,typeof(T));}
 public object Prepare(){displays=RecorderSources.Displays();return new{ready=true};}
 public async Task<object> Start(Dictionary<string,object> args){
  if(busy)throw new Exception("Recording or saving, please wait");displays=RecorderSources.Displays();
  string mode=Get(args,"mode",preferences.mode),display=Get(args,"display",preferences.display),preset=Get(args,"preset",preferences.preset),window=Get(args,"window","");
  if(!new[]{"screen","window","region"}.Contains(mode)||!new[]{"4k","native","1080"}.Contains(preset))throw new Exception("Invalid recording settings");
  if(mode=="window")windows=Recorder.GetWindows().Where(w=>w.Handle!=owner.Handle).Select(w=>new RecorderSource{id=w.Handle.ToInt64().ToString(),handle=w.Handle.ToInt64(),name=w.Title}).ToList();
  RecorderSource target=mode=="window"?windows.FirstOrDefault(w=>w.id==window):displays.FirstOrDefault(d=>d.id==display)??displays.FirstOrDefault();if(target==null)throw new Exception(mode=="window"?"Please select the window to record first":"No available screens");
  if(mode=="window"&&(!IsWindow(new IntPtr(target.handle))||IsIconic(new IntPtr(target.handle))))throw new Exception("Please open the target window and click ’Start Recording’");
  if(mode=="region"&&args.ContainsKey("region")){var box=(Dictionary<string,object>)args["region"];int x=Get(box,"x",0),y=Get(box,"y",0),w=Get(box,"width",0),h=Get(box,"height",0);if(x<0||y<0||w<32||h<32||x+w>target.width||y+h>target.height)throw new Exception("Recording area has expired, please reselect");region=new ScreenRect(x,y,w/2*2,h/2*2);regionDisplay=target.id;}
  if(mode=="region"&&(region==null||regionDisplay!=target.id))throw new Exception("Please click ’Select Area’ first”");
  controllerControls=Get(args,"controllerControls",false);preferences.mode=mode;preferences.display=display;preferences.preset=preset;preferences.systemAudio=Get(args,"systemAudio",preferences.systemAudio);preferences.microphone=Get(args,"microphone",preferences.microphone);preferences.cursor=Get(args,"cursor",preferences.cursor);preferences.minimize=Get(args,"minimize",preferences.minimize);
  preferences.fps=Get(args,"fps",preferences.fps)==30?30:60;preferences.countdown=Math.Max(0,Math.Min(5,Get(args,"countdown",preferences.countdown)));preferences.stopMinutes=Math.Max(0,Math.Min(10080,Get(args,"stopMinutes",preferences.stopMinutes)));preferences.outputDevice=Get(args,"outputDevice",preferences.outputDevice);preferences.inputDevice=Get(args,"inputDevice",preferences.inputDevice);
  if(preferences.microphone&&Recorder.GetSystemAudioCaptureDevices().Count==0)throw new Exception("No microphone detected, disable ’My Voice’ to record");
  if(preferences.systemAudio&&Recorder.GetSystemAudioLoopbackDevices().Count==0)preferences.systemAudio=false;
  Directory.CreateDirectory(preferences.folder);if(FreeBytes()<1024L*1024*1024)throw new Exception("Insufficient space remaining on save location 1 GB, Please change the folder");
  string probe=Path.Combine(preferences.folder,".write-test-"+Guid.NewGuid());using(File.Create(probe)){}File.Delete(probe);Save();
  busy=true;paused=stopping=cancelled=false;error=null;file=Path.Combine(preferences.folder,"Recorder_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".mp4");frames=lastFrames=0;previousStamp=-1;lateIntervals=0;longestInterval=0;callbackMetric=new RecorderMetric();clock.Reset();
  finish=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  try{
   for(int i=preferences.countdown;i>0;i--){state="countdown";emit(new{state=state,remaining=i,busy=true});await Task.Delay(1000);if(closing||cancelled)throw new Exception("Recording has been canceled");}
   if(recorder!=null){recorder.Dispose();recorder=null;}
   var captureApi=Get(args,"captureApi","wgc")=="dda"?RecorderApi.DesktopDuplication:RecorderApi.WindowsGraphicsCapture;
   if(mode!="window"&&!Recorder.GetDisplays().Any(d=>d.DeviceName==target.device))throw new Exception("Current display device does not support recording, please place the area on a physical monitor and reselect.");
   RecordingSourceBase source=mode=="window"?(RecordingSourceBase)new WindowRecordingSource(new IntPtr(target.handle)){IsCursorCaptureEnabled=preferences.cursor}:new DisplayRecordingSource(target.device){RecorderApi=captureApi,IsCursorCaptureEnabled=preferences.cursor,IsBorderRequired=false};
   source.IsVideoFramePreviewEnabled=false;if(mode=="region")source.SourceRect=region;
   outputWidth=preset=="4k"?3840:preset=="1080"?1920:0;outputHeight=preset=="4k"?2160:preset=="1080"?1080:0;
   if(outputWidth>0&&mode!="window"&&(mode=="region"?region.Height>region.Width:target.height>target.width)){int width=outputWidth;outputWidth=outputHeight;outputHeight=width;}
   var audio=new List<AudioSourceBase>();if(preferences.systemAudio)audio.Add(string.IsNullOrEmpty(preferences.outputDevice)?LoopbackAudioSource.Default:new LoopbackAudioSource(preferences.outputDevice));if(preferences.microphone)audio.Add(string.IsNullOrEmpty(preferences.inputDevice)?CaptureAudioSource.Default:new CaptureAudioSource(preferences.inputDevice){ForceMono=true});
   var options=new RecorderOptions{
    SourceOptions=new SourceOptions{RecordingSources=new List<RecordingSourceBase>{source}},
    OutputOptions=new OutputOptions{RecorderMode=RecorderMode.Video,OutputFrameSize=outputWidth>0?new ScreenSize(outputWidth,outputHeight):ScreenSize.Empty,Stretch=StretchMode.Uniform,IsVideoFramePreviewEnabled=false},
    VideoEncoderOptions=new VideoEncoderOptions{Framerate=preferences.fps,IsFixedFramerate=true,IsHardwareEncodingEnabled=true,IsLowLatencyEnabled=true,IsThrottlingDisabled=false,Quality=80,Bitrate=(preset=="4k"?55:18)*1000000,Encoder=new H264VideoEncoder{BitrateMode=H264BitrateControlMode.Quality,EncoderProfile=H264Profile.High},IsFragmentedMp4Enabled=true,IsMp4FastStartEnabled=false},
    AudioOptions=new AudioOptions{IsAudioEnabled=audio.Count>0,AudioSources=audio,Bitrate=AudioBitrate.bitrate_192kbps,Channels=AudioChannels.Stereo},
    MouseOptions=new MouseOptions{IsMousePointerEnabled=preferences.cursor},
    LogOptions=new LogOptions{IsLogEnabled=true,LogFilePath=Path.Combine(data,"engine.log"),LogSeverityLevel=LogLevel.Info}
   };
   if(outputWidth==0){var dimensions=Recorder.GetOutputDimensionsForRecordingSources(options.SourceOptions.RecordingSources).CombinedOutputSize;outputWidth=(int)dimensions.Width;outputHeight=(int)dimensions.Height;}
   recorder=Recorder.CreateRecorder(options);
   recorder.OnFrameRecorded+=(s,e)=>{long callbackStart=Stopwatch.GetTimestamp();lock(gate){frames=e.FrameNumber;if(previousStamp>=0&&!paused){double interval=e.Timestamp-previousStamp;if(interval>1000d/preferences.fps*1.5)lateIntervals++;longestInterval=Math.Max(longestInterval,interval);}previousStamp=e.Timestamp;callbackMetric.Add((Stopwatch.GetTimestamp()-callbackStart)*1000d/Stopwatch.Frequency);}};
   recorder.OnStatusChanged+=(s,e)=>{if(e.Status==RecorderStatus.Recording){clock.Start();state="recording";started.TrySetResult(true);}else if(e.Status==RecorderStatus.Finishing)state="saving";};
   recorder.OnRecordingComplete+=(s,e)=>Complete(true,null);
   recorder.OnRecordingFailed+=(s,e)=>{started.TrySetException(new Exception(e.Error));Complete(false,e.Error);};
   SetWindowDisplayAffinity(owner.Handle,0);await Task.Run(()=>recorder.Record(file));
   if(await Task.WhenAny(started.Task,Task.Delay(20000))!=started.Task)throw new Exception("Recording startup timeout, please reopen the target window and retry");await started.Task;
   if(!busy)throw new Exception(error??"Recording failed to start");timer.Start();CreateControls(target);if(preferences.minimize&&!testing)owner.WindowState=FormWindowState.Minimized;
   EmitStatus();return Status();
  }catch(Exception ex){if(recorder!=null){var abort=Task.Run(()=>recorder.Stop());try{await new RecorderFinalization(2000).Wait(abort,2000,"Recording component did not exit in time");}catch{}}Complete(false,ex.Message);throw;}
 }
 public object Status(){return new{busy=busy,state=state,paused=paused,seconds=Math.Round(clock.Elapsed.TotalSeconds,1),frames=frames,fps=preferences.fps,lateIntervals=lateIntervals,path=file,error=error,folder=preferences.folder,saveTimeoutMs=20000,recoveryFolder=state=="saving"||state=="failed"?Path.GetDirectoryName(file??data):null};}
 void EmitStatus(){emit(Status());}
 void Tick(){if(!busy)return;int rate=frames-lastFrames;lastFrames=frames;long size=0;try{size=new FileInfo(file).Length;}catch{}emit(new{busy=busy,state=state,paused=paused,seconds=Math.Round(clock.Elapsed.TotalSeconds,1),frames=frames,outputFps=rate,fps=preferences.fps,lateIntervals=lateIntervals,megabytes=Math.Round(size/1048576d,1),path=file});if(bar!=null&&!bar.IsDisposed)bar.UpdateState(clock.Elapsed,paused,stopping);if(tray!=null)tray.Text="Xiaoma Player + Recorder · "+(paused?"Paused ":"Recording in progress ")+clock.Elapsed.ToString(@"hh\:mm\:ss");if(!stopping&&(FreeBytes()<512L*1024*1024||preferences.stopMinutes>0&&clock.Elapsed.TotalMinutes>=preferences.stopMinutes))StopFromControls();}
 long FreeBytes(){try{return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(preferences.folder))).AvailableFreeSpace;}catch{return long.MaxValue;}}
 public object Pause(){if(!busy||stopping)return Status();if(paused){previousStamp=-1;recorder.Resume();clock.Start();paused=false;state="recording";}else{recorder.Pause();clock.Stop();paused=true;state="paused";}EmitStatus();if(bar!=null)bar.UpdateState(clock.Elapsed,paused,false);return Status();}
 public async Task<object> Stop(){
  if(finish==null)return Status();if(busy&&state=="countdown"){cancelled=true;Complete(false,"Recording canceled");return await finish.Task;}
  if(busy&&!stopping){stopping=true;state="saving";clock.Stop();EmitStatus();if(bar!=null)bar.UpdateState(clock.Elapsed,paused,true);stopTask=Task.Run(()=>recorder.Stop());}
  try{await new RecorderFinalization(20000).Wait(Task.WhenAll(stopTask??Task.CompletedTask,finish.Task),20000,"Encoder did not finish in time; recording data is retained. You can retry recording.");}
  catch(Exception ex){Complete(false,ex.Message);if(stopTask!=null)_ = stopTask.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
  return await finish.Task;
 }
 async void StopFromControls(){try{await Stop();}catch{EmitStatus();}}

 void Complete(bool success,string message){
  lock(gate){if(!busy)return;busy=false;clock.Stop();error=message;state=success?"complete":"failed";}
  double avg=clock.Elapsed.TotalSeconds>0?frames/clock.Elapsed.TotalSeconds:0;
  var report=new{success=success,path=file,error=message,width=outputWidth,height=outputHeight,frames=frames,targetFps=preferences.fps,activeSeconds=clock.Elapsed.TotalSeconds,averageOutputFps=Math.Round(avg,2),lateIntervals=lateIntervals,longestFrameIntervalMs=longestInterval,hardwareEncodingRequested=true,frameCallbackWorkMs=callbackMetric.Snapshot(),measurement="Callback output compared with wall-clock timing. Budget interval is not exactly the frame drop count from source video; frame rate of file and scene refresh rate should be evaluated separately."};
  try{string reports=Path.Combine(preferences.folder,"Recording reports");Directory.CreateDirectory(reports);File.WriteAllText(Path.Combine(reports,Path.GetFileName(file)+".json"),json.Serialize(report));}catch{}
  finish.TrySetResult(report);
  OnUi(()=>{timer.Stop();SetWindowDisplayAffinity(owner.Handle,0);if(bar!=null){bar.Dispose();bar=null;}if(tray!=null){tray.Dispose();tray=null;}EmitStatus();if(success&&!closing){owner.WindowState=FormWindowState.Normal;owner.Show();open(file);}else if(!closing){owner.WindowState=FormWindowState.Normal;owner.Show();}});
 }
 void CreateControls(RecorderSource target){if(testing||controllerControls)return;
  bar=new RecorderBar(()=>Pause(),()=>StopFromControls());var screen=target.device==null?Screen.FromControl(owner):Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==target.device)??Screen.PrimaryScreen;bar.Location=new Point(screen.WorkingArea.Right-bar.Width-16,screen.WorkingArea.Top+16);bar.Show();
  tray=new NotifyIcon{Icon=owner.Icon,Text="Xiaoma Player + Recorder · Recording in progress",Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("Pause / Resume",null,(s,e)=>Pause());menu.Items.Add("End and review",null,(s,e)=>StopFromControls());menu.Items.Add("Playback Player",null,(s,e)=>{owner.WindowState=FormWindowState.Normal;owner.Show();owner.Activate();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>{owner.WindowState=FormWindowState.Normal;owner.Show();owner.Activate();};
 }
 public void Dispose(){closing=true;timer.Stop();timer.Dispose();if(bar!=null)bar.Dispose();if(tray!=null)tray.Dispose();if(recorder!=null)recorder.Dispose();}
}
