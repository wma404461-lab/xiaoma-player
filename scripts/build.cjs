const path=require('node:path');const fs=require('node:fs/promises');const esbuild=require('esbuild');const run=require('node:util').promisify(require('node:child_process').execFile);
(async()=>{
  const root=path.resolve(__dirname,'..'),output=path.resolve(root,'build');
  if(path.dirname(output)!==root||path.basename(output)!=='build')throw new Error('Unexpected build path');
  await fs.rm(output,{recursive:true,force:true});
  await esbuild.build({entryPoints:[path.join(root,'src/renderer.js'),path.join(root,'src/image-worker.js')],outdir:output,bundle:true,format:'esm',splitting:true,platform:'browser',target:'chrome140',sourcemap:false,minify:false,logLevel:'info'});
  await fs.copyFile(path.join(root,'src/media-info-worker.cjs'),path.join(output,'media-info-worker.cjs'));
  await fs.cp(path.join(root,'node_modules/mediainfo.js/dist/cjs'),path.join(output,'metadata/cjs'),{recursive:true,filter:file=>!file.endsWith('.map')});
  await fs.mkdir(path.join(output,'metadata'),{recursive:true});await fs.copyFile(require.resolve('mediainfo.js/MediaInfoModule.wasm'),path.join(output,'metadata/MediaInfoModule.wasm'));
  await fs.mkdir(path.join(output,'encoder'),{recursive:true});const ffmpeg=require('ffmpeg-static');await fs.copyFile(ffmpeg,path.join(output,'encoder/ffmpeg.exe'));for(const suffix of ['LICENSE','README'])await fs.copyFile(ffmpeg+'.'+suffix,path.join(output,'encoder/FFmpeg.'+suffix+'.txt'));
  await fs.cp(path.join(root,'node_modules/three/examples/jsm/libs/draco/gltf'),path.join(output,'decoders/draco'),{recursive:true});
  await fs.cp(path.join(root,'node_modules/three/examples/jsm/libs/basis'),path.join(output,'decoders/basis'),{recursive:true});
  const pdfRoot=path.join(root,'node_modules/pdfjs-dist');
  for(const directory of ['cmaps','standard_fonts','wasm'])await fs.cp(path.join(pdfRoot,directory),path.join(output,'pdf',directory),{recursive:true});
  await fs.copyFile(path.join(pdfRoot,'build/pdf.worker.mjs'),path.join(output,'pdf/pdf.worker.mjs'));
  const csc=path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe');
  for(const helper of ['OfficePreview','UpdateHelper']){
    const references=helper==='OfficePreview'?['Microsoft.CSharp.dll','System.Web.Extensions.dll']:['System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Web.Extensions.dll','System.Windows.Forms.dll'];
    const sources=[path.join(root,'src',helper+'.cs')];
    if(helper==='UpdateHelper')sources.push(path.join(root,'src/native/UpdateResources.cs'));
    await run(csc,['/nologo','/target:exe',...references.map(value=>'/reference:'+value),'/out:'+path.join(output,helper+'.exe'),...sources],{windowsHide:true});
  }
})().catch(e=>{console.error(e);process.exitCode=1;});
