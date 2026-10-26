import {useEffect,useRef,useState} from 'react';
import {rpc} from './api';import type {Visual} from './model';
export function LivePreview({visual}:{visual:Visual}){
 const target=useRef<HTMLDivElement>(null),latest=useRef({visual,energy:.35}),version=useRef(0),session=useRef(crypto.randomUUID());
 const [energy,setEnergy]=useState(.35),[png,setPng]=useState(''),[error,setError]=useState(''),[retry,setRetry]=useState(0);
 useEffect(()=>{latest.current={visual,energy};version.current++;},[visual,energy]);
 useEffect(()=>{let active=true,visible=false,timer:ReturnType<typeof setTimeout>|undefined,busy=false;const start=performance.now();
  const release=()=>{void rpc('preview.release',{session:session.current}).catch(()=>{});};
  const frame=async()=>{if(!active||!visible||document.hidden||busy)return;busy=true;const token=version.current,params=latest.current;
   try{const result=await rpc<{png:string}>('preview.render',{...params,session:session.current,time:(performance.now()-start)/1000});if(active&&visible&&token===version.current){setPng(result.png);setError('');}}
   catch(e){if(active){setError((e as Error).message);visible=false;release();}}
   finally{busy=false;if(active&&visible&&!document.hidden)timer=setTimeout(()=>void frame(),125);else release();}
  };
  const update=()=>{if(timer)clearTimeout(timer);if(visible&&!document.hidden)void frame();else release();};
  const observer=new IntersectionObserver(([entry])=>{visible=entry.isIntersecting;update();});if(target.current)observer.observe(target.current);
  document.addEventListener('visibilitychange',update);
  return()=>{active=false;if(timer)clearTimeout(timer);observer.disconnect();document.removeEventListener('visibilitychange',update);release();};
 },[retry]);
 return <div ref={target} className="live-preview"><div className="preview-orb">{png&&<img src={'data:image/png;base64,'+png} alt="共享桌面渲染器的动态星球预览"/>}</div><label>成长状态 · {Math.round(energy*100)}%<input type="range" min={0} max={100} value={energy*100} onChange={e=>setEnergy(Number(e.target.value)/100)}/></label><p className="hint">尺寸与持续光效一起变化，仅用于模拟，不保存记录。</p>{error&&<p role="alert">{error}<button type="button" onClick={()=>setRetry(v=>v+1)}>重试预览</button></p>}</div>;
}
