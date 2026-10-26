import {useEffect,useState} from 'react';
import {rpc} from './api';import {Icon} from './icons';import type {Activity,Thumbnail as Image} from './model';
const images=new Map<string,Image>();
export function Thumbnail({activity,small=false}:{activity:Activity;small?:boolean}){
 const key=activity.visual_key+':'+activity.config.visual.decorative_seed+':'+activity.config.visual.renderer_version;
 const [image,setImage]=useState(images.get(key));const [error,setError]=useState('');const [retry,setRetry]=useState(0);
 useEffect(()=>{let active=true;setImage(images.get(key));setError('');
  rpc<Image>('thumbnail.get',{activityId:activity.id}).then(value=>{images.set(key,value);if(active)setImage(value);}).catch(e=>{if(active)setError(String(e.message??e));});
  return()=>{active=false;};},[activity.id,key,retry]);
 return <div className={'orb-avatar'+(small?' small':'')} style={{'--orb-accent':activity.config.visual.accent_color} as React.CSSProperties}>
  {image?<img src={'data:image/png;base64,'+image.png} alt="" draggable={false}/>:<div className="orb-placeholder"><Icon id={activity.config.icon_id} size={small?20:38}/></div>}
  {image&&<span className="icon-badge"><Icon id={activity.config.icon_id} size={small?14:22}/></span>}
  {error&&!small&&<button className="thumb-retry" title={error} onDoubleClick={e=>e.stopPropagation()} onKeyDown={e=>e.stopPropagation()} onClick={e=>{e.stopPropagation();setRetry(n=>n+1);}}>重试头像</button>}
 </div>;
}
