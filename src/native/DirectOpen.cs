using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Globalization;
using System.Threading.Tasks;

partial class NativeMain {
 bool directOpening,contentPending,scalingUi;
 void ConfigureDirectOpen(){directOpening=pending.Length>0&&!fastHomeDormant;contentPending=pending.Length>0;if(directOpening){homePainted=true;WriteStartup("direct-open");}}
 void RevealDirectOpen(){if(!directOpening)return;directOpening=false;if(!headless)Bounds=NativeWindowLayout.Current();ApplyUiScale();ShowInTaskbar=!headless;if(!headless&&!testing)Activate();}
 async void ApplyUiScale(){await SyncUiScale();}
 async Task SyncUiScale(){if(view==null||view.CoreWebView2==null||scalingUi)return;scalingUi=true;try{double ratio;string measured=await view.CoreWebView2.ExecuteScriptAsync("window.devicePixelRatio");if(IsDisposed||!double.TryParse(measured,NumberStyles.Float,CultureInfo.InvariantCulture,out ratio)||ratio<=0)return;double zoom=NativeWindowLayout.UiZoom(Screen.FromControl(this).Bounds,ratio/view.ZoomFactor);if(Math.Abs(view.ZoomFactor-zoom)>.001)view.ZoomFactor=zoom;}catch{}finally{scalingUi=false;}}
 void ContentReady(){contentPending=false;WriteStartup("content-ready");ShowViewer();NativeEncoder.Warm(appRoot);}
 void DirectStartupError(Exception error){
  var panel=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(12,13,15)};
  var message=new Label{Dock=DockStyle.Fill,Text="File temporarily cannot be opened\n"+error.Message,ForeColor=Color.FromArgb(220,223,230),Font=new Font("Segoe UI",14),TextAlign=ContentAlignment.MiddleCenter};panel.Controls.Add(message);Controls.Add(panel);panel.BringToFront();RevealDirectOpen();WriteStartup("direct-open-error");
 }
}
