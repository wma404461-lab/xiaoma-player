function encodingPlan(targetMB,frames,fps){
 if(targetMB===undefined||targetMB===null)return {passes:1,targetMB:null,bitrate:null};
 const size=Number(targetMB);if(!Number.isFinite(size)||size<.1||size>100000)throw new Error('Please enter target size 0.1 to 100000 MB');
 const budget=Math.floor(size*1024*1024),overhead=Math.max(16384,frames*64),bitrate=Math.floor((budget*.97-overhead)*8/(frames/fps));
 if(bitrate<8000)throw new Error('This sequence is long and the target size is too small, please increase it MB value');
 return {passes:2,targetMB:size,bitrate};
}
function encodingArguments(job,pass){
 const limited=job.plan.passes===2,filter=limited?'pad=ceil(iw/2)*2:ceil(ih/2)*2:color=black,scale=in_range=pc:out_range=tv:out_color_matrix=bt709':'scale=in_range=pc:out_range=pc:out_color_matrix=bt709';
 const args=['-hide_banner','-loglevel','error','-nostdin','-n','-f','image2pipe','-framerate',String(job.fps),'-i','pipe:0','-an','-c:v','libx264'];
 if(limited)args.push('-b:v',String(job.plan.bitrate),'-pass',String(pass),'-passlogfile',job.logPrefix);
 else args.push('-crf','0');
 args.push('-preset','medium','-tune','zerolatency','-refs','1','-g',String(Math.round(job.fps)),'-vf',filter,'-pix_fmt',limited?'yuv420p':'yuv444p','-colorspace','bt709','-color_primaries','bt709','-color_trc','iec61966-2-1','-color_range',limited?'tv':'pc','-threads',String(job.threads),'-progress','pipe:1','-nostats');
 if(limited&&pass===1)args.push('-f','null',process.platform==='win32'?'NUL':'/dev/null');
 else args.push('-movflags','+faststart',job.temporary);
 return args;
}
module.exports={encodingPlan,encodingArguments};
