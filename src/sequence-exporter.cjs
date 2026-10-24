const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),os=require('node:os'),{spawn}=require('node:child_process');
const {encodingPlan,encodingArguments}=require('./export-encoding.cjs');
class SequenceExporter {
 constructor(folder,urlFor,onProgress){this.folder=path.resolve(folder);this.urlFor=urlFor;this.onProgress=onProgress;this.jobs=new Map();}
 async start(item,output,options){
  if([...this.jobs.values()].some(job=>!job.finished))throw new Error('已有序列正在合成');
  const fps=Number(options.fps),start=Number(options.start??0),end=Number(options.end??item.span-1);
  if(!Number.isFinite(fps)||fps<1||fps>240||!Number.isInteger(start)||!Number.isInteger(end)||start<0||end<start||end>=item.span||path.extname(output).toLowerCase()!=='.mp4')throw new Error('合成参数不正确');
  const plan=encodingPlan(options.targetMB,end-start+1,fps),id=crypto.randomUUID(),temporary=path.join(path.dirname(output),'.xiaoma-'+id+'.mp4'),frames=[];let source=0;
  for(let index=start;index<=end;index++){const number=item.first+index;while(source+1<item.frames.length&&item.frames[source+1].number<=number)source++;frames.push({url:this.urlFor(item.frames[source].path),ext:item.extension});}
  const directory=plan.passes===2?path.join(this.folder,id):null;
  if(directory){await fs.mkdir(this.folder,{recursive:true});await fs.mkdir(directory);}
  const job={id,output,temporary,directory,logPrefix:directory?path.join(directory,'stats'):null,frames,fps,plan,threads:Math.min(4,Math.max(1,Math.floor(os.availableParallelism()/2))),count:0,pass:1,finished:false,cancelled:false,writing:false};
  this.jobs.set(id,job);
  try{await this.spawnPass(job);return {id,frames,fps,total:frames.length,passes:plan.passes,targetMB:plan.targetMB};}catch(error){await this.cancel(id);throw error;}
 }
 async spawnPass(job){
  job.count=0;job.error='';job.tail='';
  const child=spawn(path.join(__dirname,'../build/encoder/ffmpeg.exe'),encodingArguments(job,job.pass),{windowsHide:true,stdio:['pipe','pipe','pipe']});job.child=child;
  job.closed=new Promise(resolve=>{child.once('error',error=>{job.error=error.message;resolve(-1);});child.once('close',resolve);});
  child.stderr.on('data',data=>{job.error=(job.error+data.toString()).slice(-6000);});
  child.stdout.on('data',data=>{job.tail+=data.toString();const lines=job.tail.split(/\r?\n/);job.tail=lines.pop();for(const line of lines){const match=line.match(/^frame=(\d+)/);if(match)this.progress(job,'encode',Number(match[1]));}});
  child.stdin.on('error',()=>{});
  await new Promise((resolve,reject)=>{child.once('spawn',resolve);child.once('error',reject);});
 }
 progress(job,phase,done){this.onProgress({id:job.id,phase,done,total:job.frames.length,pass:job.pass,passes:job.plan.passes,targetMB:job.plan.targetMB});}
 get(id){const job=this.jobs.get(id);if(!job||job.finished||job.cancelled)throw new Error('合成任务已结束');return job;}
 async write(id,index,png){
  const job=this.get(id);
  if(job.writing||index!==job.count||index>=job.frames.length||!(png instanceof ArrayBuffer)||png.byteLength>256*1024*1024)throw new Error('合成帧数据无效');
  const bytes=Buffer.from(png);
  if(bytes.length<24||bytes.readUInt32BE(0)!==0x89504e47||bytes.readUInt32BE(4)!==0x0d0a1a0a)throw new Error('合成图像无效');
  const width=bytes.readUInt32BE(16),height=bytes.readUInt32BE(20);
  if(!width||!height||width*height>100000000)throw new Error('合成图像过大');
  if(job.width&&(job.width!==width||job.height!==height))throw new Error('序列里的图片尺寸不一致');
  job.width=width;job.height=height;job.writing=true;
  try{await new Promise((resolve,reject)=>job.child.stdin.write(bytes,error=>error?reject(error):resolve()));job.count++;this.progress(job,'read',job.count);return true;}finally{job.writing=false;}
 }
 async closePass(job){
  if(job.count!==job.frames.length||job.writing)throw new Error('序列没有完整读取');
  job.child.stdin.end();const code=await job.closed;
  if(job.cancelled)throw new Error('已取消合成');
  if(code!==0)throw new Error(job.error.includes('encoder')?'本机暂时无法编码此尺寸的 MP4':'MP4 合成失败，请换一个保存位置后重试');
 }
 async nextPass(id){
  const job=this.get(id);if(job.pass>=job.plan.passes)throw new Error('不需要再次分析');
  try{await this.closePass(job);if(job.cancelled)throw new Error('已取消合成');job.pass++;await this.spawnPass(job);this.progress(job,'read',0);return {pass:job.pass};}catch(error){await this.cancel(id);throw error;}
 }
 async cleanupLogs(job){
  if(!job.directory||path.dirname(job.directory)!==this.folder)return;
  try{for(const name of await fs.readdir(job.directory)){if(!/^stats(?:-\d+)?\.log(?:\.mbtree)?$/.test(name))continue;await fs.unlink(path.join(job.directory,name)).catch(()=>{});}await fs.rmdir(job.directory);}catch{}
 }
 async finish(id){
  const job=this.get(id);
  try{
   if(job.pass!==job.plan.passes)throw new Error('视频分析尚未完成');
   await this.closePass(job);const stat=await fs.stat(job.temporary);await fs.rename(job.temporary,job.output);
   job.finished=true;this.jobs.delete(id);await this.cleanupLogs(job);this.progress(job,'done',job.count);
   return {path:job.output,width:job.plan.passes===2?Math.ceil(job.width/2)*2:job.width,height:job.plan.passes===2?Math.ceil(job.height/2)*2:job.height,frames:job.count,fps:job.fps,size:stat.size,targetMB:job.plan.targetMB};
  }catch(error){await this.cancel(id);throw error;}
 }
 async cancel(id){const job=this.jobs.get(id);if(!job)return;job.cancelled=true;job.child?.stdin.destroy();job.child?.kill();if(job.closed)await job.closed;await fs.unlink(job.temporary).catch(()=>{});await this.cleanupLogs(job);job.finished=true;this.jobs.delete(id);}
 close(){for(const job of this.jobs.values())this.cancel(job.id).catch(()=>{});}
}
module.exports={SequenceExporter};
