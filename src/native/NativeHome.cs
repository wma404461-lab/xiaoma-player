using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

partial class NativeMain {
 Panel home;Label homeTitle,homeNote;bool initializing;string homeAction;
 readonly Dictionary<string,int> startupPhases=new Dictionary<string,int>();bool homePainted,viewerRequested;Timer viewerWarmup;
 void BuildHome(){
  home=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(11,18,32)};Controls.Add(home);
  home.Paint+=(s,e)=>{if(!homePainted){homePainted=true;WriteStartup("home-painted");}};
  var header=new Panel{Dock=DockStyle.Top,Height=58,BackColor=Color.FromArgb(17,29,48)};
  var logo=new PictureBox{Image=Icon.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(18,16),Size=new Size(26,26)};
  var name=new Label{Text="小马播放器+录屏",ForeColor=Color.FromArgb(231,232,236),Font=new Font("Microsoft YaHei UI",10),Location=new Point(54,19),AutoSize=true};header.Controls.Add(logo);header.Controls.Add(name);
  var open=HomeButton("文件",90);var folder=HomeButton("文件夹",90);var record=HomeButton("● 录屏",90);((GlassButton)record).RecordingAccent=true;record.ForeColor=Color.White;header.Controls.Add(open);header.Controls.Add(folder);header.Controls.Add(record);header.Resize+=(s,e)=>{folder.Location=new Point(header.ClientSize.Width-110,12);open.Location=new Point(header.ClientSize.Width-208,12);record.Location=new Point(header.ClientSize.Width-306,12);};open.Click+=(s,e)=>HomeChoose(false);folder.Click+=(s,e)=>HomeChoose(true);record.Click+=(s,e)=>{homeAction="record";EnsureViewer();};home.Controls.Add(header);
  var footer=new Panel{Dock=DockStyle.Bottom,Height=38,BackColor=Color.FromArgb(17,29,48)};
  var keys=new Label{Text="支持直接拖入文件与文件夹",AutoSize=true,ForeColor=Color.FromArgb(176,190,208),Location=new Point(18,11)};
  var update=new LinkLabel{Text="检查更新 · "+version,AutoSize=true,LinkColor=Color.FromArgb(153,158,169),ActiveLinkColor=Color.White};footer.Controls.Add(keys);footer.Controls.Add(update);footer.Resize+=(s,e)=>update.Location=new Point(footer.ClientSize.Width-update.Width-18,11);update.LinkClicked+=(s,e)=>{homeAction="update";EnsureViewer();};home.Controls.Add(footer);
  homeTitle=new Label{Text="拖入文件，开始查看",ForeColor=Color.FromArgb(231,232,236),Font=new Font("Microsoft YaHei UI",18),TextAlign=ContentAlignment.MiddleCenter,Size=new Size(600,50)};
  homeNote=new Label{Text="文件、文件夹，或序列中的任意一帧",ForeColor=Color.FromArgb(176,190,208),Font=new Font("Microsoft YaHei UI",10),TextAlign=ContentAlignment.MiddleCenter,Size=new Size(600,32)};
  var types=new Label{Text="图片       视频       音频       序列帧       模型       文档",ForeColor=Color.FromArgb(159,176,198),Font=new Font("Microsoft YaHei UI",10),TextAlign=ContentAlignment.MiddleCenter,Size=new Size(600,38)};
  var illustration=new Panel{Size=new Size(72,72),BackColor=home.BackColor};illustration.Paint+=(s,e)=>{var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(78,83,93),1.3f)){g.DrawRectangle(pen,15,15,42,42);g.DrawLines(pen,new[]{new Point(16,46),new Point(28,33),new Point(40,44),new Point(47,37),new Point(56,46)});g.DrawEllipse(pen,41,23,8,8);}};
  var centerOpen=HomeButton("也可以选择文件",152);centerOpen.Height=40;centerOpen.BackColor=Color.FromArgb(224,226,230);centerOpen.ForeColor=Color.FromArgb(237,244,255);centerOpen.Click+=(s,e)=>HomeChoose(false);home.Controls.Add(illustration);home.Controls.Add(homeTitle);home.Controls.Add(homeNote);home.Controls.Add(types);home.Controls.Add(centerOpen);
  home.Resize+=(s,e)=>{int x=(home.ClientSize.Width-600)/2,y=home.ClientSize.Height/2-146;illustration.Location=new Point((home.ClientSize.Width-72)/2,y);homeTitle.Location=new Point(x,y+89);homeNote.Location=new Point(x,y+140);types.Location=new Point(x,y+180);centerOpen.Location=new Point((home.ClientSize.Width-152)/2,y+239);};
  AttachHomeDrop(home);KeyPreview=true;KeyDown+=(s,e)=>{if(home!=null&&home.Visible&&e.Control&&e.KeyCode==Keys.O){e.SuppressKeyPress=true;HomeChoose(e.Shift);}};
 }
 Button HomeButton(string text,int width){var button=new GlassButton{Text=text,Width=width,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(29,31,37),ForeColor=Color.FromArgb(205,208,216),Font=new Font("Microsoft YaHei UI",10),Cursor=Cursors.Hand};button.FlatAppearance.BorderSize=0;return button;}
 void AttachHomeDrop(Control control){control.AllowDrop=true;control.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};control.DragDrop+=(s,e)=>{var paths=e.Data.GetData(DataFormats.FileDrop) as string[];if(paths!=null)OpenPaths(paths);};foreach(Control child in control.Controls)AttachHomeDrop(child);}
 void HomeChoose(bool folder){if(folder){string selected=NativeFolderPicker.Show(HomeDialogOwner(),"选择要查看的文件夹");if(selected!=null)OpenPaths(new[]{selected});}else{using(var dialog=new OpenFileDialog{Title="选择文件",Multiselect=true})if(dialog.ShowDialog(HomeDialogOwner())==DialogResult.OK)OpenPaths(dialog.FileNames);}}
 void WarmViewerAfterPaint(){
  viewerRequested=pending.Length>0;viewerWarmup=new Timer{Interval=viewerRequested?30:120};
  viewerWarmup.Tick+=(s,e)=>{viewerWarmup.Stop();viewerWarmup.Dispose();viewerWarmup=null;if(!IsDisposed){if(!headless&&!fastHomeDormant)Activate();EnsureViewer(false);}};viewerWarmup.Start();
 }
 async void EnsureViewer(bool requested=true){
  if(IsDisposed)return;if(requested)viewerRequested=true;if(ready){ShowViewer();return;}if(initializing)return;
  initializing=true;try{view=new Microsoft.Web.WebView2.WinForms.WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(11,18,32)};Controls.Add(view);if(home!=null)home.BringToFront();await Initialize();}
  catch(Exception error){initializing=false;if(thumbnails!=null){thumbnails.Dispose();thumbnails=null;}if(clipper!=null){clipper.Dispose();clipper=null;}if(exporter!=null){exporter.Dispose();exporter=null;}if(server!=null){server.Dispose();server=null;}if(view!=null){Controls.Remove(view);view.Dispose();view=null;}if(IsDisposed)return;File.WriteAllText(Path.Combine(profile,"startup-error.txt"),error.ToString());if(directOpening)DirectStartupError(error);else if(viewerRequested){homeTitle.Text="文件暂时无法打开";homeNote.Text="点击文件或检查更新可重试";}}
 }
 void ViewerReady(){initializing=false;try{File.Delete(Path.Combine(profile,"startup-error.txt"));}catch{}WriteStartup("viewer-ready");ShowViewer();}
 void ShowViewer(){
  if(!ready||!viewerRequested||contentPending)return;if(directOpening||home!=null&&home.Visible){if(home!=null)home.Visible=false;view.BringToFront();RevealFromFastHome();RevealDirectOpen();WriteStartup("viewer-shown");}
  if(homeAction=="update"||homeAction=="record"){string button=homeAction=="record"?"record-screen":"check-update";homeAction=null;view.CoreWebView2.ExecuteScriptAsync("document.getElementById('"+button+"').click()");}
 }
 void WriteStartup(string phase){try{int elapsed=(int)(DateTime.Now-Process.GetCurrentProcess().StartTime).TotalMilliseconds;startupPhases[phase]=elapsed;File.WriteAllText(Path.Combine(profile,"startup-timing.json"),json.Serialize(new{phase=phase,elapsedMs=elapsed,pid=Process.GetCurrentProcess().Id,version=version,phases=startupPhases,homeVisible=home!=null&&home.Visible}));}catch{}}
}
