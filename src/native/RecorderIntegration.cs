using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
partial class NativeMain {
 NativeRecorder recorder;NativeClipper clipper;bool recorderClosing;
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
 void InitializeRecorderLifecycle(){
  HandleCreated+=(s,e)=>{if(!testing){RegisterHotKey(Handle,6101,0x4003,0x52);RegisterHotKey(Handle,6102,0x4003,0x50);}};
  FormClosing+=async(s,e)=>{if(recorderClosing||recorder==null||!recorder.Busy)return;e.Cancel=true;try{await recorder.Stop();if(!recorder.Busy){recorderClosing=true;BeginInvoke(new Action(Close));}}catch(Exception ex){MessageBox.Show(this,ex.Message,"Recording is still saving");}};
  FormClosed+=(s,e)=>{UnregisterHotKey(Handle,6101);UnregisterHotKey(Handle,6102);if(recorder!=null)recorder.Dispose();};
 }
 protected override void WndProc(ref Message message){
  if(message.Msg==0x312){if(message.WParam.ToInt32()==6101){if(ready&&(home==null||!home.Visible))Emit("recorder-hotkey","toggle");else{homeAction="record";EnsureViewer();}}else if(message.WParam.ToInt32()==6102&&recorder!=null&&recorder.Busy)recorder.Pause();}
  base.WndProc(ref message);
 }
}
