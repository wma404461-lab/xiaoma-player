import * as pdfjs from 'pdfjs-dist';
pdfjs.GlobalWorkerOptions.workerSrc=new URL('./pdf/pdf.worker.mjs',import.meta.url).href;
let sharedWorker;
export class DocumentView {
  constructor(container,onChange){this.container=container;this.onChange=onChange;this.page=1;this.zoom=null;this.ticket=0;this.controllers=new Set();this.canvas=document.createElement('canvas');this.canvas.className='pdf-page';this.host=document.createElement('div');this.host.className='pdf-page-host';this.host.append(this.canvas);container.replaceChildren(this.host);this.observer=new ResizeObserver(()=>{clearTimeout(this.resizeTimer);this.resizeTimer=setTimeout(()=>{if(this.pdf)this.render();},120);});this.observer.observe(container);}
  async open(item){
    let url=item.url;
    if(item.extension!=='.pdf'){
      try{url=(await window.viewer.documentPreview(item.id)).url;}
      catch(error){if(item.extension==='.docx'){await this.docx(item);return;}if(item.extension==='.pptx'){await this.pptx(item);return;}throw error;}
    }
    const initial=await fetch(url,{headers:{Range:'bytes=0-65535'}});if(!initial.ok)throw new Error('Document cannot be read');
    const first=new Uint8Array(await initial.arrayBuffer()),total=Number(initial.headers.get('content-range')?.match(/\/(\d+)$/)?.[1]||initial.headers.get('content-length')||first.length);
    sharedWorker??=new pdfjs.PDFWorker({name:'xiaoma-pdf'});
    const options={worker:sharedWorker,isEvalSupported:false,useSystemFonts:true,cMapUrl:new URL('./pdf/cmaps/',import.meta.url).href,cMapPacked:true,standardFontDataUrl:new URL('./pdf/standard_fonts/',import.meta.url).href,wasmUrl:new URL('./pdf/wasm/',import.meta.url).href,useWorkerFetch:false,disableAutoFetch:true};
    if(total>first.length){const transport=new pdfjs.PDFDataRangeTransport(total,first);transport.requestDataRange=(begin,end)=>{const controller=new AbortController();this.controllers.add(controller);fetch(url,{headers:{Range:`bytes=${begin}-${end-1}`},signal:controller.signal}).then(r=>r.arrayBuffer()).then(buffer=>transport.onDataRange(begin,new Uint8Array(buffer))).catch(error=>{if(error.name!=='AbortError')this.loadingTask?.destroy();}).finally(()=>this.controllers.delete(controller));};transport.abort=()=>this.controllers.forEach(controller=>controller.abort());this.loadingTask=pdfjs.getDocument({...options,range:transport,length:total,rangeChunkSize:65536});}
    else this.loadingTask=pdfjs.getDocument({...options,data:first});
    this.loadingTask.onPassword=()=>{this.loadingTask.destroy();this.passwordError=new Error('This PDF Password required, please unlock in original software first');};
    try{this.pdf=await this.loadingTask.promise;}catch(error){throw this.passwordError||error;}
    if(this.disposed){await this.loadingTask.destroy();return;}this.pages=this.pdf.numPages;this.mode='pdf';await this.render();
  }
  async render(){if(!this.pdf||this.disposed)return;const ticket=++this.ticket;this.renderTask?.cancel();const page=await this.pdf.getPage(this.page);if(ticket!==this.ticket)return;const natural=page.getViewport({scale:1});this.pdfPageWidth=natural.width;const scale=this.zoom??Math.min((this.container.clientWidth-36)/natural.width,(this.container.clientHeight-30)/natural.height),viewport=page.getViewport({scale:Math.max(.15,scale)}),dpr=Math.min(devicePixelRatio,1.5),pixels=viewport.width*viewport.height*dpr*dpr,effective=pixels>8000000?dpr*Math.sqrt(8000000/pixels):dpr;this.canvas.width=Math.ceil(viewport.width*effective);this.canvas.height=Math.ceil(viewport.height*effective);this.canvas.style.width=viewport.width+'px';this.canvas.style.height=viewport.height+'px';this.renderTask=page.render({canvas:this.canvas,viewport,transform:effective===1?null:[effective,0,0,effective,0,0],background:'rgb(255,255,255)'});try{await this.renderTask.promise;}catch(error){if(error.name!=='RenderingCancelledException')throw error;}if(ticket===this.ticket)this.onChange?.({page:this.page,pages:this.pages,mode:this.mode});}
  async go(page){if(this.pdf){this.page=Math.max(1,Math.min(this.pages,page));await this.render();}else if(this.mode==='pptx'){this.page=Math.max(1,Math.min(this.pages,page));this.renderSlide();}else if(this.mode==='docx'){this.page=Math.max(1,Math.min(this.pages,page));this.docPages[this.page-1]?.scrollIntoView({block:'start',behavior:'instant'});this.onChange?.({page:this.page,pages:this.pages,mode:this.mode});}}
  fit(){this.zoom=null;if(this.pdf)this.render();else if(this.mode==='pptx')this.renderSlide();}
  zoomBy(factor){if(this.pdf){const current=this.zoom??this.canvas.clientWidth/(this.pdfPageWidth||612);this.zoom=Math.min(4,Math.max(.25,current*factor));this.render();}}
  async docx(item){const {renderAsync}=await import('docx-preview'),response=await fetch(item.url);this.mode='docx';this.container.replaceChildren();await renderAsync(await response.arrayBuffer(),this.container,this.container,{className:'xm-docx',inWrapper:true,renderAltChunks:false,useBase64URL:true,ignoreLastRenderedPageBreak:false});this.docPages=Array.from(this.container.querySelectorAll('section.xm-docx'));this.pages=Math.max(1,this.docPages.length);this.onChange?.({page:1,pages:this.pages,mode:this.mode});}
  async pptx(item){const {PptxPreview}=await import('./pptx-preview.js');this.slidePreview=new PptxPreview(this.container);await this.slidePreview.open(await (await fetch(item.url)).arrayBuffer());this.mode='pptx';this.pages=this.slidePreview.pages;this.renderSlide();}
  renderSlide(){this.slidePreview.render(this.page-1);this.onChange?.({page:this.page,pages:this.pages,mode:this.mode});}
  dispose(){this.disposed=true;this.ticket++;this.renderTask?.cancel();this.observer.disconnect();clearTimeout(this.resizeTimer);this.controllers.forEach(c=>c.abort());this.loadingTask?.destroy();this.slidePreview?.dispose();this.container.replaceChildren();}
}
