const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{execFile}=require('node:child_process');
class DocumentConverter {
  constructor(cacheFolder,urlFor){this.folder=cacheFolder;this.urlFor=urlFor;this.pending=new Map();this.queue=Promise.resolve();}
  async preview(item){
    const stat=await fs.stat(item.path),key=crypto.createHash('sha256').update(item.path+'@'+stat.mtimeMs+':'+stat.size).digest('hex');
    await fs.mkdir(this.folder,{recursive:true});const output=path.join(this.folder,key+'.pdf');
    try{await fs.access(output);return {url:this.urlFor(output)};}catch{}
    if(this.pending.has(key))return this.pending.get(key);
    const work=this.queue.catch(()=>{}).then(()=>this.convert(item.path,output));this.queue=work;this.pending.set(key,work);
    try{return await work;}finally{this.pending.delete(key);}
  }
  convert(input,output){return new Promise((resolve,reject)=>{
    const helper=path.join(__dirname,'../build/OfficePreview.exe');
    execFile(helper,[input,output],{windowsHide:true,timeout:90000,maxBuffer:1024*1024,encoding:'utf8'},async(error,stdout)=>{
      if(error){reject(new Error('Document preview is currently unavailable; please verify your device can open this file Word, PowerPoint or Excel Can normally open this file.'));return;}
      try{const result=JSON.parse(stdout.trim().split(/\r?\n/).at(-1));if(!result.Success)throw new Error(result.Error);await fs.access(output);resolve({url:this.urlFor(output)});}catch{reject(new Error('Document temporarily cannot be converted to preview'));}
    });
  });}
}
module.exports={DocumentConverter};
