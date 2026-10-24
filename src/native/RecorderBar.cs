using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Windows.Forms;

sealed class RecorderBar : RecorderOverlayForm {
 readonly Label time;readonly GlassButton primary,stop,exit;
 readonly Timer animation=new Timer{Interval=33};readonly Stopwatch clock=Stopwatch.StartNew();
 readonly ContextMenuStrip compactMenu=new ContextMenuStrip();readonly ToolStripMenuItem startItem,pauseItem,stopItem,exitItem;
 readonly Action onPause,onStop,onExit,onStart;
 bool activeState,pausedState,processing,compact,motionAllowed=ReadMotionSetting();int lastMotionPoll;string phase="idle";float layoutScale=1;long paints;double maxPaintMs;
 // These app effects were explicitly enabled by the user; remote desktop's
 // client-area animation policy must not silently turn them off.
 static bool ReadMotionSetting(){return !SystemInformation.HighContrast;}
 public RecorderBar(Action onPause,Action onStop,Action onExit=null,Action onStart=null){
  this.onPause=onPause;this.onStop=onStop;this.onExit=onExit;this.onStart=onStart;
  Text="录屏控制条";FormBorderStyle=FormBorderStyle.None;TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Size=new Size(338,46);BackColor=Color.FromArgb(17,29,48);ForeColor=Color.White;DoubleBuffered=true;
  time=new Label{Text="已框选",AutoSize=true,ForeColor=ForeColor,Font=new Font("Segoe UI",9)};
  primary=Make();stop=Make();exit=Make();Controls.Add(time);Controls.Add(primary);Controls.Add(stop);Controls.Add(exit);
  primary.Click+=(s,e)=>{if(compact){compactMenu.Show(primary,new Point(0,primary.Height));return;}PrimaryAction();};
  stop.Click+=(s,e)=>StopAction();exit.Click+=(s,e)=>ExitAction();
  startItem=new ToolStripMenuItem("开始录制",null,(s,e)=>PrimaryAction());pauseItem=new ToolStripMenuItem("暂停",null,(s,e)=>PrimaryAction());stopItem=new ToolStripMenuItem("结束并保存",null,(s,e)=>StopAction());exitItem=new ToolStripMenuItem("退出",null,(s,e)=>ExitAction());compactMenu.Items.AddRange(new ToolStripItem[]{startItem,pauseItem,stopItem,exitItem});
  UpdatePhase(TimeSpan.Zero,onStart==null,false,onStart==null?"recording":"idle");
  animation.Tick+=(s,e)=>{if(unchecked(Environment.TickCount-lastMotionPoll)>1000){lastMotionPoll=Environment.TickCount;motionAllowed=ReadMotionSetting();animation.Interval=motionAllowed?33:1000;}if(Visible&&motionAllowed)Invalidate();};
  Shown+=(s,e)=>{NativeRecorder.SetWindowDisplayAffinity(Handle,0x11);animation.Start();};
 }
 GlassButton Make(){return new GlassButton{FlatStyle=FlatStyle.Flat,BackColor=BackColor,ForeColor=Color.White,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9)};}
 void PrimaryAction(){if(processing)return;if(activeState){primary.Enabled=false;pauseItem.Enabled=false;onPause();}else if(onStart!=null)onStart();}
 void StopAction(){if(!activeState||processing)return;primary.Enabled=stop.Enabled=exit.Enabled=false;onStop();}
 void ExitAction(){if(phase=="saving")return;if(onExit!=null)onExit();else onStop();}
 public Size FitInside(Rectangle selection){
  compact=selection.Width<300||selection.Height<62;
  Size size=compact?new Size(Math.Max(16,Math.Min(118,selection.Width-4)),Math.Max(16,Math.Min(38,selection.Height-4))):new Size(Math.Min(338,selection.Width-16),46);
  layoutScale=compact?1:size.Width/338f;LayoutControls(size);return size;
 }
 void LayoutControls(Size size){
  time.Visible=!compact;stop.Visible=!compact&&activeState&&!processing;exit.Visible=!compact;
  if(compact){primary.Bounds=new Rectangle(2,2,Math.Max(8,size.Width-4),Math.Max(8,size.Height-4));primary.Text=size.Width>=88?(phase=="saving"?"保存中":phase=="preparing"?"准备中":activeState?(pausedState?"● 暂停":"● 录制"):"● 录屏"):"●";primary.AccessibleName=time.Text+"，录屏控制菜单";primary.Enabled=true;return;}
  int x=(int)(115*layoutScale),y=8;
  time.Location=new Point((int)(24*layoutScale),14);primary.Bounds=new Rectangle(x,y,(int)((activeState&&!processing?68:138)*layoutScale),30);
  stop.Bounds=new Rectangle((int)(188*layoutScale),y,(int)(68*layoutScale),30);exit.Bounds=new Rectangle((int)(261*layoutScale),y,(int)(68*layoutScale),30);
  primary.AccessibleName=primary.Text;foreach(Control c in new Control[]{time,primary,stop,exit}){float pt=9*layoutScale;if(Math.Abs(c.Font.Size-pt)>.01){var old=c.Font;c.Font=new Font("Segoe UI",pt);old.Dispose();}}
 }
 static GraphicsPath Outline(RectangleF r){float d=14;var p=new GraphicsPath();p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 Color Signal{get{return processing?Color.FromArgb(142,199,255):pausedState?Color.FromArgb(255,193,91):Color.FromArgb(255,89,117);}}
 protected override void OnPaint(PaintEventArgs e){
  long stamp=Stopwatch.GetTimestamp();base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
  bool motion=motionAllowed;double seconds=motion?clock.Elapsed.TotalSeconds:0;float wave=motion?(float)(.5+.5*Math.Sin(seconds*Math.PI)):1;
  Color glow=activeState?Signal:Color.FromArgb(108,207,255);
  if(Width>18&&Height>18)using(var path=Outline(new RectangleF(2,2,Width-5,Height-5))){using(var soft=new Pen(Color.FromArgb(35+(int)(wave*40),glow),4))g.DrawPath(soft,path);using(var edge=new Pen(Color.FromArgb(105+(int)(wave*70),glow),1))g.DrawPath(edge,path);using(var flow=new Pen(Color.FromArgb(210,glow),2)){flow.DashPattern=new float[]{8,24};flow.DashOffset=motion?-(float)(seconds*18):0;g.DrawPath(flow,path);}}
  if(!compact){using(var halo=new SolidBrush(Color.FromArgb(40+(int)(wave*35),Signal)))g.FillEllipse(halo,7,16,16,16);using(var dot=new SolidBrush(Signal))g.FillEllipse(dot,10,19,10,10);}
  paints++;maxPaintMs=Math.Max(maxPaintMs,(Stopwatch.GetTimestamp()-stamp)*1000d/Stopwatch.Frequency);
 }
 public void UpdateState(TimeSpan elapsed,bool paused,bool saving){UpdateState(elapsed,true,paused,saving);}
 public void UpdateState(TimeSpan elapsed,bool active,bool paused,bool saving){UpdatePhase(elapsed,active,paused,saving?"saving":active?(paused?"paused":"recording"):"idle");}
 public void UpdatePhase(TimeSpan elapsed,bool active,bool paused,string state){
  phase=state;activeState=active;pausedState=paused;processing=state=="preparing"||state=="saving";
  time.Text=state=="preparing"?"准备中":state=="saving"?"保存中":active?elapsed.ToString(@"hh\:mm\:ss"):"已框选";
  primary.Text=state=="preparing"?"准备中…":state=="saving"?"保存中…":!active?"开始录制":paused?"继续":"暂停";primary.Enabled=!processing;primary.RecordingAccent=(!active||compact)&&!processing;
  stop.Text="结束";stop.Enabled=active&&!processing;stop.RecordingAccent=true;exit.Text="退出";exit.Enabled=state!="saving";
  startItem.Visible=!active;startItem.Text=state=="preparing"?"正在准备…":"开始录制";startItem.Enabled=!processing;pauseItem.Visible=active;pauseItem.Text=state=="saving"?"正在保存…":state=="preparing"?"正在准备…":paused?"继续":"暂停";pauseItem.Enabled=active&&!processing;stopItem.Visible=active&&!processing;stopItem.Enabled=active&&!processing;exitItem.Enabled=state!="saving";
  LayoutControls(Size);Invalidate();
 }
 public object ControlState(){return new{primaryText=primary.Text,primaryEnabled=primary.Enabled,stopVisible=stop.Visible,stopEnabled=stop.Enabled,exitEnabled=exit.Enabled,phase=phase,timeText=time.Text,compact=compact,menuStatus=processing?pauseItem.Text:"",startAvailable=!activeState&&!processing,pauseAvailable=activeState&&!processing,stopAvailable=activeState&&!processing,redPrimary=primary.RecordingAccent,redStop=stop.RecordingAccent,motionEnabled=motionAllowed,paints=paints,maxPaintMs=maxPaintMs};}
 public void ClickForTest(string action){
  if(compact){ToolStripMenuItem item=action=="start"?startItem:action=="pause"?pauseItem:action=="stop"?stopItem:action=="exit"?exitItem:null;if(item==null||!item.Available||!item.Enabled)throw new InvalidOperationException("当前菜单操作不可用："+action+" / "+phase+" / available="+(item!=null&&item.Available)+" / enabled="+(item!=null&&item.Enabled));item.PerformClick();return;}
  Button b=action=="start"&&!activeState?primary:action=="pause"&&activeState?primary:action=="stop"?stop:action=="exit"?exit:null;if(b==null||!b.Visible||!b.Enabled)throw new InvalidOperationException("当前按钮不可用："+action);b.PerformClick();
 }
 public void SavePreview(string file){using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(file,ImageFormat.Png);}}
 protected override void Dispose(bool disposing){if(disposing){animation.Stop();animation.Dispose();compactMenu.Dispose();}base.Dispose(disposing);}
}
