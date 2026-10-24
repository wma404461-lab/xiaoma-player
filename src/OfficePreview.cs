using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
class OfficePreview {
  [STAThread] static int Main(string[] args) {
    dynamic app=null,doc=null; object security=null,alerts=null; bool owned=false;
    var before=Process.GetProcesses().Select(p=>p.Id).ToArray();
    try {
      if(args.Length!=2||!File.Exists(args[0]))throw new Exception("Invalid document path");
      string ext=Path.GetExtension(args[0]).ToLowerInvariant(),kind;
      if(new[]{".doc",".docx",".docm",".rtf",".odt"}.Contains(ext))kind="Word";
      else if(new[]{".ppt",".pptx",".pptm",".pps",".ppsx",".odp"}.Contains(ext))kind="PowerPoint";
      else if(new[]{".xls",".xlsx",".xlsm",".ods"}.Contains(ext))kind="Excel";
      else throw new Exception("This document format is not supported");
      app=Activator.CreateInstance(Type.GetTypeFromProgID(kind+".Application",true));
      int count=kind=="Word"?(int)app.Documents.Count:kind=="PowerPoint"?(int)app.Presentations.Count:(int)app.Workbooks.Count;
      if(count>0)throw new Exception("Please close the currently used Office document before previewing");
      owned=Process.GetProcesses().Any(p=>!before.Contains(p.Id)&&new[]{"WINWORD","POWERPNT","EXCEL","wps","wpp","et"}.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase));
      security=app.AutomationSecurity;app.AutomationSecurity=3;alerts=app.DisplayAlerts;
      if(kind=="Word") {
        app.DisplayAlerts=0;
        doc=app.Documents.Open(args[0],ConfirmConversions:false,ReadOnly:true,AddToRecentFiles:false,Visible:false);
        doc.ExportAsFixedFormat(args[1],17);
        doc.Close(0);doc=null;
      }else if(kind=="PowerPoint"){
        app.DisplayAlerts=1;
        doc=app.Presentations.Open(args[0],-1,0,0);
        try{doc.ExportAsFixedFormat(args[1],2);}catch{doc.SaveAs(args[1],32);}
        doc.Close();doc=null;
      }else{
        app.DisplayAlerts=false;app.AskToUpdateLinks=false;
        doc=app.Workbooks.Open(args[0],0,true);
        doc.ExportAsFixedFormat(0,args[1]);doc.Close(false);doc=null;
      }
      if(!File.Exists(args[1]))throw new Exception("Document preview was not generated");
      Console.WriteLine("{\"Success\":true}");return 0;
    }catch(Exception error){Console.WriteLine(new JavaScriptSerializer().Serialize(new{Success=false,Error=error.Message}));return 1;}
    finally{
      if(doc!=null){try{doc.Close(0);}catch{} try{Marshal.FinalReleaseComObject(doc);}catch{}}
      if(app!=null){try{if(security!=null)app.AutomationSecurity=security;if(alerts!=null)app.DisplayAlerts=alerts;}catch{} if(owned){try{app.Quit();}catch{}}try{Marshal.FinalReleaseComObject(app);}catch{}}
    }
  }
}
