using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class RecorderWorker {
 sealed class Host : Form {protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}}
 internal static int Run(string[] args){
  Console.SetIn(new StreamReader(Console.OpenStandardInput(),new System.Text.UTF8Encoding(false)));Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true});Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var json=new JavaScriptSerializer();var outputGate=new object();Action<object> send=value=>{lock(outputGate){Console.WriteLine(json.Serialize(value));Console.Out.Flush();}};
  using(var host=new Host{Text="Xiaoma Player + Recorder · Recording component",Icon=new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resources","app","assets","icon.ico")),ShowInTaskbar=false}){
   var handle=host.Handle;NativeRecorderEngine engine=null;bool exiting=false;
   Func<Task> close=async()=>{if(exiting)return;exiting=true;try{if(engine!=null&&engine.Busy)await engine.Stop();}catch{}if(engine!=null)engine.Dispose();Application.ExitThread();};
   Task.Run(async()=>{try{string line;while((line=await Console.In.ReadLineAsync())!=null){if(line.Length>65536)continue;var message=json.Deserialize<Dictionary<string,object>>(line);host.BeginInvoke(new Action(async()=>{int id=Convert.ToInt32(message["id"]);try{if(engine==null)engine=new NativeRecorderEngine(host,args[1],args[2]=="1",value=>send(new{status=value}),path=>send(new{open=path}));string method=Convert.ToString(message["method"]);object result;if(method=="start")result=await engine.Start((Dictionary<string,object>)message["args"]);else if(method=="prepare")result=engine.Prepare();else if(method=="pause")result=engine.Pause();else if(method=="stop")result=await engine.Stop();else result=engine.Status();send(new{id=id,result=result});}catch(Exception error){send(new{id=id,error=error.Message});}}));}}catch(Exception error){Console.Error.WriteLine(error);}finally{if(!host.IsDisposed)host.BeginInvoke(new Action(async()=>await close()));}});
   using(var parentTimer=new Timer{Interval=2000}){parentTimer.Tick+=async(s,e)=>{try{using(var parent=Process.GetProcessById(int.Parse(args[3])))if(parent.HasExited)await close();}catch(ArgumentException){await close();}};parentTimer.Start();Application.Run();}
  }return 0;
 }
}
