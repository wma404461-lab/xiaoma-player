import {EXRLoader} from 'three/addons/loaders/EXRLoader.js';
import {HDRLoader} from 'three/addons/loaders/HDRLoader.js';
import {TGALoader} from 'three/addons/loaders/TGALoader.js';
import {FloatType,HalfFloatType,DataUtils} from 'three';
import UTIF from 'utif';
const display=v=>{v=Math.max(0,v);v=Math.min(1,(v*(2.51*v+.03))/(v*(2.43*v+.59)+.14));return Math.round(255*(v<=.0031308?v*12.92:1.055*Math.pow(v,1/2.4)-.055));};
let halfColor,halfAlpha;
function halfTables(){if(halfColor)return;halfColor=new Uint8Array(65536);halfAlpha=new Uint8Array(65536);for(let i=0;i<65536;i++){const f=DataUtils.fromHalfFloat(i);halfColor[i]=display(f);halfAlpha[i]=Math.max(0,Math.min(255,Math.round(f*255)));}}
export function decodeFormat(buffer,ext,edge){
  let decoded;
  if(ext==='.exr')decoded=new EXRLoader().setDataType(edge?HalfFloatType:FloatType).parse(buffer);
  else if(ext==='.hdr')decoded=new HDRLoader().setDataType(FloatType).parse(buffer);
  else if(ext==='.tga')decoded=new TGALoader().parse(buffer);
  else{const pages=UTIF.decode(buffer),page=pages.find(i=>i.t256&&i.t257)||pages[0];if(!page)throw new Error('TIFF no image inside');UTIF.decodeImage(buffer,page);decoded={width:page.width,height:page.height,data:UTIF.toRGBA8(page)};}
  const {width,height}=decoded;if(!width||!height||width*height>100000000)throw new Error('image size is too large');
  const ratio=edge?Math.min(1,edge/Math.max(width,height)):1,w=Math.max(1,Math.round(width*ratio)),h=Math.max(1,Math.round(height*ratio)),floating=ext==='.exr'||ext==='.hdr',half=decoded.data instanceof Uint16Array&&floating;
  if(half)halfTables();const components=Math.round(decoded.data.length/(width*height)),rgba=new Uint8ClampedArray(w*h*4);let alpha=false;
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){const sx=Math.min(width-1,Math.floor((x+.5)*width/w)),sy=Math.min(height-1,Math.floor((y+.5)*height/h)),sourceY=ext==='.exr'?height-1-sy:sy,source=(sourceY*width+sx)*components,dest=(y*w+x)*4,r=decoded.data[source],g=components>=3?decoded.data[source+1]:r,b=components>=3?decoded.data[source+2]:r;rgba[dest]=half?halfColor[r]:floating?display(r):r;rgba[dest+1]=half?halfColor[g]:floating?display(g):g;rgba[dest+2]=half?halfColor[b]:floating?display(b):b;const a=components===4?decoded.data[source+3]:(floating?1:255);rgba[dest+3]=half&&components===4?halfAlpha[a]:floating?Math.max(0,Math.min(255,Math.round(a*255))):a;if(rgba[dest+3]<255)alpha=true;}
  const surface=new OffscreenCanvas(w,h);surface.getContext('2d').putImageData(new ImageData(rgba,w,h),0,0);return {bitmap:surface.transferToImageBitmap(),width,height,alpha};
}
