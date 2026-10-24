using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Keep the standard Button keyboard/accessibility behavior; paint only the skin.
sealed class GlassButton : Button {
 bool hover,pressed,recordingAccent;
 public bool RecordingAccent{get{return recordingAccent;}set{if(recordingAccent!=value){recordingAccent=value;Invalidate();}}}
 public GlassButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;ForeColor=Color.FromArgb(237,244,255);}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){pressed=true;Invalidate();}base.OnKeyDown(e);}
 protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
 protected override void OnLostFocus(EventArgs e){pressed=false;Invalidate();base.OnLostFocus(e);}
 static GraphicsPath Pill(RectangleF r){var p=new GraphicsPath();float d=r.Height;p.AddArc(r.X,r.Y,d,d,90,180);p.AddArc(r.Right-d,r.Y,d,d,270,180);p.CloseFigure();return p;}
 protected override void OnPaint(PaintEventArgs e){
  if(SystemInformation.HighContrast){base.OnPaint(e);return;}
  var g=e.Graphics;g.Clear(Parent==null?Color.FromArgb(17,29,48):Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
  float inset=1.5f;var r=new RectangleF(inset,inset,Math.Max(1,Width-inset*2-1),Math.Max(1,Height-inset*2-1));
  using(var path=Pill(r)){
   Color top=pressed?Color.FromArgb(31,48,68):hover?Color.FromArgb(48,65,85):Color.FromArgb(36,51,70);
   Color bottom=pressed?Color.FromArgb(19,32,49):hover?Color.FromArgb(38,54,73):Color.FromArgb(29,43,61);
   if(recordingAccent){top=pressed?Color.FromArgb(139,38,56):hover?Color.FromArgb(183,57,78):Color.FromArgb(166,47,67);bottom=pressed?Color.FromArgb(112,28,42):Color.FromArgb(132,35,50);}
   using(var fill=new LinearGradientBrush(r,top,bottom,LinearGradientMode.Vertical))g.FillPath(fill,path);
   using(var edge=new Pen(Color.FromArgb(45,208,228,255)))g.DrawPath(edge,path);

  }
  var text=Rectangle.Round(r);if(pressed)text.Offset(0,1);
  TextRenderer.DrawText(g,Text,Font,text,Enabled?ForeColor:Color.FromArgb(131,149,171),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
  if(Focused&&ShowFocusCues){var focus=Rectangle.Round(r);focus.Inflate(-4,-4);ControlPaint.DrawFocusRectangle(g,focus,ForeColor,BackColor);}
 }
}
