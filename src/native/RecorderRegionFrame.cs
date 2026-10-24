using System;
using System.Drawing;
using System.Windows.Forms;
sealed class RecorderRegionFrame : RecorderOverlayForm {
 public Rectangle SelectionBounds{get;private set;}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var value=base.CreateParams;value.ExStyle|=0x20|0x80|0x08000000;return value;}}
 public RecorderRegionFrame(Rectangle bounds){SelectionBounds=bounds;Text="Recording area prompt";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Place(Rectangle.Inflate(bounds,3,3));ShowInTaskbar=false;TopMost=true;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=false;AutoScaleMode=AutoScaleMode.None;var outline=new Region(new Rectangle(0,0,bounds.Width+6,bounds.Height+6));outline.Exclude(new Rectangle(3,3,bounds.Width,bounds.Height));Region=outline;}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(104,229,190),3))e.Graphics.DrawRectangle(pen,1,1,Math.Max(1,ClientSize.Width-3),Math.Max(1,ClientSize.Height-3));}
}
