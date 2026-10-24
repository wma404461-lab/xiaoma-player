(() => {
 if(!window.chrome?.webview)return;
 const bridge=window.chrome.webview,pending=new Map(),listeners=new Map();let next=0;
 bridge.addEventListener('message',({data})=>{if(data.eventName){for(const callback of listeners.get(data.eventName)||[])callback(data.value);return;}const request=pending.get(data.id);if(!request)return;pending.delete(data.id);data.error?request.reject(new Error(data.error)):request.resolve(data.result);});
 const invoke=(method,...args)=>new Promise((resolve,reject)=>{const id=++next;pending.set(id,{resolve,reject});bridge.postMessage({id,method,args});});
 const on=name=>callback=>{if(!listeners.has(name))listeners.set(name,new Set());listeners.get(name).add(callback);return ()=>listeners.get(name).delete(callback);};
 let metadataWorker,metadataId=0;const metadataPending=new Map(),metadataCache=new Map();
 const readInfo=async id=>{const file=await invoke('file-info',id),key=file.url+'@'+file.size+':'+file.mtime;if(metadataCache.has(key))return metadataCache.get(key);if(!metadataWorker){metadataWorker=new Worker(new URL('../build/native-media-worker.js',location.href),{type:'module'});metadataWorker.onmessage=({data})=>{const job=metadataPending.get(data.id);if(!job)return;metadataPending.delete(data.id);data.error?job.reject(new Error(data.error)):job.resolve(data.result);};metadataWorker.onerror=()=>{for(const job of metadataPending.values())job.reject(new Error('媒体信息暂不可用'));metadataPending.clear();metadataWorker.terminate();metadataWorker=null;};}const request=++metadataId,result=await new Promise((resolve,reject)=>{metadataPending.set(request,{resolve,reject});metadataWorker.postMessage({id:request,file});});metadataCache.set(key,result);while(metadataCache.size>40)metadataCache.delete(metadataCache.keys().next().value);return result;};
 window.viewer=Object.freeze({
  native:true,
  contentReady:()=>invoke('content-ready'),dragWindow:()=>invoke('window-drag'),recorderExit:()=>invoke('recorder-exit'),
  recorderReveal:()=>invoke('recorder-reveal'),
  recorderShowRegion:()=>invoke('recorder-show-region'),recorderHideRegion:()=>invoke('recorder-hide-region'),trimVideo:(id,options)=>invoke('trim-video',id,options),cancelTrim:()=>invoke('trim-cancel'),onTrimProgress:on('trim-progress'),
  recorderConfig:()=>invoke('recorder-config'),recorderRegion:display=>invoke('recorder-region',display),recorderFolder:()=>invoke('recorder-folder'),recorderOpenFolder:()=>invoke('recorder-open-folder'),recorderStart:options=>invoke('recorder-start',options),recorderStop:()=>invoke('recorder-stop'),recorderPause:()=>invoke('recorder-pause'),recorderStatus:()=>invoke('recorder-status'),onRecorderStatus:on('recorder-status'),onRecorderHotkey:on('recorder-hotkey'),
  prepareThumbnails:(ids,batch)=>invoke('thumbnails-start',ids,batch),onThumbnail:on('thumbnail-ready'),
  pathsForFiles:files=>new Promise((resolve,reject)=>{const id=++next;pending.set(id,{resolve,reject});bridge.postMessageWithAdditionalObjects({id,method:'file-paths',args:[]},Array.from(files));}),
  scan:paths=>invoke('scan',paths),choose:mode=>invoke('choose',mode),refreshSequence:id=>invoke('refresh-sequence',id),reveal:id=>invoke('reveal',id),fileInfo:id=>invoke('file-info',id),memoryBudget:()=>invoke('memory-budget'),initialPaths:()=>invoke('initial-paths'),fullscreen:()=>invoke('fullscreen'),
  appVersion:()=>invoke('app-version'),checkUpdate:()=>invoke('update-check'),downloadUpdate:()=>invoke('update-download'),cancelUpdate:()=>invoke('update-cancel'),installUpdate:()=>invoke('update-install'),onUpdateProgress:on('update-progress'),onFullscreen:on('fullscreen-state'),
  documentPreview:id=>invoke('document-preview',id),subtitles:id=>invoke('subtitles',id),mediaInfo:readInfo,
  startExport:(id,options)=>invoke('export-start',id,options),
  exportFrame:async(id,index,png)=>{const result=await fetch(new URL('../export/'+id+'/'+index,location.href),{method:'POST',body:png});if(!result.ok)throw new Error('合成帧读取失败');return true;},
  finishExport:id=>invoke('export-finish',id),nextExportPass:id=>invoke('export-next-pass',id),cancelExport:id=>invoke('export-cancel',id),onExportProgress:on('export-progress'),gpuStatus:()=>invoke('gpu-status'),
  playerMenu:options=>invoke('player-menu',options),onPlayerAction:on('player-action'),onMediaChanged:on('media-changed'),onOpenPaths:on('open-paths')
 });
})();
