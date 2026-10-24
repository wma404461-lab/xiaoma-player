const $=id=>document.getElementById(id);
export function setupSequenceExport(getState,openResult){
 let task=null,completed=null;
 const sizeSelect=$('export-size'),customSize=$('export-target-mb'),presets=new Set(['10','20','50','100','custom','quality']),saved=localStorage.getItem('export-size');
 if(presets.has(saved))sizeSelect.value=saved;
 const savedCustom=Number(localStorage.getItem('export-target-mb'));if(savedCustom>=.1&&savedCustom<=100000)customSize.value=savedCustom;
 const sizeChanged=()=>{$('export-custom-size').hidden=sizeSelect.value!=='custom';localStorage.setItem('export-size',sizeSelect.value);};
 sizeSelect.onchange=sizeChanged;customSize.onchange=()=>{if(customSize.checkValidity())localStorage.setItem('export-target-mb',customSize.value);};sizeChanged();
 $('close-export').onclick=()=>{$('export-toast').hidden=true;};
 $('open-export').onclick=()=>{if(completed)openResult(completed);};
 $('cancel-export').onclick=async()=>{if(!task)return;task.cancelled=true;task.reject?.(new Error('Composition canceled'));task.worker?.terminate();await window.viewer.cancelExport(task.id);};
 window.viewer.onExportProgress(info=>{if(task?.id!==info.id)return;const passes=info.passes||1,pass=info.pass||1;$('export-progress').value=Math.min(100,((pass-1)+info.done/info.total)/passes*100);const prefix=passes===2?(pass===1?'Analyzing · First pass':'Processing · Second pass'):'Processing MP4';$('export-label').textContent=info.phase==='done'?'MP4 Completed':prefix+' · '+info.done+' / '+info.total+' frames';});
 $('export-sequence').onclick=async()=>{if(task)return;const state=getState();if(state.item?.kind!=='sequence')return;let job;try{
  if(sizeSelect.value==='custom'&&!customSize.reportValidity())return;
  const targetMB=sizeSelect.value==='quality'?null:Number(sizeSelect.value==='custom'?customSize.value:sizeSelect.value);
  job=await window.viewer.startExport(state.item.id,{fps:state.fps,start:state.rangeStart,end:state.rangeEnd,targetMB});if(!job)return;
  task={...job,cancelled:false};completed=null;$('export-sequence').disabled=true;sizeSelect.disabled=true;customSize.disabled=true;$('export-toast').hidden=false;$('export-progress').hidden=false;$('export-progress').value=0;$('cancel-export').hidden=false;$('open-export').hidden=true;$('close-export').hidden=true;$('export-label').textContent=job.passes===2?'Analyzing the video frame by frame to control file size…':'Reading raw frame dimensions frame by frame…';
  if(!job.native){const worker=new Worker(new URL('./image-worker.js',import.meta.url),{type:'module'});task.worker=worker;
  for(let pass=1;pass<=(job.passes||1);pass++){
   let previous='',png=null;
   for(let index=0;index<job.frames.length;index++){if(task.cancelled)throw new Error('Composition canceled');const source=job.frames[index];if(source.url!==previous){png=await new Promise((resolve,reject)=>{task.reject=reject;worker.onmessage=({data})=>data.error?reject(new Error(data.error)):resolve(data.png);worker.onerror=()=>reject(new Error('Unable to read raw frame'));worker.postMessage({id:index,url:source.url,ext:source.ext,edge:0,encodePng:true});});previous=source.url;}await window.viewer.exportFrame(job.id,index,png);}
   if(pass<(job.passes||1))await window.viewer.nextExportPass(job.id);
  }
  }
  completed=await window.viewer.finishExport(job.id);$('export-label').textContent='Generated MP4 · '+(completed.size/1024/1024).toFixed(2)+' MB · '+completed.width+' × '+completed.height+' · '+completed.fps+' fps'+(completed.encoder&&completed.encoder!=='libx264'?' · GPU acceleration':'');$('open-export').hidden=false;
 }catch(error){if(job)await window.viewer.cancelExport(job.id).catch(()=>{});$('export-label').textContent=task?.cancelled?'Composition canceled':error.message||'MP4 Composition failed';}finally{task?.worker?.terminate();task=null;$('export-sequence').disabled=false;sizeSelect.disabled=false;customSize.disabled=false;$('cancel-export').hidden=true;$('close-export').hidden=false;$('export-progress').hidden=true;}};
}
