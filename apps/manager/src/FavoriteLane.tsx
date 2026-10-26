import {GalleryBoard} from './GalleryBoard';
import {ROOT} from './gallery-drag';
import type {RenderGalleryCard} from './Gallery';
import type {Row} from './model';

export function FavoriteLane({ids,rows,disabled,onChange,onDragging,renderCard}:{ids:string[];rows:Row[];disabled:boolean;onChange:(ids:string[])=>void;onDragging:(active:boolean)=>void;renderCard:RenderGalleryCard}){
 const byId=new Map(rows.map(r=>[r.activity.id,r])),visible=ids.filter(id=>byId.has(id));
 return <section className={'favorite-lane'+(visible.length?'':' is-empty')} aria-label="激活栏">
  {visible.length>0&&<GalleryBoard sections={[{id:ROOT,items:visible}]} disabled={disabled} renderCard={id=>renderCard(byId.get(id)!,'favorite')} onDragging={onDragging}
   onCommit={(id,intent)=>{
    if(intent.kind!=='move'||!ids.includes(id))return;
    const next=ids.filter(x=>x!==id),index=intent.before?next.indexOf(intent.before):-1;next.splice(index<0?next.length:index,0,id);
    if(next.some((item,i)=>item!==ids[i]))onChange(next);
   }}/>} 
 </section>;
}
