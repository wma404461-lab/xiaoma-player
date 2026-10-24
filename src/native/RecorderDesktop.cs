using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

sealed class RecorderDpiScope : IDisposable {
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 readonly IntPtr previous;
 public RecorderDpiScope(){previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}
 public void Dispose(){if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
}
static class RecorderDesktop {
 [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Point point);
 public static Point Cursor(){Point point;return GetPhysicalCursorPos(out point)?point:Point.Empty;}
 public static Rectangle[] Screens(){return RecorderSources.Displays().Select(d=>new Rectangle(RecorderSources.PhysicalOrigin(d.device),new Size(d.width,d.height))).ToArray();}
 public static Rectangle Bounds(){var screens=Screens();if(screens.Length==0)throw new Exception("No display available for recording");return screens.Aggregate(Rectangle.Union);}
 public static Dictionary<string,object> Region(Rectangle bounds){return new Dictionary<string,object>{{"x",bounds.X},{"y",bounds.Y},{"width",bounds.Width/2*2},{"height",bounds.Height/2*2},{"display","virtual"},{"virtualDesktop",true}};}
 public static Rectangle Resolve(Dictionary<string,object> box,string display){
  int x=Convert.ToInt32(box["x"]),y=Convert.ToInt32(box["y"]),w=Convert.ToInt32(box["width"])/2*2,h=Convert.ToInt32(box["height"])/2*2;
  bool desktop=box.ContainsKey("virtualDesktop")&&Convert.ToBoolean(box["virtualDesktop"]);if(!desktop){var screen=RecorderSources.Displays().FirstOrDefault(d=>d.id==display)??RecorderSources.Displays().FirstOrDefault();if(screen==null)throw new Exception("No display available for recording");var origin=RecorderSources.PhysicalOrigin(screen.device);if(x<0||y<0||x+w>screen.width||y+h>screen.height)throw new Exception("Recording area has expired, please reselect");x+=origin.X;y+=origin.Y;}
  var bounds=new Rectangle(x,y,w,h);var screens=Screens();if(w<32||h<32||!Bounds().Contains(bounds)||!screens.Any(s=>s.IntersectsWith(bounds)))throw new Exception("Recording area has expired, please reselect");return bounds;
 }
 public static Rectangle ControlArea(Rectangle selection,Rectangle[] screens){
  var visible=screens.Select(s=>Rectangle.Intersect(selection,s)).Where(r=>r.Width>0&&r.Height>0).ToArray();
  return visible.Length==0?selection:visible.OrderBy(r=>Math.Abs(selection.Right-r.Right)+Math.Abs(selection.Bottom-r.Bottom)).ThenByDescending(r=>r.Width*r.Height).First();
 }
 public static Rectangle ControlBounds(Rectangle selection,Size size,Rectangle[] screens){
  int w=Math.Min(size.Width,selection.Width),h=Math.Min(size.Height,selection.Height),gapX=Math.Min(8,Math.Max(0,(selection.Width-w)/2)),gapY=Math.Min(8,Math.Max(0,(selection.Height-h)/2));
  return new Rectangle(selection.Right-w-gapX,selection.Bottom-h-gapY,w,h);
 }
}
