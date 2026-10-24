const {app,BrowserWindow,dialog,ipcMain,protocol,shell,Menu,net}=require('electron');
const fs=require('node:fs');
const fsp=require('node:fs/promises');
const path=require('node:path');
const crypto=require('node:crypto');
const {Readable}=require('node:stream');
const {Catalog}=require('./catalog.cjs');
const {DocumentConverter}=require('./document-converter.cjs');
const {AppUpdater}=require('./app-updater.cjs');
const {refreshCachedName}=require('./branding.cjs');
const {MediaInfoReader}=require('./media-info.cjs');
const {SequenceExporter}=require('./sequence-exporter.cjs');
app.setName('小马播放器+录屏');app.setAppUserModelId('artist_m.XiaomaPlayer');

protocol.registerSchemesAsPrivileged([{scheme:'media',privileges:{standard:true,secure:true,supportFetchAPI:true,stream:true,corsEnabled:true}}]);
const profile=process.env.YIYAN_TEST_USER_DATA||(app.isPackaged?path.join(path.dirname(process.execPath),'.data'):path.join(__dirname,'../.runtime/profile'));fs.mkdirSync(profile,{recursive:true});app.setPath('userData',profile);
app.commandLine.appendSwitch('enable-unsafe-swiftshader');
if(process.env.YIYAN_TEST_MODE)app.disableHardwareAcceleration();
const roots=new Map(),rootIds=new Map();let win;const watchers=new Map();
const ownsInstance=app.requestSingleInstanceLock();if(!ownsInstance)app.quit();
const commandPaths=argv=>argv.slice(1).filter(p=>!p.startsWith('-')&&path.isAbsolute(p)&&path.resolve(p).toLowerCase()!==app.getAppPath().toLowerCase()&&fs.existsSync(p));
let pendingPaths=commandPaths(process.argv),rendererReady=false;
app.on('second-instance',(_event,argv)=>{const paths=commandPaths(argv);if(paths.length){if(rendererReady&&win&&!win.isDestroyed())win.webContents.send('open-paths',paths);else pendingPaths.push(...paths);}if(win&&!win.isDestroyed()){if(win.isMinimized())win.restore();win.show();win.focus();}});
function urlFor(file){const folder=path.dirname(file),key=folder.toLowerCase();let id=rootIds.get(key);if(!id){id=crypto.randomUUID();rootIds.set(key,id);roots.set(id,folder);}return `media://${id}/${encodeURIComponent(path.basename(file))}`;}
const catalog=new Catalog(urlFor);
const documentConverter=new DocumentConverter(path.join(profile,'document-cache'),urlFor);
const mediaInfoReader=new MediaInfoReader();
const sequenceExporter=new SequenceExporter(path.join(profile,'exports'),urlFor,progress=>{if(win&&!win.isDestroyed())win.webContents.send('export-progress',progress);});
const types={'.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp','.gif':'image/gif','.svg':'image/svg+xml','.bmp':'image/bmp','.avif':'image/avif','.mp4':'video/mp4','.m4v':'video/mp4','.mov':'video/quicktime','.webm':'video/webm','.mkv':'video/x-matroska','.ogv':'video/ogg','.glb':'model/gltf-binary','.gltf':'model/gltf+json','.bin':'application/octet-stream'};
async function serveMedia(request){
  try{
    const url=new URL(request.url),root=roots.get(url.hostname);if(!root)return new Response('Not admitted',{status:403});
    const relative=decodeURIComponent(url.pathname).replace(/^[/\\]+/,'');const file=path.resolve(root,relative);
    const containment=path.relative(root,file);if(containment.startsWith('..')||path.isAbsolute(containment))return new Response('Not admitted',{status:403});
    const actual=await fsp.realpath(file),actualRoot=await fsp.realpath(root);const realContainment=path.relative(actualRoot,actual);if(realContainment.startsWith('..')||path.isAbsolute(realContainment))return new Response('Not admitted',{status:403});
    const stat=await fsp.stat(actual);if(!stat.isFile())return new Response('Not a file',{status:404});
    const headers={'Content-Type':types[path.extname(file).toLowerCase()]||'application/octet-stream','Accept-Ranges':'bytes','Access-Control-Allow-Origin':'*','Access-Control-Expose-Headers':'Content-Length,Content-Range,Accept-Ranges','Cache-Control':'no-cache'};
    let start=0,end=stat.size-1,status=200;const range=request.headers.get('range');
    if(range){const match=range.match(/^bytes=(\d*)-(\d*)$/);if(!match)return new Response(null,{status:416,headers:{'Content-Range':`bytes */${stat.size}`}});
      if(match[1]){start=Number(match[1]);if(match[2])end=Math.min(end,Number(match[2]));}else if(match[2])start=Math.max(0,stat.size-Number(match[2]));
      if(start>end||start>=stat.size)return new Response(null,{status:416,headers:{'Content-Range':`bytes */${stat.size}`}});status=206;headers['Content-Range']=`bytes ${start}-${end}/${stat.size}`;
    }
    headers['Content-Length']=String(Math.max(0,end-start+1));
    return new Response(request.method==='HEAD'||stat.size===0?null:Readable.toWeb(fs.createReadStream(actual,{start,end})),{status,headers});
  }catch{return new Response('Cannot read file',{status:404});}
}
function watchItems(items){for(const watcher of watchers.values())watcher.close();watchers.clear();for(const folder of new Set(items.filter(i=>i.kind==='sequence').map(i=>i.folder))){let timer;try{const watcher=fs.watch(folder,(_event,filename)=>{catalog.noteChange(folder,filename);clearTimeout(timer);timer=setTimeout(()=>{if(!win?.isDestroyed())win.webContents.send('media-changed',folder);},450);});watcher.on('error',()=>{});watchers.set(folder,{close(){clearTimeout(timer);watcher.close();}});}catch{}}}
function allowedEvent(event){if(event.sender!==win?.webContents)throw new Error('Unexpected sender');}
app.whenReady().then(()=>{
  if(!ownsInstance)return;
  if(app.isPackaged)refreshCachedName(process.execPath);
  protocol.handle('media',serveMedia);
  Menu.setApplicationMenu(null);
  win=new BrowserWindow({width:1240,height:830,minWidth:680,minHeight:480,title:'小马播放器+录屏',icon:path.join(__dirname,'../assets/icon.ico'),backgroundColor:'#171a1f',show:!process.env.YIYAN_TEST_MODE,webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true,backgroundThrottling:!process.env.YIYAN_TEST_MODE,offscreen:!!process.env.YIYAN_TEST_MODE,autoplayPolicy:'no-user-gesture-required'}});
  win.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  if(process.env.YIYAN_TEST_MODE)win.webContents.setAudioMuted(true);
  win.webContents.on('will-navigate',(e,url)=>{if(url!==win.webContents.getURL())e.preventDefault();});
  win.webContents.session.setPermissionRequestHandler((_contents,_permission,callback)=>callback(false));
  win.loadFile(path.join(__dirname,'index.html'));
  win.on('enter-full-screen',()=>win.webContents.send('fullscreen-state',true));win.on('leave-full-screen',()=>win.webContents.send('fullscreen-state',false));
  const updater=new AppUpdater(app,profile,net.fetch.bind(net),progress=>{if(!win?.isDestroyed())win.webContents.send('update-progress',progress);});
  ipcMain.handle('app-version',event=>{allowedEvent(event);return app.getVersion();});
  ipcMain.handle('update-check',event=>{allowedEvent(event);return updater.check();});
  ipcMain.handle('update-download',event=>{allowedEvent(event);return updater.download();});
  ipcMain.handle('update-install',event=>{allowedEvent(event);return updater.install();});
  ipcMain.handle('scan',async(event,paths)=>{allowedEvent(event);if(!Array.isArray(paths)||paths.length>5000)throw new Error('文件数量过多');const result=await catalog.scan(paths);watchItems(result.items);return result;});
  ipcMain.handle('choose',async(event,mode)=>{allowedEvent(event);const result=await dialog.showOpenDialog(win,{title:mode==='folder'?'选择要查看的文件夹':'选择文件',properties:mode==='folder'?['openDirectory']:['openFile','multiSelections']});return result.canceled?[]:result.filePaths;});
  ipcMain.handle('initial-paths',event=>{allowedEvent(event);rendererReady=true;const paths=pendingPaths;pendingPaths=[];return paths;});
  ipcMain.handle('fullscreen',event=>{allowedEvent(event);const next=!win.isFullScreen();win.setFullScreen(next);win.webContents.send('fullscreen-state',next);return next;});
  ipcMain.handle('document-preview',async(event,id)=>{allowedEvent(event);const item=catalog.items.get(id);if(item?.kind!=='document')throw new Error('不是文档文件');return documentConverter.preview(item);});
  ipcMain.handle('media-info',async(event,id)=>{allowedEvent(event);const item=catalog.items.get(id);if(!['video','audio'].includes(item?.kind))throw new Error('不是音频或视频');return mediaInfoReader.read(item);});
  ipcMain.handle('export-start',async(event,id,options={})=>{allowedEvent(event);const item=catalog.items.get(id);if(item?.kind!=='sequence')throw new Error('请先打开编号序列');const selected=await dialog.showSaveDialog(win,{title:'合成高画质 MP4',defaultPath:path.join(item.folder,item.name+'.mp4'),filters:[{name:'MP4 视频',extensions:['mp4']}]});if(selected.canceled)return null;return sequenceExporter.start(item,selected.filePath,options);});
  ipcMain.handle('export-frame',(event,id,index,png)=>{allowedEvent(event);return sequenceExporter.write(id,index,png);});
  ipcMain.handle('export-finish',(event,id)=>{allowedEvent(event);return sequenceExporter.finish(id);});
  ipcMain.handle('export-next-pass',(event,id)=>{allowedEvent(event);return sequenceExporter.nextPass(id);});
  ipcMain.handle('export-cancel',(event,id)=>{allowedEvent(event);return sequenceExporter.cancel(id);});
  ipcMain.handle('subtitles',async(event,id)=>{allowedEvent(event);const item=catalog.items.get(id);if(item?.kind!=='video')return null;const base=item.path.slice(0,-path.extname(item.path).length);for(const extension of ['.vtt','.srt']){try{const stat=await fsp.stat(base+extension);if(stat.size>5*1024*1024)continue;return {extension,text:await fsp.readFile(base+extension,'utf8')};}catch{}}return null;});
  ipcMain.handle('player-menu',event=>{allowedEvent(event);return null;});
  ipcMain.handle('refresh-sequence',async(event,id)=>{allowedEvent(event);return catalog.refreshSequence(id);});
  ipcMain.handle('reveal',async(event,id)=>{allowedEvent(event);const item=catalog.items.get(id);if(item)shell.showItemInFolder(item.path);});
  ipcMain.handle('memory-budget',event=>{allowedEvent(event);if(process.env.YIYAN_TEST_MODE&&Number(process.env.YIYAN_TEST_BUDGET)>0)return Number(process.env.YIYAN_TEST_BUDGET);const os=require('node:os');return Math.min(256*1024*1024,Math.max(32*1024*1024,os.freemem()*.05));});
  win.on('closed',()=>{sequenceExporter.close();for(const watcher of watchers.values())watcher.close();watchers.clear();});
});
app.on('window-all-closed',()=>app.quit());
