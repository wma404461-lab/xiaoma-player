const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

const imageExtensions = new Set(['.png','.jpg','.jpeg','.webp','.gif','.bmp','.avif','.svg','.ico','.tif','.tiff','.tga','.exr','.hdr']);
const videoExtensions = new Set(['.mp4','.mov','.m4v','.webm','.mkv','.avi','.ogv','.mpeg','.mpg']);
const modelExtensions = new Set(['.glb','.gltf','.obj','.fbx','.stl','.ply','.3mf']);
const audioExtensions = new Set(['.mp3','.wav','.flac','.m4a','.aac','.ogg','.opus']);
const documentExtensions = new Set(['.pdf','.doc','.docx','.docm','.rtf','.ppt','.pptx','.pptm','.pps','.ppsx','.xls','.xlsx','.xlsm','.odt','.odp','.ods']);
const textExtensions = new Set(['.txt','.md','.log','.csv','.tsv','.json']);
const collator = new Intl.Collator('zh-CN', {numeric:true,sensitivity:'base'});
const idFor = value => crypto.createHash('sha256').update(value).digest('hex').slice(0,24);
function kindFor(file) {const ext=path.extname(file).toLowerCase();return imageExtensions.has(ext)?'image':videoExtensions.has(ext)?'video':modelExtensions.has(ext)?'model':audioExtensions.has(ext)?'audio':documentExtensions.has(ext)?'document':textExtensions.has(ext)?'text':null;}
function frameName(file) {
  const match=path.basename(file).match(/^(.*?)(\d+)([^\d]*)(\.[^.]+)$/);
  if(!match || !match[1] || !imageExtensions.has(match[4].toLowerCase()) || ['.gif','.svg','.ico'].includes(match[4].toLowerCase())) return null;
  const number=Number(match[2]);
  if(!Number.isSafeInteger(number))return null;
  const familyKey=path.dirname(file).toLowerCase()+'\0'+match[1].toLowerCase()+'\0'+(match[3]?match[3].toLowerCase()+'\0':'');
  return {prefix:match[1]+match[3],digits:match[2],number,extension:match[4].toLowerCase(),familyKey,key:familyKey+match[4].toLowerCase()};
}
function groupFiles(files, urlFor) {
  const groups=new Map(), plain=[];
  for(const file of files){const parsed=file.kind==='image'?frameName(file.path):null;if(parsed){if(!groups.has(parsed.key))groups.set(parsed.key,[]);groups.get(parsed.key).push({...file,...parsed});}else plain.push(file);}
  const output=[];
  for(const [key,list] of groups){
    list.sort((a,b)=>a.number-b.number||collator.compare(a.path,b.path));
    const first=list[0].number,last=list.at(-1).number,span=last-first+1;
    const unique=new Set(list.map(f=>f.number));
    if(list.length<2 || unique.size!==list.length || span>100000 || list.length/span<0.1){plain.push(...list);continue;}
    const missing=[];for(let i=1;i<list.length;i++)if(list[i].number>list[i-1].number+1)missing.push([list[i-1].number+1,list[i].number-1]);
    const frames=list.map(f=>({path:f.path,name:path.basename(f.path),url:urlFor(f.path),number:f.number,size:f.size,mtime:f.mtime,ext:f.extension}));
    const head=list[0];
    output.push({id:idFor('sequence:'+key),kind:'sequence',name:head.prefix.replace(/[._ -]+$/,'')||'Sequence frames',extension:head.extension,frames,first,last,span,missing,missingCount:span-list.length,folder:path.dirname(head.path),path:head.path,url:frames[0].url,size:list.reduce((s,f)=>s+f.size,0),mtime:Math.max(...list.map(f=>f.mtime)),sequenceKey:key});
  }
  for(const f of plain)output.push({id:idFor(f.path.toLowerCase()),kind:f.kind,name:path.basename(f.path),extension:path.extname(f.path).toLowerCase(),path:f.path,folder:path.dirname(f.path),url:urlFor(f.path),size:f.size,mtime:f.mtime});
  return output.sort((a,b)=>collator.compare(a.folder,b.folder)||collator.compare(a.name,b.name));
}

class Catalog {
  constructor(urlFor){this.urlFor=urlFor;this.items=new Map();this.versions=new Map();this.revision=Date.now();}
  noteChange(folder,filename){this.versions.set((filename?path.join(folder,String(filename)):folder).toLowerCase(),++this.revision);}
  async scan(inputPaths){
    const selected=[...new Set(inputPaths.filter(p=>typeof p==='string'&&path.isAbsolute(p)).map(p=>path.resolve(p)))];
    const files=new Map(),errors=[],seenDirectories=new Set();let capped=false;
    const add=(p,stat=null)=>{if(files.size>=50000){capped=true;return;}const kind=kindFor(p),key=p.toLowerCase();if(kind&&!files.has(key))files.set(key,{path:p,size:stat?.size||0,mtime:stat?.mtimeMs||0,kind});};
    const walk=async (directory,depth=0)=>{
      const key=directory.toLowerCase();if(seenDirectories.has(key)||depth>8||capped)return;seenDirectories.add(key);
      try{const entries=await fs.readdir(directory,{withFileTypes:true});for(const entry of entries){if(capped)break;if(entry.name.startsWith('.')||entry.name==='node_modules')continue;const p=path.join(directory,entry.name);if(entry.isDirectory())await walk(p,depth+1);else if(entry.isFile()&&kindFor(p))add(p);}}catch{errors.push('Cannot read folder '+path.basename(directory));}
    };
    const expanded=new Set(),directoryEntries=new Map();
    const entriesFor=async folder=>{if(!directoryEntries.has(folder))directoryEntries.set(folder,await fs.readdir(folder,{withFileTypes:true}));return directoryEntries.get(folder);};
    for(const p of selected){
      try{const stat=await fs.stat(p);if(stat.isDirectory()){await walk(p);continue;}if(!kindFor(p)){errors.push('Not supported yet '+(path.extname(p)||'This type')+': '+path.basename(p));continue;}add(p,stat);
        const parsed=frameName(p);if(parsed&&!expanded.has(parsed.familyKey)){expanded.add(parsed.familyKey);const names=await entriesFor(path.dirname(p));for(const name of names){if(!name.isFile())continue;const sibling=path.join(path.dirname(p),name.name),other=frameName(sibling);if(other?.familyKey===parsed.familyKey)add(sibling);}}
      }catch{errors.push('Not found '+path.basename(p));}
    }
    let items=groupFiles([...files.values()],this.urlFor);
    const primary=selected.find(p=>kindFor(p));
    const matches=i=>primary&&(i.path.toLowerCase()===primary.toLowerCase()||i.frames?.some(f=>f.path.toLowerCase()===primary.toLowerCase()));
    const galleryKind=items.find(matches)?.kind;
    if(selected.length===1&&['image','video'].includes(galleryKind)){
      try{const folder=path.dirname(primary),entries=await entriesFor(folder);for(const entry of entries){if(capped)break;if(entry.isFile()&&kindFor(entry.name)===galleryKind)add(path.join(folder,entry.name));}items=groupFiles([...files.values()],this.urlFor);}catch{errors.push('Cannot read other assets in the same folder');}
    }
    const index=primary?items.findIndex(matches):0;
    this.items=new Map(items.map(i=>[i.id,i]));
    if(capped)errors.push('Too many files, only first 50000 files loaded');
    return {items,selectedIndex:Math.max(0,index),errors,fileCount:files.size};
  }
  async refreshSequence(id){
    const previous=this.items.get(id);if(previous?.kind!=='sequence')return null;
    const names=await fs.readdir(previous.folder,{withFileTypes:true}),files=[],old=new Map(previous.frames.map(frame=>[frame.path.toLowerCase(),frame]));
    for(const name of names){if(!name.isFile())continue;const p=path.join(previous.folder,name.name),key=p.toLowerCase();if(frameName(p)?.key!==previous.sequenceKey)continue;const before=old.get(key);files.push({path:p,size:before?.size||0,mtime:this.versions.get(key)||this.versions.get(previous.folder.toLowerCase())||before?.mtime||0,kind:'image'});}
    const result=groupFiles(files,this.urlFor).find(i=>i.id===id);
    if(result)this.items.set(id,result);return result||null;
  }
}
module.exports={Catalog,groupFiles,frameName,kindFor,imageExtensions,videoExtensions,modelExtensions,audioExtensions,documentExtensions,textExtensions};
