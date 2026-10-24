import {FrameCache} from './frame-cache.js';
import {VideoThumbnails} from './video-thumbnails.js';
import './update-ui.js';
import {setupRecorder} from './recorder-region-ui.js';
import {setupClipper} from './timeline-clipper.js';
import {VolumeControl} from './volume-control.js';
import {VideoPlaybackStats} from './video-playback-stats.js';
import {SequenceClock} from './sequence-clock.js';
import {MediaHUD} from './media-hud.js';
import {setupSequenceExport} from './sequence-export-ui.js';
const $=id=>document.getElementById(id),stage=$('stage'),picture=$('picture'),ctx=picture.getContext('2d'),video=$('video'),music=$('music'),animated=$('animated-image');video.crossOrigin='anonymous';music.crossOrigin='anonymous';
const names={image:'Image',video:'Video',audio:'Audio',sequence:'Sequence frames',model:'Model',document:'Document',text:'Text'};
const state={items:[],item:null,index:0,epoch:0,request:0,frame:0,playing:false,scrubbing:false,loadingFrame:false,fps:24,loop:true,ordered:true,videoFps:30,rangeStart:0,rangeEnd:0,view:{zoom:null,x:0,y:0,rotation:0},bitmap:null,model:null,modelInfo:null,document:null,background:'black',lastTick:0,performanceFrames:[],stripVisible:true,previewEdge:1280};
const volumeControl=new VolumeControl([video,music]),hud=new MediaHUD(()=>state,dimensions);
const videoStats=new VideoPlaybackStats(video);state.videoStats=()=>videoStats.read();
const sequenceClock=new SequenceClock();let thumbnailCheckTime=0;
let cachePaintTimer,scanEpoch=0,loadTimer,drag=null,seekQueued=null,seekHandle=null,qualityTimer;
const budget=await window.viewer.memoryBudget();
const cache=new FrameCache(budget,()=>{if(!cachePaintTimer)cachePaintTimer=setTimeout(()=>{cachePaintTimer=null;drawTrack();},200);});
const previewEdge=()=>state.previewEdge;
let playbackHandle=null,paintHandle=null,detailTime=0,stripItems=null,stripStart=0;
const thumbnails=new Map();
let thumbnailBatch='',stripScrollFrame=null;
window.viewer.onThumbnail?.(info=>{if(info.batch!==thumbnailBatch)return;if(info.url)setThumbnail(info.id,info.url);if(info.complete)$('file-strip').dataset.thumbnails='ready';});
function prepareThumbnails(){if(!window.viewer.prepareThumbnails)return;thumbnailBatch=crypto.randomUUID();$('file-strip').dataset.thumbnails='loading';window.viewer.prepareThumbnails(state.items.map(item=>item.id),thumbnailBatch).catch(()=>{$('file-strip').dataset.thumbnails='ready';});}
const videoThumbnails=new VideoThumbnails((id,data)=>setThumbnail(id,data));
const av=()=>['video','audio'].includes(state.item?.kind);
const player=()=>state.item?.kind==='audio'?music:video;
const playable=item=>['video','audio','sequence'].includes(item.kind);
const listActive=()=>state.ordered&&state.items.filter(playable).length>1;
function setThumbnail(id,data){thumbnails.set(id,data);const target=$('file-strip').querySelector(`[data-item-id="${id}"] .file-thumb`);if(target){const img=document.createElement('img');img.src=data;img.alt='';img.draggable=false;target.replaceChildren(img);}}
function nativeLoop(){return !state.editingVideo&&state.loop&&!listActive()&&state.rangeStart===0&&state.rangeEnd>=player().duration;}
function nextPlayable(){if(state.editingVideo||!listActive())return false;let next=state.items.findIndex((item,index)=>index>state.index&&playable(item));if(next<0&&state.loop)next=state.items.findIndex(playable);if(next>=0){openItem(next,true);return true;}return false;}
function adjacentVisualItem(delta){const kind=state.item?.kind,length=state.items.length;if(!['image','video'].includes(kind))return -1;for(let step=1;step<length;step++){const index=(state.index+delta*step+length)%length;if(state.items[index].kind===kind)return index;}return -1;}
function switchVisualItem(delta){if(state.editingVideo||!$('loading').hidden)return;const index=adjacentVisualItem(delta);if(index>=0)openItem(index);}
function paintSoon(){if(paintHandle)return;paintHandle=requestAnimationFrame(()=>{paintHandle=null;drawPicture();layoutElement();});}
const motion=()=>state.item?.kind==='sequence'||av()||!!state.modelInfo?.animations.length;
const isSequence=()=>state.item?.kind==='sequence';
const imageFrame=item=>({url:item.url,mtime:item.mtime,ext:item.extension});
const fmtSeconds=value=>{const seconds=Math.max(0,Math.floor(value||0));return String(Math.floor(seconds/60)).padStart(2,'0')+':'+String(seconds%60).padStart(2,'0');};
function setStatus(text){$('status').textContent=text;}
function loading(value,text='Loading…'){ $('loading').hidden=!value;$('loading-text').textContent=text; }
function showError(error){loading(false);$('error').hidden=false;$('error-text').textContent=error.message||String(error);setPlaying(false);}
function rememberFps(){try{localStorage.setItem('fps:'+state.item.id,String(state.fps));}catch{}}
function setPlaying(value,inspect=true){
  state.playing=value;state.lastTick=performance.now();state.performanceFrames=[];$('play-icon').setAttribute('href',value?'#i-pause':'#i-play');$('play').setAttribute('aria-label',value?'Pause':'Play');
  sequenceClock.reset(state.lastTick,state.frame);
  state.prefetchPrimed=false;
  hud.update();
  syncThumbnailActivity();cache.parallelPrefetch=value&&isSequence();
  if(av()){const media=player();if(value&&state.editingVideo&&(media.currentTime<state.rangeStart||media.currentTime>=state.rangeEnd-.01))media.currentTime=state.rangeStart;if(value)media.play().catch(()=>{state.playing=false;$('play-icon').setAttribute('href','#i-play');});else media.pause();}
  if(state.model)state.model.setPlaying(value);
  if(value&&isSequence()&&!playbackHandle)playbackHandle=requestAnimationFrame(tick);
  if(!value){if(playbackHandle)cancelAnimationFrame(playbackHandle);playbackHandle=null;cache.stopPrefetch();if(inspect)scheduleQuality();}
}
function scheduleQuality(){clearTimeout(qualityTimer);qualityTimer=setTimeout(upgradeQuality,250);}
function syncThumbnailActivity(){
  let busy=state.playing&&isSequence();
  if(state.playing&&state.item?.kind==='video'){
    const stats=videoStats.read();
    busy=performance.now()-state.lastTick<1500||stats.waiting||stats.speed===null||stats.speed<stats.requested*.9||stats.fps<state.videoFps*stats.requested*.85||!hud.metadata?.video?.bitrate||hud.metadata.video.bitrate>50000000;
  }
  videoThumbnails.suspend(busy);
}
async function upgradeQuality(){
  if(state.playing||state.scrubbing||!state.bitmap||state.view.zoom===null||state.view.zoom<.75||state.bitmap.bitmap.width===state.bitmap.width)return;
  if(isSequence()){requestFrame(state.frame,true);return;}
  if(state.item?.kind!=='image')return;const epoch=state.epoch;
  try{const entry=await cache.get(imageFrame(state.item),0);if(epoch!==state.epoch)return;cache.pin(entry);state.bitmap=entry;drawPicture();}catch{}
}
function sourceAt(index){const item=state.item,target=item.first+index,frames=item.frames;let left=0,right=frames.length-1;while(left<=right){const mid=(left+right)>>1;if(frames[mid].number<=target)left=mid+1;else right=mid-1;}return {source:frames[Math.max(0,right)],missing:frames[Math.max(0,right)].number!==target,target};}
function applyBackground(){stage.classList.remove('checker','light');}
function fit(){state.view={zoom:null,x:0,y:0,rotation:0};if(state.model){state.model.fit();return;}if(state.document){state.document.fit();return;}if(state.item&&state.bitmap){const source=isSequence()?sourceAt(state.frame).source:imageFrame(state.item),entry=cache.entries.get(cache.key(source,previewEdge()));if(entry){cache.pin(entry);state.bitmap=entry;}}drawPicture();layoutElement();}
function dimensions(){if(state.bitmap)return {width:state.bitmap.width,height:state.bitmap.height};if(!animated.hidden)return {width:animated.naturalWidth,height:animated.naturalHeight};if(!video.hidden)return {width:video.videoWidth,height:video.videoHeight};return {width:1,height:1};}
function fitZoom(width,height){if(state.view.rotation%180)[width,height]=[height,width];return Math.min((stage.clientWidth-32)/Math.max(1,width),(stage.clientHeight-32)/Math.max(1,height));}
function drawPicture(){
  if(picture.hidden||!state.bitmap?.bitmap||!state.item)return;const w=stage.clientWidth,h=stage.clientHeight,dpr=Math.min(devicePixelRatio,state.playing||state.scrubbing?1:1.25);if(picture.width!==Math.round(w*dpr)||picture.height!==Math.round(h*dpr)){picture.width=Math.round(w*dpr);picture.height=Math.round(h*dpr);}ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);const entry=state.bitmap,zoom=state.view.zoom??fitZoom(entry.width,entry.height);ctx.save();ctx.translate(w/2+state.view.x,h/2+state.view.y);ctx.rotate(state.view.rotation*Math.PI/180);ctx.scale(zoom,zoom);ctx.imageSmoothingEnabled=zoom<4;ctx.drawImage(entry.bitmap,-entry.width/2,-entry.height/2,entry.width,entry.height);ctx.restore();
}
function layoutElement(){const element=!video.hidden?video:!animated.hidden?animated:null;if(!element)return;const {width,height}=dimensions(),zoom=state.view.zoom??fitZoom(width,height);element.style.width=width*zoom+'px';element.style.height=height*zoom+'px';element.style.transform=`translate(calc(-50% + ${state.view.x}px),calc(-50% + ${state.view.y}px)) rotate(${state.view.rotation}deg)`;}
function updateTime(){
  hud.refresh();
  const item=state.item;if(!item)return;
  if(item.kind==='sequence'){$('time').textContent=(item.first+state.frame)+' / '+item.last;$('timeline').value=state.frame;}
  else if(av()){const media=player();$('time').textContent=fmtSeconds(media.currentTime)+' / '+fmtSeconds(media.duration);$('timeline').value=media.currentTime;}
  else if(state.modelInfo?.animations.length){const duration=state.modelInfo.animations[state.model.activeClip||0].duration,time=state.model.mixer?.time||0,position=state.loop?time%duration:Math.min(time,duration);$('time').textContent=fmtSeconds(position)+' / '+fmtSeconds(duration);$('timeline').value=position;}
}
function updateControls(){
  const item=state.item;if(!item)return;const seq=item.kind==='sequence',vid=item.kind==='video',model=item.kind==='model',doc=item.kind==='document',moving=motion();
  $('export-sequence').hidden=!seq;
  $('export-size-controls').hidden=!seq;
  $('trim-video').hidden=!vid;$('reveal-video').hidden=false;
  const image=item.kind==='image',navigable=image||vid;$('image-navigation').hidden=!navigable||adjacentVisualItem(-1)<0&&adjacentVisualItem(1)<0;$('previous-image').disabled=adjacentVisualItem(-1)<0;$('next-image').disabled=adjacentVisualItem(1)<0;
  $('image-navigation').setAttribute('aria-label',vid?'Switch video':'switch image');
  for(const[id,label,key]of[['previous-image',vid?'Previous video':'previous image','←'],['next-image',vid?'Next video':'next image','→']]){$(id).setAttribute('aria-label',label);$(id).title=label+' ('+key+')';}
  $('controls').hidden=false;$('motion-controls').hidden=!moving;$('still-controls').hidden=moving||doc;$('document-controls').hidden=!doc;$('fps').hidden=!seq;$('speed').hidden=!av();$('audio').hidden=!av();$('order').hidden=!seq&&!av();$('animation').hidden=!model||!moving;$('range-toggle').hidden=true;if(!seq&&!av())$('range-controls').hidden=true;$('range-toggle').setAttribute('aria-expanded',String(!$('range-controls').hidden));$('wireframe').hidden=!model;$('actual-size').hidden=model||doc||item.kind==='text';$('rotate').hidden=model||doc||item.kind==='text';
  $('file-name').textContent=seq?`${item.name} · ${item.first}–${item.last}${item.extension}`:item.name;$('file-name').title=item.path+'\n Click to show in folder';
  document.title='Xiaoma Player + Recorder · '+item.name;$('gesture-hint').textContent=model?'Drag to rotate · Scroll to zoom':seq?'D / F Frame by frame · Space to play · F11 Full screen':av()?'Space to pause · ← → Jump to · ↑ ↓ Volume · F11 Full screen':doc?'Previous page / Next Page · F11 Full screen':'Scroll to zoom · Backspace Fit';
  if(image)$('gesture-hint').textContent='← / → switch image · Scroll to zoom';
  if(vid)$('gesture-hint').textContent='← / → Switch video · D / F Frame by frame · Space to pause · ↑ / ↓ Volume';
  $('timeline').min=0;$('timeline').max=seq?item.span-1:av()?player().duration||1:state.modelInfo?.animations[state.model?.activeClip||0]?.duration||1;$('timeline').step=seq?'1':'0.001';$('fps').value=state.fps;
  $('previous-frame').disabled=!seq&&!vid&&!model;$('next-frame').disabled=!seq&&!vid&&!model;
  $('range-start').step=$('range-end').step=seq?'1':'0.1';$('range-start').min=$('range-end').min=seq?item.first:0;$('range-start').max=$('range-end').max=seq?item.last:player().duration||0;$('range-start').value=seq?item.first+state.rangeStart:state.rangeStart;$('range-end').value=seq?item.first+state.rangeEnd:state.rangeEnd;$('range-unit').textContent=seq?'frames':'seconds';
  updateTime();drawTrack();
}
function updateDetails(){
  if(state.playing&&performance.now()-detailTime<250)return;detailTime=performance.now();
  const item=state.item;if(!item)return;const dims=dimensions(),pixels=dims.width>1?`${dims.width} × ${dims.height}`:'';
  if(item.kind==='sequence'){
    let text=`${item.frames.length} frames · ${item.extension.slice(1).toUpperCase()}${pixels?' · '+pixels:''}`;
    if(item.missingCount)text+=` · missing ${item.missingCount} frames`;
    const times=state.performanceFrames,actual=times.length>2?(times.length-1)*1000/(times.at(-1)-times[0]):state.fps;
    if(state.playing&&times.length>8&&actual<state.fps*.87)text+=` · Real-time preview ${Math.round(actual)} / ${state.fps} fps`;
    else text+=state.playing?' · Play directly':' · Paused';setStatus(text);
  }else if(av()){const media=player();setStatus(`${item.extension.slice(1).toUpperCase()} · ${item.kind==='video'?pixels+' · ':''}${fmtSeconds(media.duration)}`);
  }else if(item.kind==='document'){setStatus(item.extension.slice(1).toUpperCase()+' · '+(state.document?.pages||1)+' Page');
  }else if(item.kind==='text'){$('details').textContent=item.extension.slice(1).toUpperCase()+' · Text preview';setStatus($('details').textContent);
  }else if(item.kind==='model'){
    const info=state.modelInfo,text=info?`${Math.round(info.triangles).toLocaleString()} Face${info.animations.length?' · '+info.animations.length+' animation items':''}`:'Loading model';$('details').textContent=text;setStatus(item.extension.slice(1).toUpperCase()+' · '+text);
  }else{$('details').textContent=pixels+(pixels?' · ':'')+item.extension.slice(1).toUpperCase();setStatus($('details').textContent);}
}
function prefetch(index){
  if(!isSequence()||!state.playing||state.scrubbing)return;
  const item=state.item,edge=previewEdge(),entry=state.bitmap,framesToRead=Math.min(16,Math.max(2,Math.floor((cache.budget-(entry?.bytes||0))/Math.max(entry?.bytes||4e6,1)*.6)));
  const current=sequenceClock.read(performance.now(),state.fps,state.rangeStart,state.rangeEnd,false).frame,estimate=Math.max(1,Math.min(12,Math.ceil((state.decodeMs||0)*state.fps/1000)+2));
  const lead=state.prefetchPrimed?(state.loop?estimate:Math.min(estimate,Math.max(1,state.rangeEnd-current))):1,nearby=[];state.prefetchPrimed=true;
  for(let offset=lead;offset<lead+framesToRead;offset++){
    let next=current+offset;if(next>state.rangeEnd){if(!state.loop)break;next=state.rangeStart+(next-state.rangeStart)%(state.rangeEnd-state.rangeStart+1);}
    const source=sourceAt(next).source;if(!nearby.includes(source))nearby.push(source);
  }
  cache.prefetch(nearby,edge);
}
async function requestFrame(index,full=false){
  if(!isSequence())return;const epoch=state.epoch,ticket=++state.request;index=Math.max(0,Math.min(state.item.span-1,index));state.frame=index;state.loadingFrame=true;updateTime();const {source,missing,target}=sourceAt(index);clearTimeout(loadTimer);loadTimer=setTimeout(()=>loading(true,'Loading frame number '+target+' frames…'),180);
  try{
    const edge=full?0:previewEdge(),entry=await cache.get(source,edge);if(epoch!==state.epoch||ticket!==state.request)return;if(entry.decodeMs)state.decodeMs=state.decodeMs?state.decodeMs*.8+entry.decodeMs*.2:entry.decodeMs;clearTimeout(loadTimer);loading(false);cache.pin(entry);state.bitmap=entry;picture.hidden=false;applyBackground(entry.alpha);drawPicture();$('stage-badge').hidden=!missing;$('stage-badge').textContent=missing?`Page ${target} Frame not generated yet · Show previous frame`:'';
    if(state.playing){const now=performance.now();state.performanceFrames.push(now);while(state.performanceFrames.length&&state.performanceFrames[0]<now-1600)state.performanceFrames.shift();}
    state.loadingFrame=false;state.lastPaintTime=performance.now();updateTime();updateDetails();currentThumbnail(entry.bitmap);prefetch(index);
  }catch(error){if(epoch!==state.epoch||ticket!==state.request)return;clearTimeout(loadTimer);loading(false);state.loadingFrame=false;$('stage-badge').hidden=false;$('stage-badge').textContent='Page '+target+' Frame load failed · You can continue dragging or retry';setStatus(error.message);if(state.playing){state.playing=false;$('play-icon').setAttribute('href','#i-play');}}
}
function scheduleSeek(index){seekQueued=index;if(seekHandle)return;seekHandle=requestAnimationFrame(()=>{seekHandle=null;const value=seekQueued;seekQueued=null;requestFrame(value,false);scheduleQuality();});}
function drawTrack(){
  const canvas=$('cache-track'),wrap=canvas.parentElement,w=wrap.clientWidth;if(!w)return;const dpr=Math.min(devicePixelRatio,2);if(canvas.width!==Math.round(w*dpr)||canvas.height!==Math.round(6*dpr)){canvas.width=Math.round(w*dpr);canvas.height=Math.round(6*dpr);}const context=canvas.getContext('2d');context.setTransform(dpr,0,0,dpr,0,0);context.clearRect(0,0,w,6);context.fillStyle='#292d35';context.fillRect(0,0,w,6);const item=state.item;if(!item)return;
  if(item.kind==='sequence'){
    context.fillStyle='#747d8d';for(const entry of cache.entries.values())if(Number.isFinite(entry.frameNumber))context.fillRect((entry.frameNumber-item.first)/item.span*w,0,Math.max(1,w/item.span),6);
    context.fillStyle='#ce965e';for(const [start,end] of item.missing)context.fillRect((start-item.first)/item.span*w,0,Math.max(1,(end-start+1)/item.span*w),6);
    if(state.rangeStart>0||state.rangeEnd<item.span-1){context.fillStyle='#14171cc9';context.fillRect(0,0,state.rangeStart/item.span*w,6);context.fillRect((state.rangeEnd+1)/item.span*w,0,w,6);}
  }else if(item.kind==='video'&&video.duration){context.fillStyle='#747d8d';for(let i=0;i<video.buffered.length;i++)context.fillRect(video.buffered.start(i)/video.duration*w,0,(video.buffered.end(i)-video.buffered.start(i))/video.duration*w,6);}
}
function currentThumbnail(source){if(!state.item||thumbnails.has(state.item.id))return;try{const canvas=document.createElement('canvas');canvas.width=160;canvas.height=90;canvas.getContext('2d').drawImage(source,0,0,160,90);setThumbnail(state.item.id,canvas.toDataURL('image/jpeg',.75));}catch{}}
function stripStride(){return Math.max(32,($('file-strip').clientWidth-12)*9/16+6);}
function renderStrip(fromScroll=false){
  const strip=$('file-strip'),virtual=state.items.length>400,stride=stripStride(),start=virtual?Math.max(0,Math.min(state.items.length-64,Math.floor(strip.scrollTop/stride)-3)):0;
  if(fromScroll&&start===stripStart)return;stripStart=start;strip.replaceChildren();
  const spacer=height=>{const node=document.createElement('div');node.className='list-spacer';node.style.height=height+'px';strip.append(node);};if(start)spacer(Math.max(0,start*stride-8));
  state.items.slice(start,virtual?start+64:state.items.length).forEach((item,offset)=>{const index=start+offset,button=document.createElement('button');button.className='file-item'+(index===state.index?' selected':'');button.dataset.itemId=item.id;button.dataset.kind=item.kind;button.title=item.name+'\n'+item.path;button.setAttribute('aria-label',item.name);button.setAttribute('aria-pressed',String(index===state.index));
    const thumb=document.createElement('span');thumb.className='file-thumb';thumb.innerHTML=`<svg class="icon"><use href="#i-${item.kind}"/></svg>`;if(thumbnails.has(item.id)){const img=document.createElement('img');img.draggable=false;img.alt='';img.src=thumbnails.get(item.id);thumb.replaceChildren(img);}button.append(thumb);button.addEventListener('click',()=>openItem(index));strip.append(button);
  });if(virtual&&start+64<state.items.length)spacer(Math.max(0,(state.items.length-start-64)*stride-8));
  if(!window.viewer.prepareThumbnails)videoThumbnails.observe(strip,state.items,thumbnails);
}
function buildStrip(force=false){
  const strip=$('file-strip');$('item-count').textContent=state.items.length+' items';$('list-count').textContent=state.items.length+' items';$('toggle-strip').hidden=!state.items.length;strip.hidden=false;$('playlist-pane').hidden=!state.items.length||!state.stripVisible;
  if(stripItems!==state.items||force){stripItems=state.items;strip.scrollTop=state.items.length>400?Math.max(0,state.index-3)*stripStride():0;renderStrip();}
  else if(state.index<stripStart||state.index>=stripStart+(state.items.length>400?64:state.items.length)){strip.scrollTop=Math.max(0,state.index-3)*stripStride();renderStrip();}
  for(const button of strip.querySelectorAll('.file-item')){const selected=button.dataset.itemId===state.item.id;button.classList.toggle('selected',selected);button.setAttribute('aria-pressed',String(selected));}strip.querySelector('.selected')?.scrollIntoView({block:'nearest'});
}
$('file-strip').addEventListener('scroll',()=>{if(state.items.length<=400||stripScrollFrame)return;stripScrollFrame=requestAnimationFrame(()=>{stripScrollFrame=null;renderStrip(true);});});
new ResizeObserver(()=>{if(state.items.length>400)renderStrip();}).observe($('file-strip'));
async function openItem(index,autoplay=false){
  const item=state.items[index];if(!item)return;state.epoch++;const epoch=state.epoch;setPlaying(false,false);state.scrubbing=false;state.loadingFrame=false;state.bitmap=null;state.modelInfo=null;state.model?.dispose();state.model=null;state.document?.dispose();state.document=null;state.request++;clearTimeout(loadTimer);clearTimeout(qualityTimer);window.viewer.native?cache.beginItem():cache.clear();for(const media of [video,music]){media.pause();media.removeAttribute('src');media.load();}if(state.subtitleUrl)URL.revokeObjectURL(state.subtitleUrl);state.subtitleUrl=null;$('subtitles').hidden=true;animated.removeAttribute('src');state.item=item;state.index=index;state.frame=0;state.rangeStart=0;state.rangeEnd=item.kind==='sequence'?item.span-1:0;state.view={zoom:null,x:0,y:0,rotation:0};state.previewEdge=stage.clientWidth<=850?960:1280;state.background='black';applyBackground();state.fps=Number(localStorage.getItem('fps:'+item.id))||24;
  for(const id of ['empty','picture','video','animated-image','model-view','audio-view','document-view','text-view','error','stage-badge','range-controls'])$(id).hidden=true;$('play-icon').setAttribute('href','#i-play');$('wireframe').setAttribute('aria-pressed','false');loading(true);buildStrip();updateControls();
  videoThumbnails.suspend(true);state.decodeMs=0;state.videoFps=30;state.videoExactFps=false;state.videoPresentedTime=null;hud.load(item).then(()=>{if(epoch===state.epoch&&!$('loading').hidden)return;if(epoch===state.epoch)syncThumbnailActivity();});
  try{
    if(item.kind==='sequence'){await requestFrame(0);if(epoch!==state.epoch)return;if(state.bitmap&&autoplay){setPlaying(true);prefetch(0);}}
    else if(item.kind==='image'){
      if(window.viewer.fileInfo){const info=await window.viewer.fileInfo(item.id);if(epoch!==state.epoch)return;item.size=info.size;item.mtime=info.mtime;}
      if(['.gif','.svg','.webp'].includes(item.extension)){await new Promise((resolve,reject)=>{animated.onload=resolve;animated.onerror=()=>reject(new Error('Image cannot be read'));animated.src=item.url;});if(epoch!==state.epoch)return;animated.hidden=false;applyBackground(['.svg','.webp'].includes(item.extension));layoutElement();}
      else{const entry=await cache.get(imageFrame(item),previewEdge());if(epoch!==state.epoch)return;cache.pin(entry);state.bitmap=entry;picture.hidden=false;applyBackground(entry.alpha);drawPicture();currentThumbnail(entry.bitmap);}loading(false);updateDetails();
    }else if(av()){
      $('speed').value='1';player().defaultPlaybackRate=1;
      const media=player();await new Promise((resolve,reject)=>{const done=()=>{media.removeEventListener('loadedmetadata',done);media.removeEventListener('error',fail);resolve();};const fail=()=>{media.removeEventListener('loadedmetadata',done);media.removeEventListener('error',fail);reject(new Error('The encoding of this audio or video is currently not supported'));};media.addEventListener('loadedmetadata',done);media.addEventListener('error',fail);media.src=item.url;media.load();});if(epoch!==state.epoch)return;if(item.kind==='video'){video.hidden=false;loadSubtitles(item,epoch);}else{$('audio-view').hidden=false;$('audio-title').textContent=item.name;$('audio-format').textContent=item.extension.slice(1).toUpperCase()+' · '+fmtSeconds(media.duration);}media.volume=Number($('volume').value);media.playbackRate=Number($('speed').value);state.rangeEnd=media.duration;media.loop=nativeLoop();layoutElement();loading(false);updateControls();updateDetails();setPlaying(true);
    }else if(item.kind==='document'){
      $('document-view').hidden=false;loading(true,'Preparing document preview…');const {DocumentView}=await import('./documents.js');if(epoch!==state.epoch)return;const view=new DocumentView($('document-view'),info=>{if(epoch!==state.epoch)return;$('page-number').value=info.page;$('page-number').max=info.pages;$('page-count').textContent=info.pages;updateDetails();});state.document=view;await view.open(item);if(epoch!==state.epoch)return;loading(false);updateControls();updateDetails();
    }else if(item.kind==='text'){
      const response=await fetch(item.url,{headers:{Range:'bytes=0-10485759'}});if(!response.ok)throw new Error('Text cannot be read');const buffer=await response.arrayBuffer();if(epoch!==state.epoch)return;let text=new TextDecoder('utf-8').decode(buffer);if(text.includes('\uFFFD'))text=new TextDecoder('gb18030').decode(buffer);$('text-view').textContent=text.slice(0,5000000);$('text-view').hidden=false;loading(false);updateDetails();
    }else{
      $('model-view').hidden=false;loading(true,'Loading model…');const {ModelView}=await import('./models.js');if(epoch!==state.epoch)return;const model=new ModelView($('model-view'));state.model=model;state.modelInfo=await model.load(item);if(epoch!==state.epoch)return;if(!state.modelInfo)return;
      model.setLoop(state.loop);model.onFrame=updateTime;model.onEnded=()=>{if(state.model===model)setPlaying(false);};$('animation').replaceChildren();state.modelInfo.animations.forEach((clip,i)=>{const option=document.createElement('option');option.value=i;option.textContent=clip.name;$('animation').append(option);});loading(false);updateControls();updateDetails();
    }
  }catch(error){if(epoch===state.epoch)showError(error);}finally{if(epoch===state.epoch)syncThumbnailActivity();}
}
async function importPaths(paths){if(!paths.length)return;const scan=++scanEpoch;loading(true,'Identifying file and sequence…');$('error').hidden=true;$('empty').hidden=true;setStatus('Identifying…');try{const result=await window.viewer.scan(paths);if(scan!==scanEpoch)return;if(!result.items.length){loading(false);showError(new Error(result.errors.join('; ')||'No viewable files in the folder'));return;}cache.clear();videoThumbnails.reset();thumbnails.clear();state.items=result.items;state.stripVisible=state.items.length>1;await openItem(result.selectedIndex);prepareThumbnails();if(result.errors.length)setStatus(result.errors.length+' Item not open · '+result.errors[0]);}catch(error){if(scan===scanEpoch)showError(error);}finally{if(scan===scanEpoch)await window.viewer.contentReady?.().catch(()=>{});}}
async function choose(mode='files'){const paths=await window.viewer.choose(mode);await importPaths(paths);}
async function revealCurrent(){if(!state.item)return;const button=$('reveal-video');button.disabled=true;setStatus('Locating in folder…');try{await window.viewer.reveal(state.item.id);setStatus('Already located in folder');}catch(error){setStatus(error.message||'Folder temporarily cannot be opened');}finally{button.disabled=false;}}$('reveal-video').onclick=revealCurrent;
$('open-files').onclick=()=>choose();$('empty-open').onclick=()=>choose();$('open-folder').onclick=()=>choose('folder');
$('play').onclick=()=>setPlaying(!state.playing);
function repeatMode(ordered){state.loop=true;state.ordered=ordered;$('loop').classList.toggle('active',!ordered);$('loop').setAttribute('aria-pressed',String(!ordered));$('order').classList.toggle('active',ordered);$('order').setAttribute('aria-pressed',String(ordered));if(av())player().loop=nativeLoop();state.model?.setLoop(true);sequenceClock.reset(performance.now(),state.frame);}
$('loop').onclick=()=>repeatMode(false);$('order').onclick=()=>repeatMode(true);
$('fps').onchange=()=>{state.fps=Number($('fps').value);rememberFps();state.lastTick=performance.now();sequenceClock.reset(state.lastTick,state.frame);};$('speed').onchange=()=>{if(av())player().playbackRate=Number($('speed').value);};
function step(delta){setPlaying(false,false);if(isSequence())scheduleSeek(Math.max(0,Math.min(state.item.span-1,state.frame+delta)));else if(state.item?.kind==='video'){const fps=state.videoFps||30,current=Math.round((state.videoPresentedTime??video.currentTime)*fps),total=hud.frame()?.total||Math.round(video.duration*fps);state.videoPresentedTime=null;video.currentTime=Math.max(0,Math.min(video.duration-.00001,(Math.min(total-1,Math.max(0,current+delta))+.001)/fps));hud.update();}else if(state.modelInfo?.animations.length){const duration=state.modelInfo.animations[state.model.activeClip||0].duration;state.model.seek(Math.max(0,Math.min(duration,(state.model.mixer?.time||0)+delta/24)));updateTime();}}
$('previous-frame').onclick=()=>step(-1);$('next-frame').onclick=()=>step(1);
$('previous-image').onclick=()=>switchVisualItem(-1);$('next-image').onclick=()=>switchVisualItem(1);
$('image-navigation').addEventListener('pointerdown',e=>e.stopPropagation());$('image-navigation').addEventListener('dblclick',e=>e.stopPropagation());
document.addEventListener('keydown',e=>{if(!['image','video'].includes(state.item?.kind)||state.editingVideo||!['ArrowLeft','ArrowRight'].includes(e.key)||e.ctrlKey||e.altKey||e.metaKey||e.shiftKey||document.querySelector('dialog[open]')||['INPUT','SELECT','TEXTAREA'].includes(e.target.tagName)||e.target.isContentEditable)return;e.preventDefault();switchVisualItem(e.key==='ArrowLeft'?-1:1);});
$('timeline').oninput=()=>{const value=Number($('timeline').value);setPlaying(false,false);if(isSequence())scheduleSeek(value);else if(av())player().currentTime=value;else state.model?.seek(value);};
$('animation').onchange=()=>{state.model?.selectAnimation(Number($('animation').value));state.model?.setLoop(state.loop);$('timeline').max=state.modelInfo.animations[Number($('animation').value)].duration;};
$('range-toggle').onclick=()=>{const visible=$('range-controls').hidden;$('range-controls').hidden=!visible;$('range-toggle').setAttribute('aria-expanded',String(visible));};
function setRange(){if(!state.item)return;const seq=isSequence(),base=seq?state.item.first:0,max=seq?state.item.span-1:player().duration;let start=Number($('range-start').value)-base,end=Number($('range-end').value)-base;start=Math.max(0,Math.min(max,start));end=Math.max(start,Math.min(max,end));state.rangeStart=start;state.rangeEnd=end;$('range-start').value=start+base;$('range-end').value=end+base;if(seq&&state.frame<start)requestFrame(start,!state.playing);if(av())player().loop=nativeLoop();drawTrack();}
$('range-start').onchange=setRange;$('range-end').onchange=setRange;$('clear-range').onclick=()=>{state.rangeStart=0;state.rangeEnd=isSequence()?state.item.span-1:player().duration;$('range-controls').hidden=true;updateControls();if(av())player().loop=nativeLoop();};
$('fit').onclick=fit;$('rotate').onclick=()=>{state.view.rotation=(state.view.rotation+90)%360;state.view.zoom=null;drawPicture();layoutElement();};
$('actual-size').onclick=()=>{setPlaying(false,false);state.view.zoom=1;state.view.x=state.view.y=0;drawPicture();layoutElement();upgradeQuality();};
$('wireframe').onclick=()=>{const active=$('wireframe').getAttribute('aria-pressed')!=='true';$('wireframe').setAttribute('aria-pressed',String(active));state.model?.wireframe(active);};

function toggleSidebar(){if(state.fullscreen||document.fullscreenElement){document.body.classList.toggle('show-fullscreen-list');return;}state.stripVisible=!state.stripVisible;$('playlist-pane').hidden=!state.stripVisible;}
$('toggle-strip').onclick=toggleSidebar;$('show-sidebar').onclick=toggleSidebar;$('hide-sidebar').onclick=toggleSidebar;
let fullscreenTimer,cursorTimer,controlsHeld=false;
const inFullscreen=()=>state.fullscreen||!!document.fullscreenElement;
function hideControls(){if(!inFullscreen()||controlsHeld)return;const editing=$('controls').contains(document.activeElement)&&document.activeElement.tagName==='INPUT'&&document.activeElement.type==='number';if(editing||(CSS.supports('selector(select:open)')&&$('controls').querySelector('select:open'))){fullscreenTimer=setTimeout(hideControls,400);return;}document.body.classList.remove('controls-visible');if($('controls').contains(document.activeElement))document.activeElement.blur();}
function revealControls(){document.body.classList.add('controls-visible');document.body.classList.remove('screen-idle');clearTimeout(fullscreenTimer);if(inFullscreen())fullscreenTimer=setTimeout(hideControls,1800);}
function fullscreenState(value){state.fullscreen=value;document.body.classList.toggle('fullscreen',value);clearTimeout(fullscreenTimer);clearTimeout(cursorTimer);controlsHeld=false;document.body.classList.remove('controls-visible','screen-idle','show-fullscreen-list');if(value)cursorTimer=setTimeout(()=>document.body.classList.add('screen-idle'),1800);}
async function fullscreen(){fullscreenState(await window.viewer.fullscreen());}$('fullscreen').onclick=fullscreen;$('play-fullscreen').onclick=fullscreen;window.viewer.onFullscreen(fullscreenState);
document.addEventListener('mousemove',e=>{if(!inFullscreen())return;document.body.classList.remove('screen-idle');clearTimeout(cursorTimer);cursorTimer=setTimeout(()=>document.body.classList.add('screen-idle'),1800);if(controlsHeld||e.clientY>=innerHeight-Math.max(68,$('controls').offsetHeight+16))revealControls();else{clearTimeout(fullscreenTimer);fullscreenTimer=setTimeout(hideControls,200);}});
$('controls').addEventListener('pointerdown',()=>{controlsHeld=true;clearTimeout(fullscreenTimer);});
for(const event of ['pointerup','pointercancel'])document.addEventListener(event,()=>{if(!controlsHeld)return;controlsHeld=false;if(inFullscreen())fullscreenTimer=setTimeout(hideControls,1800);});
$('previous-page').onclick=()=>state.document?.go(state.document.page-1);$('next-page').onclick=()=>state.document?.go(state.document.page+1);$('page-number').onchange=()=>state.document?.go(Number($('page-number').value));$('document-zoom-out').onclick=()=>state.document?.zoomBy(.85);$('document-zoom-in').onclick=()=>state.document?.zoomBy(1.18);
stage.addEventListener('pointerdown',e=>{if(!isSequence()||e.button!==0||!$('error').hidden||!$('empty').hidden)return;drag={x:e.clientX,y:e.clientY,button:0,carry:0};state.scrubbing=true;stage.setPointerCapture(e.pointerId);setPlaying(false,false);});
stage.addEventListener('pointermove',e=>{if(!drag)return;const dx=e.clientX-drag.x;drag.x=e.clientX;drag.carry+=dx*.3;const whole=Math.trunc(drag.carry);if(whole){drag.carry-=whole;scheduleSeek(Math.max(0,Math.min(state.item.span-1,state.frame+whole)));}});
stage.addEventListener('pointerup',()=>{drag=null;state.scrubbing=false;scheduleQuality();});stage.addEventListener('pointercancel',()=>{drag=null;state.scrubbing=false;});stage.addEventListener('dblclick',()=>{if(state.item?.kind==='video')fullscreen();else fit();});
function changeVolume(delta){if(!av())return;volumeControl.set(player().volume+delta);setStatus('Volume '+Math.round(player().volume*100)+'%');}
stage.addEventListener('wheel',e=>{if(!state.item||['model','document','text','audio'].includes(state.item.kind))return;e.preventDefault();const dims=dimensions(),before=state.view.zoom??fitZoom(dims.width,dims.height),after=Math.max(.01,Math.min(256,before*Math.exp(-e.deltaY*.0015)));state.view.x=0;state.view.y=0;state.view.zoom=after;paintSoon();scheduleQuality();},{passive:false});
document.addEventListener('keydown',e=>{if(e.defaultPrevented)return;if(e.key==='F11'||e.altKey&&e.key==='Enter'){e.preventDefault();fullscreen();return;}if(e.key==='Escape'&&state.fullscreen&&!$('update-dialog').open){e.preventDefault();fullscreen();return;}if($('update-dialog').open||['INPUT','SELECT','TEXTAREA'].includes(e.target.tagName)||e.target.isContentEditable)return;if(e.ctrlKey&&e.key.toLowerCase()==='o'){e.preventDefault();choose(e.shiftKey?'folder':'files');return;}if(e.key==='Escape'&&state.fullscreen){e.preventDefault();fullscreen();return;}if(e.ctrlKey||e.altKey||e.metaKey)return;if(e.code==='Space'&&motion()){e.preventDefault();setPlaying(!state.playing);revealControls();}else if(['d','D','['].includes(e.key)&&['sequence','video','model'].includes(state.item?.kind)){e.preventDefault();step(-1);}else if(['f','F',']'].includes(e.key)&&['sequence','video','model'].includes(state.item?.kind)){e.preventDefault();step(1);}else if(['ArrowLeft','ArrowRight'].includes(e.key)&&av()){e.preventDefault();player().currentTime=Math.max(0,Math.min(player().duration,player().currentTime+(e.key==='ArrowRight'?5:-5)));}else if(['ArrowUp','ArrowDown'].includes(e.key)&&av()){e.preventDefault();changeVolume(e.key==='ArrowUp'?.05:-.05);}else if(e.key==='ArrowLeft'&&isSequence()){e.preventDefault();step(-1);}else if(e.key==='ArrowRight'&&isSequence()){e.preventDefault();step(1);}else if(e.key==='Backspace'){e.preventDefault();fit();}else if(e.key==='Home'&&isSequence()){setPlaying(false,false);scheduleSeek(state.rangeStart);}else if(e.key==='End'&&isSequence()){setPlaying(false,false);scheduleSeek(state.rangeEnd);}else if(e.key==='PageDown'){if(state.document)state.document.go(state.document.page+1);else openItem((state.index+1)%state.items.length);}else if(e.key==='PageUp'){if(state.document)state.document.go(state.document.page-1);else openItem((state.index-1+state.items.length)%state.items.length);}});
document.addEventListener('contextmenu',e=>e.preventDefault());
window.viewer.onPlayerAction(action=>{if(action==='play')setPlaying(!state.playing);else if(action==='previous-frame')step(-1);else if(action==='next-frame')step(1);else if(action==='previous-item')openItem((state.index-1+state.items.length)%state.items.length);else if(action==='next-item')openItem((state.index+1)%state.items.length);else if(action==='order')$('order').click();else if(action==='fullscreen')fullscreen();else if(action==='sidebar')toggleSidebar();else if(action==='reveal'&&state.item)window.viewer.reveal(state.item.id);});
let dragDepth=0;
document.addEventListener('dragenter',e=>{e.preventDefault();if([...e.dataTransfer.types].includes('Files')){dragDepth++;$('drop-overlay').hidden=false;}});
document.addEventListener('dragover',e=>{e.preventDefault();e.dataTransfer.dropEffect='copy';});
document.addEventListener('dragleave',e=>{e.preventDefault();dragDepth=Math.max(0,dragDepth-1);if(!dragDepth)$('drop-overlay').hidden=true;});
document.addEventListener('drop',async e=>{e.preventDefault();dragDepth=0;$('drop-overlay').hidden=true;try{const paths=await window.viewer.pathsForFiles(Array.from(e.dataTransfer.files));if(paths.length)importPaths(paths);}catch(error){showError(error);}});
window.addEventListener('blur',()=>{$('drop-overlay').hidden=true;dragDepth=0;});
for(const media of [video,music]){media.addEventListener('waiting',()=>{if(av()&&player()===media&&state.playing){loading(true,'Buffering…');hud.update();}});media.addEventListener('playing',()=>{if(av()&&player()===media){loading(false);updateDetails();}});media.addEventListener('canplay',()=>{if(av()&&player()===media)loading(false);});media.addEventListener('timeupdate',()=>{if(!av()||player()!==media)return;if(media.currentTime>=state.rangeEnd&&state.rangeEnd<media.duration-.01){if(state.loop)media.currentTime=state.rangeStart;else setPlaying(false,false);}updateTime();});media.addEventListener('progress',drawTrack);media.addEventListener('ended',()=>{if(!av()||player()!==media)return;if(nextPlayable())return;if(state.loop){media.currentTime=state.rangeStart;setPlaying(true);}else setPlaying(false,false);});}
video.addEventListener('loadeddata',()=>{if(state.item?.kind==='video')currentThumbnail(video);});
video.addEventListener('waiting',()=>videoThumbnails.suspend(true));
if(video.requestVideoFrameCallback){
  let previous=null;video.addEventListener('emptied',()=>{previous=null;});
  const sample=(now,metadata)=>{
    if(state.item?.kind==='video'){
      videoStats.sample(now,metadata);state.videoPresentedTime=metadata.mediaTime;
      if(previous&&!state.videoExactFps){const dt=metadata.mediaTime-previous.mediaTime,df=metadata.presentedFrames-previous.presentedFrames;if(dt>0&&dt<.2&&df>0){const estimate=df/dt;if(estimate>=10&&estimate<=240)state.videoFps=estimate;}}
      if(now-thumbnailCheckTime>=250){thumbnailCheckTime=now;syncThumbnailActivity();}
      if(!state.playing||hud.visible)hud.refresh();
    }
    previous=metadata;video.requestVideoFrameCallback(sample);
  };video.requestVideoFrameCallback(sample);
}
video.addEventListener('seeking',()=>{state.videoPresentedTime=null;});video.addEventListener('seeked',()=>hud.update());
document.addEventListener('keydown',e=>{if(e.key==='Tab'&&!e.ctrlKey&&!e.altKey&&!e.metaKey&&!['INPUT','SELECT','TEXTAREA'].includes(e.target.tagName)&&!$('update-dialog').open){e.preventDefault();hud.toggle();}});
window.viewer.onPlayerAction(action=>{if(action==='media-info')hud.toggle();});
setupSequenceExport(()=>state,result=>importPaths([result.path]));
setupRecorder(()=>{if(state.playing)setPlaying(false,false);},async path=>{if(state.item?.path!==path||video.readyState<1)await importPaths([path]);$('trim-video').click();});
setupClipper(()=>state,()=>{if(state.playing)setPlaying(false,false);},addClippedVideo);
async function addClippedVideo(result){
 if(!result.item||!result.source){await importPaths([result.path]);return;}
 const items=state.items.filter(item=>item.id!==result.item.id);let sourceIndex=items.findIndex(item=>item.id===result.sourceId);if(sourceIndex<0){items.push(result.source);sourceIndex=items.length-1;}items.splice(sourceIndex+1,0,result.item);state.items=items;state.stripVisible=true;prepareThumbnails();await openItem(sourceIndex+1);setPlaying(false,false);
}
async function loadSubtitles(item,epoch){try{const subtitle=await window.viewer.subtitles(item.id);if(!subtitle||epoch!==state.epoch)return;let text=subtitle.text.replace(/^\uFEFF/,'');if(subtitle.extension==='.srt')text='WEBVTT\n\n'+text.replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g,'$1.$2');state.subtitleUrl=URL.createObjectURL(new Blob([text],{type:'text/vtt'}));$('subtitle-track').src=state.subtitleUrl;$('subtitles').hidden=false;$('subtitles').setAttribute('aria-pressed','true');video.textTracks[0].mode='showing';}catch{}}
$('subtitles').onclick=()=>{const show=$('subtitles').getAttribute('aria-pressed')!=='true';$('subtitles').setAttribute('aria-pressed',String(show));if(video.textTracks[0])video.textTracks[0].mode=show?'showing':'hidden';};
new ResizeObserver(()=>{drawPicture();layoutElement();drawTrack();}).observe(stage);
window.viewer.onOpenPaths(importPaths);
document.body.dataset.ready='true';
const initialPaths=await window.viewer.initialPaths();if(initialPaths.length)importPaths(initialPaths);
let refreshBusy=false;
window.viewer.onMediaChanged(async folder=>{if(!isSequence()||state.item.folder!==folder||refreshBusy)return;refreshBusy=true;const id=state.item.id,oldFirst=state.item.first,oldLast=state.item.last,oldCount=state.item.frames.length;try{const updated=await window.viewer.refreshSequence(id);if(updated&&state.item?.id===id){const wasWhole=state.rangeStart===0&&state.rangeEnd===state.item.span-1;state.item=updated;state.items[state.index]=updated;state.frame=Math.max(0,oldFirst+state.frame-updated.first);if(wasWhole)state.rangeEnd=updated.span-1;updateControls();if(updated.frames.length!==oldCount||updated.last!==oldLast){buildStrip();setStatus('New frame detected · Current '+updated.frames.length+' frames');}if(!state.playing)requestFrame(state.frame,true);}}catch{}finally{refreshBusy=false;}});
function tick(now){playbackHandle=null;if(!state.playing||!isSequence())return;
  const position=sequenceClock.read(now,state.fps,state.rangeStart,state.rangeEnd);
  if(position.boundary){const whole=state.rangeStart===0&&state.rangeEnd===state.item.span-1;if(whole&&nextPlayable())return;state.request++;state.loadingFrame=false;}
  if(position.ended&&!state.loop){setPlaying(false,false);requestFrame(state.rangeEnd);return;}
  // Display the newest ready frame at or before the playhead instead of waiting on a stale decode.
  let ready=null;const wrapped=position.frame<state.frame;
  for(let frame=position.frame;frame>=Math.max(state.rangeStart,position.frame-16);frame--){if(!wrapped&&frame<=state.frame)break;if(cache.has(sourceAt(frame).source,previewEdge())){ready=frame;break;}}
  if(ready!==null&&ready!==state.frame)requestFrame(ready);
  else if(!state.loadingFrame&&position.frame!==state.frame&&now-(state.lastPaintTime||now)>500)requestFrame(position.frame);
  prefetch(position.frame);
  if(state.playing)playbackHandle=requestAnimationFrame(tick);
}
for(const id of ['range-start','range-end'])$(id).addEventListener('change',()=>sequenceClock.reset(performance.now(),Math.max(state.rangeStart,Math.min(state.rangeEnd,state.frame))));
for(const id of ['loop','clear-range'])$(id).addEventListener('click',()=>sequenceClock.reset(performance.now(),state.frame));
window.addEventListener('beforeunload',()=>{state.model?.dispose();state.document?.dispose();videoThumbnails.reset();for(const slot of cache.workers)slot.worker.terminate();});


document.querySelector('.topbar').addEventListener('pointerdown',e=>{if(e.button!==0||e.target.closest('button,input,select,a'))return;e.preventDefault();window.viewer.dragWindow?.().catch(()=>{});});
