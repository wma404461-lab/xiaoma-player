using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;
class UpdateHelper {
  [DllImport("shell32.dll")] static extern void SHChangeNotify(uint events,uint flags,IntPtr item1,IntPtr item2);
  static string Full(string value){return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);}
  static bool Within(string root,string value){return Full(value).StartsWith(Full(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
  static void NoLinks(string root,string value){for(string current=Full(value);Within(root,current)||current.Equals(Full(root),StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current)){if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new Exception("更新路径不能包含链接");}}
  static void ValidApp(string root){string file=Path.Combine(root,"resources","app","package.json");var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));if((string)data["name"]!="yiyan-viewer"||!File.Exists(Path.Combine(root,"小马播放器.exe")))throw new Exception("这不是小马播放器+录屏目录");}
  static string Hash(string file){using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  static void Prepare(string zip,string stage,string sha,string installed=null,string recipe=null){
    var timer=Stopwatch.StartNew();
    if(Hash(zip)!=sha.ToLowerInvariant())throw new Exception("更新包校验失败");
    double verifyMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
    if(Directory.Exists(stage))throw new Exception("更新暂存目录已存在");
    Directory.CreateDirectory(stage);long total=0;int count=0;
    using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries){
      string name=entry.FullName.Replace('/',Path.DirectorySeparatorChar),file=Full(Path.Combine(stage,name));
      if(!Within(stage,file)||name.StartsWith(".data",StringComparison.OrdinalIgnoreCase)||name.Split(Path.DirectorySeparatorChar).Any(p=>p==".."||p.Contains(":"))||(entry.ExternalAttributes>>16&0xF000)==0xA000)throw new Exception("更新包包含无效路径");
      total+=entry.Length;if(total>1500000000L||++count>25000)throw new Exception("更新包大小超出限制");
      if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(file);continue;}
      Directory.CreateDirectory(Path.GetDirectoryName(file));entry.ExtractToFile(file,false);
    }
    double extractMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
    if(recipe!=null){var plan=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(recipe));UpdateResources.Materialize(installed,stage,UpdateResources.Parse(plan["inherit"]));var package=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(stage,"resources","app","package.json")));if(Convert.ToString(package["version"])!=Convert.ToString(plan["version"]))throw new Exception("继承资源版本不正确");}
    ValidApp(stage);Console.WriteLine(new JavaScriptSerializer().Serialize(new{Success=true,archiveVerifyMs=verifyMs,extractMs=extractMs,reuseMs=timer.Elapsed.TotalMilliseconds,reused=recipe!=null}));
  }
  static void CopyAtomic(string source,string target){
    Directory.CreateDirectory(Path.GetDirectoryName(target));string temporary=target+".xiaoma-new";
    for(int tries=0;;tries++)try{File.Copy(source,temporary,true);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);return;}catch(IOException){if(tries>=30)throw;Thread.Sleep(1000);}
  }
  static void Install(string stage,string target,int pid){
    target=Full(target);stage=Full(stage);ValidApp(target);ValidApp(stage);
    string updates=Path.Combine(target,".data","updates");if(!Within(updates,stage))throw new Exception("暂存目录位置不正确");
    NoLinks(target,target);NoLinks(target,stage);
    try{var process=Process.GetProcessById(pid);string running=Full(process.MainModule.FileName);if(!new[]{"小马播放器.exe","小马播放器+录屏.exe","小马播放器核心.exe"}.Any(name=>running.Equals(Full(Path.Combine(target,name)),StringComparison.OrdinalIgnoreCase)))throw new Exception("更新目标进程不正确");if(!process.WaitForExit(45000))throw new Exception("播放器尚未退出，请稍后重试");}catch(ArgumentException){}
    var installTimer=Stopwatch.StartNew();string transaction=Path.GetDirectoryName(stage);string backup=Path.Combine(transaction,"backup");Directory.CreateDirectory(backup);var changed=new List<string>();int unchanged=0;Process restarted=null;
    try{
      foreach(string source in Directory.GetFiles(stage,"*",SearchOption.AllDirectories)){
        string relative=source.Substring(stage.Length+1);if(relative.StartsWith(".data",StringComparison.OrdinalIgnoreCase))throw new Exception("更新不能覆盖个人数据");
        string destination=Full(Path.Combine(target,relative));if(!Within(target,destination))throw new Exception("更新路径越界");NoLinks(target,destination);
        if(File.Exists(destination)&&new FileInfo(source).Length==new FileInfo(destination).Length&&Hash(source)==Hash(destination)){unchanged++;continue;}
        if(File.Exists(destination)){string saved=Path.Combine(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(destination,saved,false);}
        changed.Add(relative);CopyAtomic(source,destination);
      }
      double copyMs=installTimer.Elapsed.TotalMilliseconds;SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);string entry=File.Exists(Path.Combine(target,"小马播放器+录屏.exe"))?"小马播放器+录屏.exe":"小马播放器.exe";restarted=Process.Start(new ProcessStartInfo(Path.Combine(target,entry)){WorkingDirectory=target,UseShellExecute=true});
      File.WriteAllText(Path.Combine(transaction,"install-metrics.json"),new JavaScriptSerializer().Serialize(new{success=true,copyMs=copyMs,changedFiles=changed.Count,unchangedFiles=unchanged,restartPid=restarted.Id,restartRequestedAt=DateTime.UtcNow.ToString("o")}));
      File.WriteAllText(Path.Combine(transaction,"success.txt"),DateTime.UtcNow.ToString("o"));
      // These are the verified, generated update folders; keep the user's profile.
      foreach(string generated in new[]{stage,backup})try{if(!Within(updates,generated))throw new Exception("清理路径越界");NoLinks(target,generated);Directory.Delete(generated,true);}catch{}
      try{string downloaded=Path.Combine(Path.GetDirectoryName(stage),"release.zip");NoLinks(target,downloaded);File.Delete(downloaded);}catch{}
    }catch{
      if(restarted!=null)try{if(!restarted.HasExited)restarted.Kill();}catch{}
      foreach(string relative in changed.AsEnumerable().Reverse()){string destination=Path.Combine(target,relative),saved=Path.Combine(backup,relative);try{if(File.Exists(saved))CopyAtomic(saved,destination);else if(File.Exists(destination))File.Delete(destination);}catch{}}
      throw;
    }
  }
  [STAThread] static int Main(string[] args){
    try{if(args.Length==4&&args[0]=="prepare")Prepare(args[1],args[2],args[3]);else if(args.Length==6&&args[0]=="prepare-reuse")Prepare(args[1],args[2],args[3],args[4],args[5]);else if(args.Length==4&&args[0]=="install")Install(args[1],args[2],int.Parse(args[3]));else throw new Exception("无效更新参数");return 0;}
    catch(Exception error){if(args.Length>0&&args[0]=="install"&&Environment.GetEnvironmentVariable("YIYAN_TEST_MODE")==null)MessageBox.Show("更新未完成，已尝试恢复原版。\n"+error.Message,"小马播放器+录屏",MessageBoxButtons.OK,MessageBoxIcon.Information);else Console.WriteLine(new JavaScriptSerializer().Serialize(new{Success=false,Error=error.Message}));return 1;}
  }
}
