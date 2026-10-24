const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{execFile,spawn}=require('node:child_process');
const config=require('./update-config.json');
function newer(a,b){const left=a.split('.').map(Number),right=b.split('.').map(Number);return left.some((value,i)=>left.slice(0,i).every((v,j)=>v===(right[j]||0))&&value>(right[i]||0));}
function validateManifest(envelope,installed){
  if(typeof envelope.payload!=='string'||envelope.payload.length>20000||typeof envelope.signature!=='string'||!crypto.verify(null,Buffer.from(envelope.payload,'base64'),config.publicKey,Buffer.from(envelope.signature,'base64')))throw new Error('Update information signature verification failed');
  const info=JSON.parse(Buffer.from(envelope.payload,'base64').toString('utf8')),url=new URL(info.url),base=new URL(config.manifestUrl);
  if(!/^\d+\.\d+\.\d+$/.test(info.version)||url.origin!==base.origin||!url.pathname.startsWith('/tools/xiaoma-player/releases/')||url.username||url.password||!(/^[a-f0-9]{64}$/i.test(info.sha256))||!Number.isSafeInteger(info.size)||info.size<1000||info.size>600*1024*1024)throw new Error('Update information format is incorrect');
  return {...info,available:newer(info.version,installed),installed,notes:String(info.notes||'').slice(0,2000)};
}
class AppUpdater {
  constructor(app,profile,fetcher,onProgress){this.app=app;this.profile=profile;this.fetcher=fetcher;this.onProgress=onProgress;this.info=null;this.pending=null;}
  async check(){
    const response=await this.fetcher(config.manifestUrl,{cache:'no-store',redirect:'error',signal:AbortSignal.timeout(15000)});if(!response.ok)throw new Error('Unable to connect to update server temporarily');
    const raw=await response.text();if(raw.length>40000)throw new Error('Update information is too large');this.info=validateManifest(JSON.parse(raw),this.app.getVersion());return this.info;
  }
  async download(){if(this.pending)return this.pending;this.pending=this.downloadOnce();try{return await this.pending;}finally{this.pending=null;}}
  async downloadOnce(){
    const info=await this.check();if(!info.available)return {current:true};if(!this.app.isPackaged)throw new Error('Development mode should use final program to test updates');
    const root=path.dirname(process.execPath),folder=path.join(this.profile,'updates',crypto.randomUUID()),zip=path.join(folder,'release.zip'),stage=path.join(folder,'stage'),helper=path.join(folder,'UpdateHelper.exe');
    if(path.resolve(this.profile).toLowerCase()!==path.join(root,'.data').toLowerCase())throw new Error('Please run the update from a normal final directory');
    await fs.mkdir(folder,{recursive:true});await fs.copyFile(path.join(__dirname,'../build/UpdateHelper.exe'),helper);
    const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),10*60*1000);
    let handle;try{
      const response=await this.fetcher(info.url,{cache:'no-store',redirect:'error',signal:controller.signal});if(!response.ok)throw new Error('Update package download failed');
      handle=await fs.open(zip,'wx');const hash=crypto.createHash('sha256');let bytes=0,last=0;
      for await(const chunk of response.body){bytes+=chunk.length;if(bytes>info.size)throw new Error('Update package size is incorrect');hash.update(chunk);await handle.write(chunk);if(Date.now()-last>150){last=Date.now();this.onProgress({phase:'download',percent:Math.round(bytes/info.size*100)});}}
      await handle.close();handle=null;if(bytes!==info.size||hash.digest('hex')!==info.sha256)throw new Error('Update package verification failed, please redownload');
      this.onProgress({phase:'prepare',percent:100});await new Promise((resolve,reject)=>execFile(helper,['prepare',zip,stage,info.sha256],{windowsHide:true,timeout:120000},(error,stdout)=>error?reject(new Error('Failed to extract or verify update package')):resolve(stdout)));
      this.ready={helper,stage,root};return {ready:true,version:info.version};
    }catch(error){if(handle)await handle.close();await fs.rm(folder,{recursive:true,force:true}).catch(()=>{});throw error;}finally{clearTimeout(timer);}
  }
  install(){if(!this.ready)throw new Error('Please download the update first');const {helper,stage,root}=this.ready;return new Promise((resolve,reject)=>{const child=spawn(helper,['install',stage,root,String(process.pid)],{detached:true,windowsHide:true,stdio:'ignore'});child.once('error',reject);child.once('spawn',()=>{child.unref();resolve(true);setTimeout(()=>this.app.quit(),100);});});}
}
module.exports={AppUpdater,validateManifest,newer,config};
