const path=require('node:path'),fs=require('node:fs'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..'),exe=path.join(root,'dist','Xiaoma Player + Recorder.exe');
if(!fs.existsSync(exe)){console.error('Please run first pnpm package, Generate current version.');process.exitCode=1;}
else{const child=spawn(exe,process.argv.slice(2),{cwd:path.dirname(exe),windowsHide:false,stdio:'inherit'});child.on('error',error=>{console.error(error.message);process.exitCode=1;});child.on('exit',code=>process.exitCode=code||0);}
