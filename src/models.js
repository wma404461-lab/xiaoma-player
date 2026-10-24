import * as THREE from 'three';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {DRACOLoader} from 'three/addons/loaders/DRACOLoader.js';
import {KTX2Loader} from 'three/addons/loaders/KTX2Loader.js';
import {MeshoptDecoder} from 'three/addons/libs/meshopt_decoder.module.js';
import {OBJLoader} from 'three/addons/loaders/OBJLoader.js';
import {MTLLoader} from 'three/addons/loaders/MTLLoader.js';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
import {STLLoader} from 'three/addons/loaders/STLLoader.js';
import {PLYLoader} from 'three/addons/loaders/PLYLoader.js';
import {ThreeMFLoader} from 'three/addons/loaders/3MFLoader.js';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';

export class ModelView {
  constructor(container){
    this.container=container;this.scene=new THREE.Scene();this.camera=new THREE.PerspectiveCamera(42,1,.001,1000);this.renderer=new THREE.WebGLRenderer({antialias:true,alpha:true});this.renderer.setPixelRatio(Math.min(devicePixelRatio,1.25));this.renderer.outputColorSpace=THREE.SRGBColorSpace;this.renderer.toneMapping=THREE.ACESFilmicToneMapping;container.append(this.renderer.domElement);this.controls=new OrbitControls(this.camera,this.renderer.domElement);this.controls.enableDamping=true;this.controls.screenSpacePanning=false;this.controls.enablePan=false;this.disposed=false;this.playing=false;this.last=performance.now();this.lastRender=0;
    this.renderer.toneMappingExposure=.85;this.scene.add(new THREE.HemisphereLight(0xe4ecff,0x747b85,1));const key=new THREE.DirectionalLight(0xffffff,2);key.position.set(3,5,4);this.scene.add(key);const fill=new THREE.DirectionalLight(0xc0dcff,.6);fill.position.set(-4,2,-3);this.scene.add(fill);
    this.scene.environmentIntensity=.75;
    this.manager=new THREE.LoadingManager();this.manager.setURLModifier(url=>url.replace(/\\/g,'/'));this.manager.onLoad=()=>this.requestRender();
    this.draco=new DRACOLoader(this.manager).setDecoderPath(new URL('./decoders/draco/',import.meta.url).href);
    this.ktx=new KTX2Loader(this.manager).setTranscoderPath(new URL('./decoders/basis/',import.meta.url).href).detectSupport(this.renderer);
    this.controls.addEventListener('start',()=>{this.autoFit=false;this.interacting=true;this.requestRender();});this.controls.addEventListener('end',()=>{this.interacting=false;this.requestRender();});this.controls.addEventListener('change',()=>this.requestRender());this.resizeObserver=new ResizeObserver(()=>this.resize());this.resizeObserver.observe(container);this.resize();this.requestRender();
  }
  async load(item){
    let object,clips=[];const base=item.url.slice(0,item.url.lastIndexOf('/')+1);
    if(['.glb','.gltf'].includes(item.extension)){const loader=new GLTFLoader(this.manager).setDRACOLoader(this.draco).setKTX2Loader(this.ktx).setMeshoptDecoder(MeshoptDecoder);const result=await loader.loadAsync(item.url);object=result.scene;clips=result.animations;}
    else if(item.extension==='.fbx'){object=await new FBXLoader(this.manager).loadAsync(item.url);clips=object.animations||[];}
    else if(item.extension==='.obj'){
      const response=await fetch(item.url);if(!response.ok)throw new Error('模型读取失败');const text=await response.text();let loader=new OBJLoader(this.manager);const library=text.match(/^mtllib\s+(.+)$/m)?.[1]?.trim();
      if(library){try{const mtlResponse=await fetch(new URL(library.replace(/\\/g,'/'),base));if(mtlResponse.ok){const materials=new MTLLoader(this.manager).parse(await mtlResponse.text(),base);materials.preload();loader.setMaterials(materials);}}catch{}}
      object=loader.parse(text);
    }else if(item.extension==='.3mf')object=await new ThreeMFLoader(this.manager).loadAsync(item.url);
    else{
      const loader=item.extension==='.stl'?new STLLoader(this.manager):new PLYLoader(this.manager);const geometry=await loader.loadAsync(item.url);if(!geometry.attributes.position?.count)throw new Error('模型内没有顶点');
      const colors=!!geometry.attributes.color;
      if(item.extension==='.ply'&&!geometry.index&&!geometry.attributes.normal)object=new THREE.Points(geometry,new THREE.PointsMaterial({color:colors?0xffffff:0xb3c6da,vertexColors:colors,size:.02,sizeAttenuation:true}));
      else{if(!geometry.attributes.normal)geometry.computeVertexNormals();object=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:colors?0xffffff:0xaabccf,vertexColors:colors,metalness:.05,roughness:.5,side:THREE.DoubleSide}));}
    }
    if(this.disposed){this.disposeObject(object);return null;}
    this.object=object;const box=new THREE.Box3().setFromObject(object);if(box.isEmpty())throw new Error('模型内没有可显示的几何体');const center=box.getCenter(new THREE.Vector3()),size=box.getSize(new THREE.Vector3());object.position.sub(center);this.radius=Math.hypot(size.x,size.y,size.z)/2||1;this.scene.add(object);
    let requiresEnvironment=false;object.traverse(node=>{for(const material of node.material?(Array.isArray(node.material)?node.material:[node.material]):[])if(material.isMeshStandardMaterial&&(material.metalness>.1||material.map))requiresEnvironment=true;});
    if(requiresEnvironment){const pmrem=new THREE.PMREMGenerator(this.renderer);const room=new RoomEnvironment();this.environment=pmrem.fromScene(room,.04);this.scene.environment=this.environment.texture;room.dispose();pmrem.dispose();}
    this.grid=new THREE.GridHelper(this.radius*4,20,0x526074,0x343d49);this.grid.position.y=-size.y/2-.001;this.grid.material.transparent=true;this.grid.material.opacity=.28;this.scene.add(this.grid);
    let vertices=0,triangles=0;object.traverse(node=>{if(node.geometry?.attributes.position){vertices+=node.geometry.attributes.position.count;triangles+=Math.floor((node.geometry.index?.count||node.geometry.attributes.position.count)/3);if(node.isPoints)node.material.size=this.radius*.006;}if(node.material){for(const material of Array.isArray(node.material)?node.material:[node.material])if(material?.isMeshBasicMaterial&&!material.map)material.color.set(0xacbdce);}});
    if(vertices>150000)this.renderer.setPixelRatio(1);this.clips=clips;if(clips.length){this.mixer=new THREE.AnimationMixer(object);this.mixer.addEventListener('finished',()=>{this.playing=false;this.onEnded?.();});this.selectAnimation(0);}this.fit();return {vertices,triangles,animations:clips.map((clip,i)=>({name:clip.name||'动画 '+(i+1),duration:clip.duration}))};
  }
  selectAnimation(index){if(!this.mixer)return;this.mixer.stopAllAction();this.action=this.mixer.clipAction(this.clips[index]);this.action.play();this.mixer.setTime(0);this.activeClip=index;this.requestRender();}
  seek(time){if(this.action){this.action.paused=false;this.action.enabled=true;this.action.play();}this.mixer?.setTime(time);this.requestRender();}
  setPlaying(value){if(value&&this.action){if(this.action.time>=this.clips[this.activeClip].duration){this.action.reset().play();this.mixer.setTime(0);}this.action.paused=false;this.action.enabled=true;}this.playing=value;this.last=performance.now();this.requestRender();}
  setLoop(value){this.loop=value;if(this.action){this.action.setLoop(value?THREE.LoopRepeat:THREE.LoopOnce,value?Infinity:1);this.action.clampWhenFinished=true;}}
  fit(){this.autoFit=true;const vertical=THREE.MathUtils.degToRad(this.camera.fov),fov=2*Math.atan(Math.tan(vertical/2)*Math.min(1,this.camera.aspect)),distance=this.radius/Math.sin(fov/2)*1.12;this.camera.position.set(.68,.4,.75).normalize().multiplyScalar(distance);this.camera.near=Math.max(.00001,this.radius/1000);this.camera.far=this.radius*1000;this.camera.updateProjectionMatrix();this.controls.target.set(0,0,0);this.controls.minDistance=this.radius*.04;this.controls.maxDistance=this.radius*40;this.controls.update();this.requestRender();}
  wireframe(value){this.object?.traverse(node=>{if(node.material)for(const material of Array.isArray(node.material)?node.material:[node.material])if('wireframe' in material)material.wireframe=value;});this.requestRender();}
  resize(){const {clientWidth:w,clientHeight:h}=this.container;if(!w||!h)return;this.camera.aspect=w/h;this.camera.updateProjectionMatrix();this.renderer.setSize(w,h);if(this.radius&&this.autoFit)this.fit();this.requestRender();}
  requestRender(){if(this.disposed)return;this.needsRender=true;if(!this.animation&&!this.inFrame)this.animation=requestAnimationFrame(now=>this.animate(now));}
  animate(now){this.animation=null;if(this.disposed)return;if(this.playing&&!this.interacting&&now-this.lastRender<32){this.animation=requestAnimationFrame(time=>this.animate(time));return;}this.inFrame=true;const dt=Math.min(.1,(now-this.last)/1000);this.last=now;if(this.playing)this.mixer?.update(dt);const changed=this.controls.update(dt);if(changed||this.needsRender||this.playing){this.renderer.render(this.scene,this.camera);this.lastRender=now;if(this.onFrame&&now-(this.lastUi||0)>100){this.lastUi=now;this.onFrame();}}this.needsRender=false;this.inFrame=false;if(this.playing||changed)this.requestRender();}
  disposeObject(object){const geometries=new Set(),materials=new Set(),textures=new Set();object?.traverse(node=>{if(node.geometry)geometries.add(node.geometry);if(node.material)for(const material of Array.isArray(node.material)?node.material:[node.material]){materials.add(material);for(const value of Object.values(material))if(value?.isTexture)textures.add(value);}});geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());textures.forEach(t=>{t.source?.data?.close?.();t.dispose();});}
  dispose(){this.disposed=true;cancelAnimationFrame(this.animation);this.resizeObserver.disconnect();this.controls.dispose();this.disposeObject(this.object);this.grid?.geometry.dispose();this.grid?.material.dispose();this.environment?.dispose();this.draco.dispose();this.ktx.dispose();this.renderer.dispose();this.renderer.domElement.remove();}
}
