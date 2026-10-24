const fs=require('node:fs/promises'),path=require('node:path'),esbuild=require('esbuild'),run=require('node:util').promisify(require('node:child_process').execFile);
(async()=>{
 const root=path.resolve(__dirname,'..'),version=require('../package.json').version,out=path.resolve(root,process.env.PLAYER_PACKAGE_DIR||'.runtime/package-stage'),app=path.join(out,'resources/app'),build=path.join(app,'build'),sdk=path.join(root,'.runtime/native-sdk');
 if(!out.startsWith(root+path.sep)||out===path.join(root,'成品'))throw new Error('Build in a staging directory before replacing the original installation');
 await require('./create-icon.cjs').generate();
 await fs.mkdir(path.join(app,'src'),{recursive:true});await fs.mkdir(build,{recursive:true});
 await fs.copyFile(path.join(root,'docs','录屏使用说明.txt'),path.join(out,'录屏使用说明.txt'));
 for(const file of ['index.html','style.css','layout.css','theme.css','recorder.css','readability.css','glass.css','timeline-clipper.css','native-preload.js','update-config.json'])await fs.copyFile(path.join(root,'src',file),path.join(app,'src',file));
 await fs.cp(path.join(root,'assets'),path.join(app,'assets'),{recursive:true,filter:file=>!['icon-inverse.png','icon-inverse.ico'].includes(path.basename(file))});
 await esbuild.build({entryPoints:['renderer','image-worker','native-media-worker'].map(file=>path.join(root,'src',file+'.js')),outdir:build,bundle:true,format:'esm',splitting:true,platform:'browser',target:'chrome140',minify:true,sourcemap:false,logLevel:'info'});
 await fs.mkdir(path.join(build,'metadata'),{recursive:true});await fs.copyFile(require.resolve('mediainfo.js/MediaInfoModule.wasm'),path.join(build,'metadata/MediaInfoModule.wasm'));
 for(const folder of ['encoder','decoders','pdf'])await fs.cp(path.join(root,'build',folder),path.join(build,folder),{recursive:true});
 await require('./pack-encoder.cjs').prepare(out);
 for(const dll of ['Core','WinForms'])await fs.copyFile(path.join(sdk,'webview2/lib/net462/Microsoft.Web.WebView2.'+dll+'.dll'),path.join(out,'Microsoft.Web.WebView2.'+dll+'.dll'));
 await fs.copyFile(path.join(sdk,'webview2/runtimes/win-x64/native/WebView2Loader.dll'),path.join(out,'WebView2Loader.dll'));
 await fs.copyFile(path.join(sdk,'crypto/lib/net461/BouncyCastle.Cryptography.dll'),path.join(out,'BouncyCastle.Cryptography.dll'));
 await fs.writeFile(path.join(app,'package.json'),JSON.stringify({name:'yiyan-viewer',productName:'小马播放器+录屏',version,engine:'WebView2 Evergreen',description:'小马播放器+录屏'},null,2));
 const framework=path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319'),csc='C:/Program Files/dotnet/sdk/10.0.201/Roslyn/bincore/csc.dll';
 const attributes=path.join(sdk,'Version.cs');await fs.writeFile(attributes,'using System.Reflection;\n[assembly: AssemblyTitle("小马播放器+录屏")]\n[assembly: AssemblyDescription("小马播放器+录屏")]\n[assembly: AssemblyProduct("小马播放器+录屏")]\n[assembly: AssemblyCompany("artist_m")]\n[assembly: AssemblyVersion("'+version+'.0")]\n[assembly: AssemblyFileVersion("'+version+'.0")]\n');
 const references=['mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'].map(f=>path.join(framework,f)).concat([path.join(out,'Microsoft.Web.WebView2.Core.dll'),path.join(out,'Microsoft.Web.WebView2.WinForms.dll'),path.join(out,'BouncyCastle.Cryptography.dll')]);
 const recorderDll=path.join(sdk,'recorder','ScreenRecorderLib.dll');await fs.copyFile(recorderDll,path.join(out,'ScreenRecorderLib.dll'));references.push(recorderDll);
 for(const entry of [['core','NAudio.Core.dll'],['wasapi','NAudio.Wasapi.dll']]){const dll=path.join(sdk,'naudio',entry[0],'lib','netstandard2.0',entry[1]);await fs.copyFile(dll,path.join(out,entry[1]));references.push(dll);}references.push(path.join(framework,'netstandard.dll'));
 const sources=(await fs.readdir(path.join(root,'src/native'))).filter(f=>f.endsWith('.cs')).map(f=>path.join(root,'src/native',f));
 try{await run('dotnet',[csc,'/nologo','/nostdlib+','/optimize+','/platform:x64','/target:winexe',...references.map(r=>'/reference:'+r),'/win32icon:'+path.join(root,'assets/icon.ico'),'/out:'+path.join(out,'小马播放器核心.exe'),attributes,...sources],{windowsHide:true});}catch(error){throw new Error(error.stdout+'\n'+error.stderr);}
 // The TinyCC startup shell was quarantined by Windows Defender on 2026-10-22.
 // Use the complete managed application as each entrypoint: its native home,
 // file arguments, named-pipe single instance, icon and version are already built
 // above. Do not restore the quarantined binary or change security exclusions.
 await fs.copyFile(path.join(out,'小马播放器核心.exe'),path.join(out,'小马播放器.exe'));
 await fs.copyFile(path.join(out,'小马播放器.exe'),path.join(out,'小马播放器+录屏.exe'));
 await fs.copyFile(path.join(root,'build','OfficePreview.exe'),path.join(build,'OfficePreview.exe'));
 const updateRefs=['mscorlib.dll','System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll'].map(f=>'/reference:'+path.join(framework,f));
 try{await run('dotnet',[csc,'/nologo','/nostdlib+','/optimize+','/platform:x64','/target:exe',...updateRefs,'/win32icon:'+path.join(root,'assets/icon.ico'),'/out:'+path.join(build,'UpdateHelper.exe'),path.join(root,'src','UpdateHelper.cs'),path.join(root,'src/native/UpdateResources.cs')],{windowsHide:true});}catch(error){throw new Error(error.stdout+'\n'+error.stderr);}
 await fs.writeFile(path.join(out,'小马播放器.exe.config'),'<?xml version="1.0"?><configuration><System.Windows.Forms.ApplicationConfigurationSection><add key="DpiAwareness" value="PerMonitorV2" /></System.Windows.Forms.ApplicationConfigurationSection><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>');
 await fs.copyFile(path.join(out,'小马播放器.exe.config'),path.join(out,'小马播放器+录屏.exe.config'));
 await fs.copyFile(path.join(out,'小马播放器.exe.config'),path.join(out,'小马播放器核心.exe.config'));
 let notices='小马播放器+录屏使用系统 Microsoft Edge WebView2 Evergreen Runtime (.NET Framework 4.8)。Windows 11 默认自带 WebView2；其他电脑需安装微软 WebView2 Runtime。软件包不附带浏览器内核。\nMicrosoft WebView2 SDK 1.0.3912.50\n'+await fs.readFile(path.join(sdk,'webview2/LICENSE.txt'),'utf8');
 for(const pkg of ['three','utif','pdfjs-dist','docx-preview','jszip','mediainfo.js'])for(const file of ['LICENSE','LICENSE.txt','LICENSE.md'])try{notices+='\n'+pkg+'\n'+await fs.readFile(path.join(root,'node_modules',pkg,file),'utf8');break;}catch{}
 notices+='\nBouncy Castle: https://www.bouncycastle.org/about/license/\n'+await fs.readFile(path.join(sdk,'crypto/LICENSE.md'),'utf8');
 notices+='\nScreenRecorderLib 7.0.1 (MIT), https://github.com/sskodje/ScreenRecorderLib\n'+await fs.readFile(path.join(sdk,'recorder','LICENSE.txt'),'utf8');
 notices+='\nNAudio 2.2.1 (MIT), https://github.com/naudio/NAudio\n'+await fs.readFile(path.join(sdk,'naudio','LICENSE.txt'),'utf8');
 notices+='\nFFmpeg is a separate encoder executable. GPL license and build/source information are included in resources/app/build/encoder.\n';
 await fs.writeFile(path.join(out,'THIRD_PARTY_NOTICES.txt'),notices);await fs.copyFile(path.join(root,'node_modules/.pnpm/pako@1.0.11/node_modules/pako/LICENSE'),path.join(out,'THIRD_PARTY_PAKO_LICENSE.txt'));
 if(!process.env.PLAYER_PACKAGE_DIR)await require('./install-original.cjs').installOriginal(out);
 console.log(JSON.stringify({exe:path.join(process.env.PLAYER_PACKAGE_DIR?out:path.join(root,'成品'),'小马播放器.exe'),version}));
})().catch(error=>{console.error(error);process.exitCode=1;});

