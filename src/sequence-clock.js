// Playback follows elapsed time. Decoding a late frame must not slow the timeline.
export class SequenceClock {
  reset(now,frame){this.started=now;this.first=frame;this.cycle=0;}
  read(now,fps,start,end,advance=true){
    const length=end-start+1,offset=Math.max(0,this.first-start)+Math.max(0,Math.floor((now-this.started)*fps/1000));
    const cycle=Math.floor(offset/length),boundary=cycle>this.cycle;if(advance)this.cycle=cycle;
    return {frame:start+offset%length,boundary,ended:offset>=length};
  }
}
