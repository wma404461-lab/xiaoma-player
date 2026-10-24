using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

// This digest is shared by the native client and the external installer.
// A reused resource is verified both before download and after copying.
static class UpdateResources {
 public static string Full(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);}
 static void ValidateRelative(string relative){
  if(String.IsNullOrEmpty(relative)||relative.StartsWith(".")||relative.StartsWith("/")||relative.StartsWith("\\")||relative.Contains(":"))throw new Exception("Invalid resource inheritance path");
  string[] pieces=relative.Replace('\\','/').Split('/');if(pieces.Any(p=>p==""||p=="."||p==".."||p.Equals(".data",StringComparison.OrdinalIgnoreCase)))throw new Exception("Invalid resource inheritance path");
 }
 public static string Resolve(string root,string relative){
  ValidateRelative(relative);string[] pieces=relative.Replace('\\','/').Split('/');
  string file=Full(Path.Combine(root,String.Join(Path.DirectorySeparatorChar.ToString(),pieces)));if(!file.StartsWith(Full(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Resource path out of bounds");NoLinks(root,file);return file;
 }
 public static void NoLinks(string root,string file){
  string limit=Full(root);for(string current=Full(file);current.Equals(limit,StringComparison.OrdinalIgnoreCase)||current.StartsWith(limit+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current)){
   if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new Exception("Update path cannot contain links");
  }
 }
 public static string Hash(string file){using(var source=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant();}
 static IEnumerable<string> Files(string folder){
  NoLinks(folder,folder);foreach(string file in Directory.GetFiles(folder)){NoLinks(folder,file);yield return file;}
  foreach(string child in Directory.GetDirectories(folder)){NoLinks(folder,child);foreach(string file in Files(child))yield return file;}
 }
 public static Dictionary<string,object> Digest(string root,string relative,string kind){
  string file=Resolve(root,relative);long size=0;int count=0;string digest;
  if(kind=="file"){size=new FileInfo(file).Length;count=1;digest=Hash(file);}
  else if(kind=="tree-v1"){
   var entries=Files(file).Select(p=>new{path=p,name=p.Substring(file.Length+1).Replace('\\','/')}).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray();
   using(var sha=SHA256.Create()){
    foreach(var entry in entries){long bytes=new FileInfo(entry.path).Length;size+=bytes;count++;if(count>10000||size>1500000000L)throw new Exception("Inherited resource too large");
     byte[] line=Encoding.UTF8.GetBytes(entry.name+"\0"+bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\0"+Hash(entry.path)+"\n");sha.TransformBlock(line,0,line.Length,null,0);
    }sha.TransformFinalBlock(new byte[0],0,0);digest=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
   }
  }else throw new Exception("Unsupported resource inheritance format");
  return new Dictionary<string,object>{{"path",relative},{"kind",kind},{"size",size},{"files",count},{"sha256",digest}};
 }
 public static List<Dictionary<string,object>> Parse(object value){
  var list=new List<Dictionary<string,object>>();var paths=new List<string>();
  foreach(object item in (IEnumerable)value){var resource=item as Dictionary<string,object>;if(resource==null)throw new Exception("Invalid inherited resource information");string path=Convert.ToString(resource["path"]),kind=Convert.ToString(resource["kind"]),sha=Convert.ToString(resource["sha256"]);long size=Convert.ToInt64(resource["size"]);int files=Convert.ToInt32(resource["files"]);
   ValidateRelative(path);if((kind!="file"&&kind!="tree-v1")||size<0||size>1500000000L||files<1||files>10000||!System.Text.RegularExpressions.Regex.IsMatch(sha,"^[a-fA-F0-9]{64}$"))throw new Exception("Invalid inherited resource information");
   string normalized=path.Replace('\\','/');if(paths.Any(p=>p.Equals(normalized,StringComparison.OrdinalIgnoreCase)||p.StartsWith(normalized+"/",StringComparison.OrdinalIgnoreCase)||normalized.StartsWith(p+"/",StringComparison.OrdinalIgnoreCase)))throw new Exception("Duplicate inherited resource path");paths.Add(normalized);list.Add(resource);if(list.Count>64)throw new Exception("Too many inherited resources");
  }if(list.Count==0)throw new Exception("Missing inherited resource");return list;
 }
 public static bool Matches(string root,Dictionary<string,object> expected){try{var actual=Digest(root,Convert.ToString(expected["path"]),Convert.ToString(expected["kind"]));return Convert.ToInt64(actual["size"])==Convert.ToInt64(expected["size"])&&Convert.ToInt32(actual["files"])==Convert.ToInt32(expected["files"])&&String.Equals(Convert.ToString(actual["sha256"]),Convert.ToString(expected["sha256"]),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 public static void Materialize(string root,string stage,List<Dictionary<string,object>> resources){
  foreach(var resource in resources){string relative=Convert.ToString(resource["path"]),kind=Convert.ToString(resource["kind"]);if(!Matches(root,resource))throw new Exception("Original runtime resource has changed; please use a complete update package");string source=Resolve(root,relative),destination=Resolve(stage,relative);
   if(File.Exists(destination)||Directory.Exists(destination))throw new Exception("Update package overlaps with inherited resource");
   if(kind=="file"){Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(source,destination,false);}
   else{Directory.CreateDirectory(destination);foreach(string file in Files(source)){string target=Resolve(destination,file.Substring(source.Length+1).Replace('\\','/'));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,false);}}
   if(!Matches(stage,resource))throw new Exception("Validation failed after copying runtime resource");
  }
 }
}
