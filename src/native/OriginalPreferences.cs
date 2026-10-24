using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

partial class NativeMain {
 async Task ImportOriginalPreferences(CoreWebView2 core){
  string file=Path.Combine(profile,"legacy-preferences.json");if(!File.Exists(file))return;
  try{if(new FileInfo(file).Length>1024*1024)return;var saved=json.Deserialize<Dictionary<string,string>>(File.ReadAllText(file));var preferences=new Dictionary<string,string>();foreach(var entry in saved)if(Regex.IsMatch(entry.Key,@"^(player-volume|player-muted|export-size|export-target-mb|fps:[a-f0-9]{24})$")&&entry.Value!=null&&entry.Value.Length<80)preferences[entry.Key]=entry.Value;
   await core.AddScriptToExecuteOnDocumentCreatedAsync("(()=>{try{if(localStorage.getItem('original-preferences-imported'))return;const saved="+json.Serialize(preferences)+";for(const [key,value] of Object.entries(saved))if(localStorage.getItem(key)===null)localStorage.setItem(key,value);localStorage.setItem('original-preferences-imported','true');}catch{}})();");
  }catch(Exception error){File.WriteAllText(Path.Combine(profile,"preferences-import-error.txt"),error.Message);}
 }
}
