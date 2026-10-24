const {execFile}=require('node:child_process'),path=require('node:path');
// Refresh only this executable's cached Shell name after an in-place update.
function refreshCachedName(executable){
  if(process.platform!=='win32'||path.basename(executable)!=='小马播放器.exe')return;
  const key='HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\MuiCache',value=executable+'.FriendlyAppName';
  execFile('reg.exe',['query',key,'/v',value],{windowsHide:true},error=>{if(!error)execFile('reg.exe',['add',key,'/v',value,'/t','REG_SZ','/d','小马播放器','/f'],{windowsHide:true},()=>{});});
}
module.exports={refreshCachedName};
