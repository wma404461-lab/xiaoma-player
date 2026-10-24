using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

// The full desktop overlay is painted once. Only four narrow edges move while dragging.
sealed class RecorderPickerEdge : RecorderOverlayForm {
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var value=base.CreateParams;value.ExStyle|=0x20|0x80|0x08000000;return value;}}
 public RecorderPickerEdge(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(104,229,190);}
}
sealed class RecorderRegionPicker : RecorderOverlayForm {
 Point origin;Rectangle selected;bool dragging;readonly RecorderPickerEdge[] edges=new RecorderPickerEdge[4];readonly Form hint;readonly RecorderMetric previewMetric=new RecorderMetric();
 public Rectangle Selection{get{return selected;}}public object PreviewWork(){return previewMetric.Snapshot();}
 public double MaxPreviewMs{get;private set;}public int PreviewUpdates{get;private set;}
 public RecorderRegionPicker(Screen screen):this(screen.Bounds){}
 public RecorderRegionPicker(Rectangle bounds){
  Text="Select recording area";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Place(bounds);ShowInTaskbar=false;TopMost=true;BackColor=Color.Black;Opacity=.16;Cursor=Cursors.Cross;KeyPreview=true;
  for(int i=0;i<edges.Length;i++)edges[i]=new RecorderPickerEdge();
  hint=new Form{Text="Selection prompt",FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,ShowInTaskbar=false,TopMost=true,AutoScaleMode=AutoScaleMode.None,BackColor=Color.FromArgb(21,22,25),ForeColor=Color.FromArgb(231,232,236),Size=new Size(320,42)};
  hint.Controls.Add(new Label{Text="Drag to adjust recording area · Esc Cancel",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",10)});
  Shown+=(s,e)=>{var screen=RecorderDesktop.Screens();Point p=RecorderDesktop.Cursor();Rectangle target=Array.Find(screen,r=>r.Contains(p));if(target.IsEmpty)target=screen[0];hint.Location=new Point(target.Left+24,target.Top+24);hint.Show();for(int i=0;i<edges.Length;i++){edges[i].Place(new Rectangle(bounds.Left-20,bounds.Top-20,1,1));edges[i].Show();}};
  KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}};
  MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;origin=RecorderDesktop.Cursor();dragging=true;Capture=true;};
  MouseMove+=(s,e)=>{if(!dragging)return;Point p=RecorderDesktop.Cursor();UpdatePreview(Rectangle.FromLTRB(Math.Min(origin.X,p.X),Math.Min(origin.Y,p.Y),Math.Max(origin.X,p.X),Math.Max(origin.Y,p.Y)));};
  MouseUp+=(s,e)=>{if(!dragging)return;dragging=false;Capture=false;if(selected.Width<32||selected.Height<32)return;DialogResult=DialogResult.OK;Close();};
 }
 public void UpdatePreview(Rectangle bounds){
  long start=Stopwatch.GetTimestamp();selected=bounds;const int line=2;var positions=new[]{new Rectangle(bounds.Left,bounds.Top,Math.Max(1,bounds.Width),line),new Rectangle(bounds.Left,bounds.Bottom-line,Math.Max(1,bounds.Width),line),new Rectangle(bounds.Left,bounds.Top,line,Math.Max(1,bounds.Height)),new Rectangle(bounds.Right-line,bounds.Top,line,Math.Max(1,bounds.Height))};
  for(int i=0;i<edges.Length;i++){edges[i].Place(positions[i]);if(!edges[i].Visible)edges[i].Show();}PreviewUpdates++;double elapsed=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;previewMetric.Add(elapsed);MaxPreviewMs=Math.Max(MaxPreviewMs,elapsed);
 }
 protected override void Dispose(bool disposing){if(disposing){foreach(var edge in edges)if(edge!=null)edge.Dispose();if(hint!=null)hint.Dispose();}base.Dispose(disposing);}
}
