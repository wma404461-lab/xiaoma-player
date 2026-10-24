const fs=require('node:fs/promises'),path=require('node:path'),{execFile}=require('node:child_process'),run=require('node:util').promisify(execFile);
exports.installOriginal=async function(stage){
 const root=path.resolve(__dirname,'..'),target=path.join(root,'dist');stage=path.resolve(stage);
 const inside=p=>{const relative=path.relative(root,path.resolve(p));if(!relative||relative.startsWith('..')||path.isAbsolute(relative))throw new Error('Installation path must stay inside this project');};
 inside(stage);inside(target);if(stage===target||stage.startsWith(target+path.sep)||target.startsWith(stage+path.sep))throw new Error('Staging and original installation must be separate');
 await fs.access(path.join(stage,'Xiaoma Player.exe'));await fs.access(path.join(stage,'resources/app/src/index.html'));
 const result=await run('powershell.exe',['-NoProfile','-Command',"@(Get-CimInstance Win32_Process -Filter \"Name = 'Xiaoma Player.exe' OR Name = 'Xiaoma Player + Recorder.exe' OR Name = 'Xiaoma Player Core.exe'\" | Where-Object { $_.ExecutablePath -eq $env:PLAYER_ORIGINAL_EXE -or $_.ExecutablePath -eq $env:PLAYER_RENAMED_EXE -or $_.ExecutablePath -eq $env:PLAYER_CORE_EXE } | Select-Object -ExpandProperty ProcessId) | ConvertTo-Json -Compress"],{windowsHide:true,env:{...process.env,PLAYER_ORIGINAL_EXE:path.join(target,'Xiaoma Player.exe'),PLAYER_RENAMED_EXE:path.join(target,'Xiaoma Player + Recorder.exe'),PLAYER_CORE_EXE:path.join(target,'Xiaoma Player Core.exe')}});
 if(result.stdout.trim()&&JSON.parse(result.stdout.trim()).length!==0)throw new Error('Original player is still running; please close it before replacement; build cache has already been completed.');
 const backup=path.join(root,'.runtime/in-place-backups',new Date().toISOString().replace(/[:.]/g,'-'));inside(backup);await fs.mkdir(backup,{recursive:true});await fs.mkdir(target,{recursive:true});
 let previous;try{previous=JSON.parse(await fs.readFile(path.join(root,'.runtime/last-in-place-repair.json'),'utf8'));}catch{}let originalPackage;try{originalPackage=JSON.parse(await fs.readFile(path.join(target,'resources/app/package.json'),'utf8'));}catch{}
 const legacyBackup=originalPackage&&!originalPackage.engine?backup:previous?.legacyBackup||previous?.backup;
 const old=[],installed=[];
 try{
  for(const entry of await fs.readdir(target)){if(entry==='.data')continue;const from=path.join(target,entry),to=path.join(backup,entry);inside(from);inside(to);await fs.rename(from,to);old.push(entry);}
  for(const entry of await fs.readdir(stage)){if(entry==='.data')continue;const from=path.join(stage,entry),to=path.join(target,entry);inside(from);inside(to);installed.push(entry);await fs.cp(from,to,{recursive:true});}
 }catch(error){
  const failed=backup+'-failed';inside(failed);await fs.mkdir(failed,{recursive:true});for(const entry of installed){const from=path.join(target,entry),to=path.join(failed,entry);inside(from);inside(to);if(await fs.access(from).then(()=>true,()=>false))await fs.rename(from,to);}
  for(const entry of old){const from=path.join(backup,entry),to=path.join(target,entry);inside(from);inside(to);await fs.rename(from,to);}throw error;
 }
 await fs.writeFile(path.join(root,'.runtime/last-in-place-repair.json'),JSON.stringify({target,backup,legacyBackup,stage,preservedProfile:path.join(target,'.data'),at:new Date().toISOString()},null,2));
 console.log('Original program has been fixed: '+path.join(target,'Xiaoma Player.exe'));console.log('Backup of original program: '+backup);
};
if(require.main===module)exports.installOriginal(process.argv[2]).catch(error=>{console.error(error);process.exitCode=1;});
