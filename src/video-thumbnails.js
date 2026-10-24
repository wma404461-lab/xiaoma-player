export class VideoThumbnails {
  constructor(callback){this.callback=callback;this.queue=[];this.pending=new Map();this.active=null;this.observer=null;this.suspended=false;this.generation=0;}
  observe(container,items,cache){this.observer?.disconnect();const byId=new Map(items.map(item=>[item.id,item]));this.observer=new IntersectionObserver(entries=>{for(const entry of entries){if(!entry.isIntersecting)continue;const item=byId.get(entry.target.dataset.itemId);if(item?.kind!=='video'||cache.has(item.id)||this.pending.has(item.id))continue;const job={item,generation:this.generation,retry:false};this.pending.set(item.id,job);this.queue.push(job);}this.pump();},{root:container,rootMargin:'50px'});for(const row of container.querySelectorAll('.file-item'))this.observer.observe(row);}
  async pump(){
    if(this.suspended||this.active||!this.queue.length)return;
    const job=this.queue.shift();this.active=job;job.retry=false;
    try{await new Promise(resolve=>setTimeout(resolve,80));if(this.suspended||job.generation!==this.generation)return;job.retry=false;const data=await this.capture(job.item.url);if(job.generation===this.generation&&!job.retry)this.callback(job.item.id,data);}catch{}finally{
      // A visible row may never intersect again, so resume cancelled jobs explicitly.
      if(job.retry&&job.generation===this.generation)this.queue.unshift(job);
      else if(this.pending.get(job.item.id)===job)this.pending.delete(job.item.id);
      if(this.active===job)this.active=null;this.pump();
    }
  }
  capture(url){return new Promise((resolve,reject)=>{const video=document.createElement('video');video.crossOrigin='anonymous';video.preload='metadata';video.muted=true;video.playsInline=true;let done=false;const end=(error,data)=>{if(done)return;done=true;this.cancelCapture=null;clearTimeout(timeout);video.pause();video.removeAttribute('src');video.load();if(error)reject(error);else resolve(data);};this.cancelCapture=()=>end(new Error('Thumbnail paused'));const snapshot=()=>{try{if(!video.videoWidth)return;const canvas=document.createElement('canvas');canvas.width=192;canvas.height=108;const c=canvas.getContext('2d');c.fillStyle='#151c25';c.fillRect(0,0,192,108);const scale=Math.max(192/video.videoWidth,108/video.videoHeight),w=video.videoWidth*scale,h=video.videoHeight*scale;c.drawImage(video,(192-w)/2,(108-h)/2,w,h);end(null,canvas.toDataURL('image/jpeg',.75));}catch(e){end(e);}};video.onloadedmetadata=()=>{video.currentTime=Math.min(2,Math.max(.05,video.duration*.12));};video.onseeked=snapshot;video.onerror=()=>end(new Error('No available thumbnail'));const timeout=setTimeout(()=>end(new Error('Thumbnail loading timeout')),10000);video.src=url;video.load();});}
  suspend(value){this.suspended=value;if(value&&this.active){this.active.retry=true;this.cancelCapture?.();}else if(!value)this.pump();}
  reset(){this.generation++;this.cancelCapture?.();this.observer?.disconnect();this.queue=[];this.pending.clear();}
}
