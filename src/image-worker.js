function dimensions(bytes){
  const d=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
  if(bytes[0]===137&&bytes[1]===80&&bytes.length>=24)return {width:d.getUint32(16),height:d.getUint32(20)};
  if(bytes[0]===66&&bytes[1]===77&&bytes.length>=26)return {width:Math.abs(d.getInt32(18,true)),height:Math.abs(d.getInt32(22,true))};
  if(bytes[0]===255&&bytes[1]===216){let offset=2,orientation=1;while(offset+9<bytes.length){if(bytes[offset]!==255){offset++;continue;}const marker=bytes[offset+1];if(marker===217||marker===218)break;const size=d.getUint16(offset+2);if(size<2)break;
    if(marker===225&&offset+18<bytes.length&&bytes[offset+4]===69&&bytes[offset+5]===120){try{const tiff=offset+10,little=d.getUint16(tiff)===0x4949,ifd=tiff+d.getUint32(tiff+4,little),count=d.getUint16(ifd,little);for(let i=0;i<count;i++){const pos=ifd+2+i*12;if(pos+12>bytes.length)break;if(d.getUint16(pos,little)===0x112)orientation=d.getUint16(pos+8,little);}}catch{}}
    if([192,193,194,195,197,198,199,201,202,203,205,206,207].includes(marker)){let width=d.getUint16(offset+7),height=d.getUint16(offset+5);if(orientation>=5&&orientation<=8)[width,height]=[height,width];return {width,height};}offset+=size+2;
  }}return null;
}
self.onmessage=async({data:{id,url,ext,edge,encodePng=false}})=>{
  try{
    const nativePreview=edge&&!encodePng&&/^http:\/\/127\.0\.0\.1:\d+\/[^/]+\/media\//.test(url)&&['.png','.bmp'].includes(ext);
    const response=await fetch(nativePreview?url+(url.includes('?')?'&':'?')+'preview='+edge:url);if(!response.ok)throw new Error('文件读取失败，可能仍在写入');let result;
    if(['.exr','.hdr','.tga','.tif','.tiff'].includes(ext)){
      const {decodeFormat}=await import('./format-decoders.js');result=decodeFormat(await response.arrayBuffer(),ext,edge);
    }else{
      const blob=await response.blob(),size=dimensions(new Uint8Array(await blob.slice(0,128*1024).arrayBuffer()));let options={imageOrientation:'from-image'};
      if(size&&size.width*size.height>100000000)throw new Error('图片尺寸过大');
      if(edge&&size&&Math.max(size.width,size.height)>edge){const ratio=edge/Math.max(size.width,size.height);options={...options,resizeWidth:Math.max(1,Math.round(size.width*ratio)),resizeHeight:Math.max(1,Math.round(size.height*ratio)),resizeQuality:'medium'};}
      let bitmap=await createImageBitmap(blob,options),width=Number(response.headers.get('X-Image-Width'))||size?.width||bitmap.width,height=Number(response.headers.get('X-Image-Height'))||size?.height||bitmap.height,alpha=false;
      if(width*height>100000000){bitmap.close();throw new Error('图片尺寸过大');}
      if(edge&&Math.max(bitmap.width,bitmap.height)>edge){const ratio=edge/Math.max(bitmap.width,bitmap.height),surface=new OffscreenCanvas(Math.max(1,Math.round(bitmap.width*ratio)),Math.max(1,Math.round(bitmap.height*ratio)));surface.getContext('2d').drawImage(bitmap,0,0,surface.width,surface.height);bitmap.close();bitmap=surface.transferToImageBitmap();}
      if(['.png','.webp','.avif','.ico'].includes(ext)){const probe=new OffscreenCanvas(24,24),c=probe.getContext('2d',{willReadFrequently:true});c.drawImage(bitmap,0,0,24,24);const data=c.getImageData(0,0,24,24).data;for(let i=3;i<data.length;i+=4)if(data[i]<255){alpha=true;break;}}
      result={bitmap,width,height,alpha};
    }
    const {bitmap}=result;
    if(encodePng){const surface=new OffscreenCanvas(bitmap.width,bitmap.height),context=surface.getContext('2d',{alpha:false});context.fillStyle='#000';context.fillRect(0,0,surface.width,surface.height);context.drawImage(bitmap,0,0);bitmap.close();const png=await (await surface.convertToBlob({type:'image/png'})).arrayBuffer();surface.width=surface.height=1;self.postMessage({id,png,width:result.width,height:result.height},[png]);}
    else self.postMessage({id,...result,bytes:bitmap.width*bitmap.height*4},[bitmap]);
  }catch(error){self.postMessage({id,error:error.message||'图片无法解码'});}
};
