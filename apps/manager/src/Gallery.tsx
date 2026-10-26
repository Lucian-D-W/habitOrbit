import {useEffect,useRef,useState,type ReactElement} from 'react';
import {Check,Ellipsis,X} from 'lucide-react';
import {rpc} from './api';
import {FavoriteLane} from './FavoriteLane';
import {GalleryBoard} from './GalleryBoard';
import {isLoose,layoutSections,type DragIntent} from './gallery-drag';
import {type Row} from './model';
import {cardKey,groupKey,dissolveGroup,location,mergeCards,moveCard,type GalleryLayout} from './gallery-layout';

export type ActivationControl={active:boolean;disabled:boolean;toggle:()=>void};
export type RenderGalleryCard=(row:Row,copy?:string,activation?:ActivationControl)=>ReactElement;

export function Gallery({rows,revision,disabled,renderCard,onError}:{rows:Row[];revision:string;disabled:boolean;renderCard:RenderGalleryCard;onError:(message:string)=>void}){
 const [layout,setLayout]=useState<GalleryLayout|null>(null),[saving,setSaving]=useState(false),[dragging,setDragging]=useState<'main'|'favorite'|null>(null);
 const [rename,setRename]=useState<string|null>(null),[name,setName]=useState('');
 const [menu,setMenu]=useState<{id:string;x:number;y:number}|null>(null),menuRef=useRef<HTMLDivElement>(null);
 const inFlight=useRef(false),requestVersion=useRef(0),mounted=useRef(true);
 const byId=new Map(rows.map(r=>[r.activity.id,r]));
 useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;requestVersion.current++;};},[]);
 useEffect(()=>{
  if(dragging||saving)return;
  const version=++requestVersion.current;
  rpc<GalleryLayout>('gallery.get').then(value=>{if(mounted.current&&version===requestVersion.current&&!inFlight.current)setLayout(value);}).catch(e=>{if(mounted.current&&version===requestVersion.current)onError(e.message);});
  return()=>{if(version===requestVersion.current)requestVersion.current++;};
 },[revision,dragging,saving]);
 useEffect(()=>{
  if(!menu)return;
  menuRef.current?.querySelector<HTMLButtonElement>('button')?.focus();
  const close=(event:PointerEvent)=>{if(!menuRef.current?.contains(event.target as Node))setMenu(null);};
  const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){event.stopPropagation();setMenu(null);}};
  document.addEventListener('pointerdown',close);document.addEventListener('keydown',escape);
  return()=>{document.removeEventListener('pointerdown',close);document.removeEventListener('keydown',escape);};
 },[menu]);
 useEffect(()=>{if(menu&&!byId.has(menu.id))setMenu(null);},[rows,menu]);
 const save=async(next:GalleryLayout):Promise<boolean>=>{
  if(inFlight.current||disabled)return false;
  inFlight.current=true;requestVersion.current++;setSaving(true);setMenu(null);setLayout(next);
  try{const saved=await rpc<GalleryLayout>('gallery.save',{layout:next});if(mounted.current)setLayout(saved);return true;}
  catch(e){onError((e as Error).message);try{const actual=await rpc<GalleryLayout>('gallery.get');if(mounted.current)setLayout(actual);}catch{if(mounted.current)setLayout(null);}return false;}
  finally{inFlight.current=false;if(mounted.current)setSaving(false);}
 };
 const combine=async(id:string,target:string)=>{
  if(!layout||!byId.has(id)||!byId.has(target)||id===target)return;
  const group=location(layout,target),newId=crypto.randomUUID(),next=mergeCards(layout,id,target,newId);
  if(next===layout)return;
  if(await save(next)&&!group&&mounted.current){setRename(newId);setName('新分组');}
 };
 const commit=(id:string,intent:DragIntent)=>{
  if(!layout||!byId.has(id))return;
  if(intent.kind!=='move'){void combine(id,intent.target);return;}
  const section=layoutSections(layout,rows.map(r=>r.activity.id)).find(s=>s.id===intent.container);if(!section)return;
  const loose=isLoose(section.id),topBefore=intent.before?cardKey(intent.before):section.beforeGroup?groupKey(section.beforeGroup):null;
  const next=moveCard(layout,id,{group:loose?null:intent.container,before:intent.before,...(loose?{topBefore}:{})});
  if(JSON.stringify(next)!==JSON.stringify(layout))void save(next);
 };
 const beginRename=(id:string)=>{const group=layout?.groups.find(g=>g.id===id);if(group){setRename(id);setName(group.name);}};
 const activation=(id:string):ActivationControl=>({active:!!layout?.favorites.includes(id),disabled:disabled||saving||!!dragging,toggle:()=>{
  if(layout)void save({...layout,favorites:layout.favorites.includes(id)?layout.favorites.filter(x=>x!==id):[...layout.favorites,id]});
 }});
 if(!layout)return <p role="status">正在载入卡片布局…</p>;
 const busy=disabled||saving,sections=layoutSections(layout,rows.map(r=>r.activity.id));
 const selectedGroup=menu?location(layout,menu.id):null;
 return <section className="organized-gallery" aria-busy={saving}>
  <FavoriteLane ids={layout.favorites} rows={rows} disabled={busy||dragging==='main'||!!rename} onChange={favorites=>void save({...layout,favorites})} onDragging={active=>setDragging(active?'favorite':null)} renderCard={(row,copy)=>renderCard(row,copy,activation(row.activity.id))}/>
  <GalleryBoard sections={sections} disabled={busy||dragging==='favorite'||!!rename} grouping renderCard={id=>renderCard(byId.get(id)!,'main',activation(id))} onCommit={commit}
   onDragging={active=>{setDragging(active?'main':null);if(active)setMenu(null);}}
   onCardMenu={(id,x,y)=>{if(!busy&&!dragging)setMenu({id,x:Math.max(8,Math.min(x,window.innerWidth-280)),y:Math.max(8,Math.min(y,window.innerHeight-360))});}}
   renderHeader={section=>{
    const group=layout.groups.find(g=>g.id===section.id);if(!group)return null;
    return <header className="group-heading">{rename===group.id?<form className="group-name-editor" onSubmit={e=>{e.preventDefault();if(name.trim())void save({...layout,groups:layout.groups.map(g=>g.id===group.id?{...g,name:name.trim()}:g)}).then(ok=>{if(ok)setRename(null);});}}>
     <input autoFocus aria-label="分组名称" maxLength={64} value={name} disabled={saving} onChange={e=>setName(e.target.value)} onKeyDown={e=>{if(e.key==='Escape'){e.preventDefault();setRename(null);}}}/>
     <button type="submit" aria-label="保存分组名称" title="保存名称" disabled={busy||!name.trim()}><Check size={16}/></button>
     <button type="button" aria-label="取消修改名称" title="取消" disabled={saving} onClick={()=>setRename(null)}><X size={16}/></button>
    </form>:<><h2><button className="group-name" disabled={busy||!!dragging} title="修改分组名称" onClick={()=>beginRename(group.id)}>{group.name}</button></h2>
     <details className="group-actions"><summary aria-label={`${group.name}的操作`} title="分组操作"><Ellipsis size={18}/></summary><div>
      <button disabled={busy||!!dragging} onClick={e=>{e.currentTarget.closest('details')?.removeAttribute('open');beginRename(group.id);}}>改名</button>
      <button disabled={busy||!!dragging} onClick={()=>void save(dissolveGroup(layout,group.id))}>解散分组</button>
     </div></details></>}</header>;
   }}/>
  {menu&&byId.has(menu.id)&&<div ref={menuRef} className="gallery-card-menu" role="dialog" aria-label={`${byId.get(menu.id)!.activity.config.name}的分组操作`} style={{left:menu.x,top:menu.y}}>
   {selectedGroup&&<button onClick={()=>void save(moveCard(layout,menu.id,{group:null}))}>移出分组</button>}
   {layout.groups.filter(g=>g.id!==selectedGroup).map(g=><button key={g.id} onClick={()=>void save(moveCard(layout,menu.id,{group:g.id}))}>移入「{g.name}」</button>)}
   {layout.ungrouped.filter(id=>id!==menu.id&&byId.has(id)).map(id=><button key={id} onClick={()=>void combine(menu.id,id)}>与「{byId.get(id)!.activity.config.name}」成组</button>)}
   <button onClick={()=>setMenu(null)}>关闭</button>
  </div>}
 </section>;
}
