const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
const sharp=require('sharp');

async function generate(){
 const root=path.resolve(__dirname,'..');
 if(!sharp)throw new Error('sharp runtime is required');
 const primary=await fs.readFile(path.join(root,'assets/icon.svg'),'utf8'),small=await fs.readFile(path.join(root,'assets/icon-16.svg'),'utf8'),medium=await fs.readFile(path.join(root,'assets/icon-24.svg'),'utf8');
 const dark=svg=>svg.replace('data-variant="primary"','data-variant="dark-background"').replace('stroke-opacity=".18"','stroke-opacity=".38"');
 const sizes=[16,20,24,32,40,48,64,96,128,256];
 for(const [suffix,source] of [['',primary],['-inverse',dark(primary)]]){
  const images=[];for(const size of sizes){let optical=size===16?small:size===20||size===24?medium:primary;if(suffix)optical=dark(optical);images.push(await sharp(Buffer.from(optical),{density:384}).resize(size,size).png({compressionLevel:9,effort:10}).toBuffer());}
  const directory=Buffer.alloc(6+16*sizes.length);directory.writeUInt16LE(1,2);directory.writeUInt16LE(sizes.length,4);let offset=directory.length;
  for(let i=0;i<sizes.length;i++){const p=6+i*16;directory[p]=sizes[i]===256?0:sizes[i];directory[p+1]=directory[p];directory.writeUInt16LE(1,p+4);directory.writeUInt16LE(32,p+6);directory.writeUInt32LE(images[i].length,p+8);directory.writeUInt32LE(offset,p+12);offset+=images[i].length;}
  await fs.writeFile(path.join(root,'assets/icon'+suffix+'.ico'),Buffer.concat([directory,...images]));await fs.writeFile(path.join(root,'assets/icon'+suffix+'.png'),await sharp(Buffer.from(source),{density:384}).resize(512,512).png({compressionLevel:9,effort:10}).toBuffer());if(suffix)await fs.writeFile(path.join(root,'assets/icon'+suffix+'.svg'),source);
 }
 return {specRevision:'2026-10-12-free-app-icon-design-v4',style:'blue-ivory-horse-media',horseMark:'xiaoma-media-horse-v4',editableVector:true,png:512,icoSizes:sizes,opticalSizes:[16,24],darkBackgroundVariant:true};
}
exports.generate=generate;
if(require.main===module)generate().then(value=>console.log(JSON.stringify(value))).catch(error=>{console.error(error);process.exitCode=1;});
