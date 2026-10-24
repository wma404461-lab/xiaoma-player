using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

class Frame { public string path,name,url,ext; public long number,size; public double mtime; }
class Item {
 public string id,kind,name,extension,folder,path,url,sequenceKey;
 public long size,first,last,span,missingCount; public double mtime;
 public List<Frame> frames; public List<long[]> missing;
}
class NativeCatalog {
 [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)] static extern int StrCmpLogicalW(string a,string b);
 readonly Func<string,string> urlFor;
 public Dictionary<string,Item> items=new Dictionary<string,Item>();
 readonly Dictionary<string,double> revisions=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
 static readonly Dictionary<string,string> kinds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 static readonly Regex framePattern=new Regex(@"^(.*?)(\d+)([^\d]*)(\.[^.]+)$",RegexOptions.CultureInvariant);
 static NativeCatalog(){
  Add("image","png jpg jpeg webp gif bmp avif svg ico tif tiff tga exr hdr");
  Add("video","mp4 mov m4v webm mkv avi ogv mpeg mpg");
  Add("model","glb gltf obj fbx stl ply 3mf"); Add("audio","mp3 wav flac m4a aac ogg opus");
  Add("document","pdf doc docx docm rtf ppt pptx pptm pps ppsx xls xlsx xlsm odt odp ods"); Add("text","txt md log csv tsv json");
 }
 static void Add(string kind,string exts){foreach(string ext in exts.Split(' '))kinds["."+ext]=kind;}
 public static string Kind(string file){string value;return kinds.TryGetValue(Path.GetExtension(file),out value)?value:null;}
 public static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant().Substring(0,24);}
 public NativeCatalog(Func<string,string> url){urlFor=url;}
 public static Tuple<string,long,string,string> Parse(string file){
  var m=framePattern.Match(Path.GetFileName(file)); long number;
  if(!m.Success||m.Groups[1].Length==0||Kind(file)!="image"||new[]{".gif",".svg",".ico"}.Contains(m.Groups[4].Value.ToLowerInvariant())||!long.TryParse(m.Groups[2].Value,out number)||number>9007199254740991L)return null;
  string ext=m.Groups[4].Value.ToLowerInvariant(),prefix=m.Groups[1].Value,suffix=m.Groups[3].Value;
  return Tuple.Create(prefix+suffix,number,ext,Path.GetDirectoryName(file).ToLowerInvariant()+"\0"+prefix.ToLowerInvariant()+"\0"+(suffix.Length==0?"":suffix.ToLowerInvariant()+"\0")+ext);
 }
 static string Family(Tuple<string,long,string,string> parsed){return parsed.Item4.Substring(0,parsed.Item4.Length-parsed.Item3.Length);}
 Frame FrameFor(string file,long number=0){double revision;return new Frame{path=file,name=Path.GetFileName(file),url=urlFor(file),ext=Path.GetExtension(file).ToLowerInvariant(),number=number,mtime=revisions.TryGetValue(file,out revision)?revision:0};}
 List<Item> Group(IEnumerable<string> files,Dictionary<string,Tuple<string,long,string,string>> parsedFiles=null){
  var groups=new Dictionary<string,List<Tuple<string,Tuple<string,long,string,string>>>>();var plain=new List<string>();var result=new List<Item>();
  foreach(string file in files){Tuple<string,long,string,string> parsed;if(parsedFiles==null||!parsedFiles.TryGetValue(file,out parsed)){parsed=Parse(file);if(parsedFiles!=null)parsedFiles[file]=parsed;}if(parsed==null){plain.Add(file);continue;}List<Tuple<string,Tuple<string,long,string,string>>> list;if(!groups.TryGetValue(parsed.Item4,out list))groups[parsed.Item4]=list=new List<Tuple<string,Tuple<string,long,string,string>>>();list.Add(Tuple.Create(file,parsed));}
  foreach(var group in groups){var list=group.Value.OrderBy(x=>x.Item2.Item2).ToList();long first=list[0].Item2.Item2,last=list[list.Count-1].Item2.Item2,span=last-first+1;
   if(list.Count<2||span>100000||span<=0||(double)list.Count/span<.1||list.Select(x=>x.Item2.Item2).Distinct().Count()!=list.Count){plain.AddRange(list.Select(x=>x.Item1));continue;}
   var missing=new List<long[]>();for(int i=1;i<list.Count;i++)if(list[i].Item2.Item2>list[i-1].Item2.Item2+1)missing.Add(new[]{list[i-1].Item2.Item2+1,list[i].Item2.Item2-1});
   var frames=list.Select(x=>FrameFor(x.Item1,x.Item2.Item2)).ToList();string head=list[0].Item1,name=Regex.Replace(list[0].Item2.Item1,@"[._ -]+$","");
   result.Add(new Item{id=Hash("sequence:"+group.Key),kind="sequence",name=name.Length>0?name:"Sequence frames",extension=list[0].Item2.Item3,frames=frames,first=first,last=last,span=span,missing=missing,missingCount=span-list.Count,folder=Path.GetDirectoryName(head),path=head,url=frames[0].url,sequenceKey=group.Key,mtime=frames.Max(f=>f.mtime)});
  }
  foreach(string file in plain){var frame=FrameFor(file);result.Add(new Item{id=Hash(file.ToLowerInvariant()),kind=Kind(file),name=frame.name,extension=frame.ext,folder=Path.GetDirectoryName(file),path=file,url=frame.url,mtime=frame.mtime});}
  result.Sort((a,b)=>{int directory=StrCmpLogicalW(a.folder,b.folder);if(directory!=0)return directory;int name=StrCmpLogicalW(a.name,b.name);return name!=0?name:StrCmpLogicalW(a.extension,b.extension);});return result;
 }
 public object Scan(string[] selected){
  if(selected.Length>5000)throw new Exception("Too many files");
  var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var errors=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var expanded=new HashSet<string>();bool capped=false;
  var parsedFiles=new Dictionary<string,Tuple<string,long,string,string>>(StringComparer.OrdinalIgnoreCase);var directories=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
  Func<string,Tuple<string,long,string,string>> parse=file=>{Tuple<string,long,string,string> value;if(!parsedFiles.TryGetValue(file,out value))parsedFiles[file]=value=Parse(file);return value;};
  Func<string,string[]> siblings=directory=>{string[] value;if(!directories.TryGetValue(directory,out value))directories[directory]=value=Directory.GetFiles(directory);return value;};
  Action<string> add=file=>{if(files.Count>=50000){capped=true;return;}if(Kind(file)!=null)files.Add(file);};
  Action<string,int> walk=null;walk=(directory,depth)=>{if(depth>8||capped||!seen.Add(directory))return;try{foreach(string file in Directory.EnumerateFiles(directory)){if(capped)break;if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)add(file);}foreach(string child in Directory.EnumerateDirectories(directory)){if(capped)break;if(!Path.GetFileName(child).StartsWith(".")&&Path.GetFileName(child)!="node_modules"&&(File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)walk(child,depth+1);}}catch{errors.Add("Cannot read folder "+Path.GetFileName(directory));}};
  foreach(string input in selected){try{if(!Path.IsPathRooted(input))continue;string file=Path.GetFullPath(input);if(Directory.Exists(file)){walk(file,0);continue;}if(!File.Exists(file)){errors.Add("Not found "+Path.GetFileName(file));continue;}if(Kind(file)==null){errors.Add("Not supported yet "+Path.GetExtension(file)+": "+Path.GetFileName(file));continue;}add(file);var parsed=parse(file);if(parsed!=null&&expanded.Add(Family(parsed)))foreach(string sibling in siblings(Path.GetDirectoryName(file))){if(capped)break;var other=parse(sibling);if(other!=null&&Family(other)==Family(parsed))add(sibling);}}catch{errors.Add("Not found "+Path.GetFileName(input));}}
  var grouped=Group(files,parsedFiles);string primary=selected.FirstOrDefault(p=>Kind(p)!=null);if(primary!=null)try{primary=Path.GetFullPath(primary);}catch{primary=null;}
  Func<Item,bool> matches=i=>String.Equals(i.path,primary,StringComparison.OrdinalIgnoreCase)||(i.frames!=null&&i.frames.Any(f=>String.Equals(f.path,primary,StringComparison.OrdinalIgnoreCase)));
  // Opening one image or video brings its immediate siblings into the list. Sequences stay grouped.
  var primaryItem=grouped.FirstOrDefault(i=>matches(i));string galleryKind=primaryItem==null?null:primaryItem.kind;
  if(selected.Length==1&&(galleryKind=="image"||galleryKind=="video")){try{foreach(string sibling in siblings(Path.GetDirectoryName(primary))){if(capped)break;if(Kind(sibling)==galleryKind&&(File.GetAttributes(sibling)&FileAttributes.ReparsePoint)==0)add(sibling);}grouped=Group(files,parsedFiles);}catch{errors.Add("Cannot read other assets in the same folder");}}
  items=grouped.ToDictionary(i=>i.id);int index=primary==null?0:grouped.FindIndex(i=>matches(i));
  if(capped)errors.Add("Too many files, only first 50000 files loaded");return new{items=grouped,selectedIndex=Math.Max(0,index),errors=errors,fileCount=files.Count};
 }
 public Item Get(string id){Item item;if(!items.TryGetValue(id,out item))throw new Exception("File has been switched, please reopen");return item;}
 public Item AddFile(string file){file=Path.GetFullPath(file);if(!File.Exists(file)||Kind(file)==null)throw new Exception("Saved fragment temporarily unavailable");var item=Group(new[]{file}).Single();item.size=new FileInfo(file).Length;items[item.id]=item;return item;}
 public Item Refresh(string id){var previous=Get(id);if(previous.kind!="sequence")return null;var matching=new List<string>();var parsedFiles=new Dictionary<string,Tuple<string,long,string,string>>();foreach(string file in Directory.EnumerateFiles(previous.folder)){var parsed=Parse(file);if(parsed!=null&&parsed.Item4==previous.sequenceKey){matching.Add(file);parsedFiles[file]=parsed;}}var result=Group(matching,parsedFiles).FirstOrDefault(i=>i.id==id);if(result!=null)items[id]=result;return result;}
 public void NoteChange(string file){lock(revisions)revisions[file]=DateTime.UtcNow.Subtract(new DateTime(1970,1,1)).TotalMilliseconds;}
}
