const $=id=>document.getElementById(id),dialog=$('update-dialog');
let version='',busy=false,checking=null;
window.viewer.appVersion().then(value=>{version=value;$('check-update').title='小马播放器+录屏 '+value+' · 检查更新';$('update-version').textContent='当前版本 '+value;});
async function check(silent=false){
  if(!silent){if(!dialog.open)dialog.showModal();$('update-message').textContent='正在检查更新…';$('retry-update').hidden=true;$('install-update').hidden=true;}if(checking)return checking;
  checking=(async()=>{try{const info=await window.viewer.checkUpdate();$('check-update').textContent=info.available?'有更新':'检查更新';$('check-update').classList.toggle('available',info.available);$('update-message').textContent=info.available?'新版本 '+info.version+' 已准备好':'已经是最新版本';$('update-notes').textContent=info.notes;$('update-notes').hidden=!info.available||!info.notes;$('update-hint').hidden=!info.available;$('install-update').hidden=!info.available;$('retry-update').hidden=info.available;return info;}catch{$('update-message').textContent='暂时连不上更新服务器，稍后可以重试。';$('retry-update').hidden=false;}finally{checking=null;}})();return checking;
}
$('check-update').onclick=()=>{if(busy){if(!dialog.open)dialog.showModal();}else check();};$('retry-update').onclick=()=>check();$('close-update').onclick=()=>dialog.close();dialog.addEventListener('close',()=>{if(busy&&window.viewer.cancelUpdate)window.viewer.cancelUpdate().catch(()=>{});});
window.viewer.onUpdateProgress(info=>{$('update-progress').value=info.percent;$('update-message').textContent=info.phase==='prepare'?'正在校验并准备更新…':'正在下载更新 '+info.percent+'%';});
$('install-update').onclick=async()=>{if(busy)return;busy=true;$('install-update').disabled=true;$('retry-update').hidden=true;$('update-progress').hidden=false;try{const result=await window.viewer.downloadUpdate();if(result.current){$('update-message').textContent='已经是最新版本';return;}$('update-message').textContent='正在重新打开播放器…';await window.viewer.installUpdate();}catch{$('update-message').textContent='更新未完成，点击重新检查后再试。';$('retry-update').hidden=false;}finally{busy=false;$('install-update').disabled=false;$('update-progress').hidden=true;}};
setTimeout(()=>check(true),6000);
