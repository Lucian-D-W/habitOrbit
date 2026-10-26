import {cloneElement,useCallback,useEffect,useRef,useState,type HTMLAttributes,type ReactElement,type ReactNode,type Ref} from 'react';
import {DndContext,DragOverlay,KeyboardSensor,MeasuringStrategy,PointerSensor,useDroppable,useSensor,useSensors,type CollisionDetection,type DragStartEvent} from '@dnd-kit/core';
import {SortableContext,useSortable,rectSortingStrategy,sortableKeyboardCoordinates} from '@dnd-kit/sortable';
import {CSS} from '@dnd-kit/utilities';
import {FolderPlus} from 'lucide-react';
import {centerOf,isDragControl,isLoose,projectMove,resolveIntent,sameIntent,sectionOf,type CardBounds,type ContainerBounds,type DragIntent,type DragSection} from './gallery-drag';

type CardElement=ReactElement<HTMLAttributes<HTMLElement>&{ref?:Ref<HTMLElement>}>;
type Props={
 sections:DragSection[];disabled:boolean;grouping?:boolean;
 renderCard:(id:string)=>ReactElement;
 renderHeader?:(section:DragSection)=>ReactNode;
 onCommit:(id:string,intent:DragIntent)=>void;
 onDragging:(active:boolean)=>void;
 onCardMenu?:(id:string,x:number,y:number)=>void;
};

function Tile({id,disabled,children,folder,dragging,onMenu}:{id:string;disabled:boolean;children:ReactElement;folder:boolean;dragging:boolean;onMenu?:Props['onCardMenu']}){
 const {attributes,listeners,setNodeRef,setActivatorNodeRef,transform,transition,isDragging}=useSortable({id,disabled});
 const card=children as CardElement;
 return <div ref={setNodeRef} data-gallery-card={id} className={'gallery-tile'+(isDragging?' lifted-source':'')+(folder?' folder-target':'')}
  style={{transform:!isDragging?CSS.Transform.toString(transform):undefined,transition}}>
  {cloneElement(card,{
   ...attributes,ref:setActivatorNodeRef,role:'group','aria-roledescription':'可拖动卡片',
   onPointerDown:e=>{if(!isDragControl(e.target,e.currentTarget))listeners?.onPointerDown?.(e);},
   onKeyDown:e=>{
    if(isDragControl(e.target,e.currentTarget))return;
    if((e.shiftKey&&e.key==='F10')||e.key==='ContextMenu'){
     e.preventDefault();const r=e.currentTarget.getBoundingClientRect();onMenu?.(id,r.left+16,r.top+24);return;
    }
    listeners?.onKeyDown?.(e);
    if(!dragging&&!e.defaultPrevented)card.props.onKeyDown?.(e);
   },
   onContextMenu:e=>{if(onMenu&&!isDragControl(e.target,e.currentTarget)){e.preventDefault();onMenu(id,e.clientX,e.clientY);}},
  })}
  {folder&&<span className="folder-preview-mark" aria-hidden="true"><FolderPlus size={22}/></span>}
 </div>;
}
function DropSection({section,children,header,active,dragging}:{section:DragSection;children:ReactNode;header:ReactNode;active:boolean;dragging:boolean}){
 const {setNodeRef}=useDroppable({id:'container:'+section.id,data:{container:section.id}});
 return <section ref={setNodeRef} className={'gallery-drop-section'+(isLoose(section.id)?' loose-cards':' named-group')+(section.items.length?'':' empty-section')+(active?' container-target':'')+(dragging?' during-drag':'')} aria-label={isLoose(section.id)?'未分组卡片':'分组卡片'}>
  {header}{children}
 </section>;
}
/** A drag only owns a temporary projection. No RPC, model mutation, or counts
 * happen here. This same surface is used for the main gallery and favorites. */
export function GalleryBoard({sections,disabled,grouping=false,renderCard,renderHeader,onCommit,onDragging,onCardMenu}:Props){
 const [draft,setDraft]=useState<DragSection[]|null>(null),[active,setActive]=useState<string|null>(null),[intent,setIntent]=useState<DragIntent|null>(null);
 const [overlay,setOverlay]=useState<{html:string;width:number;height:number}|null>(null),[session,setSession]=useState(0);
 const root=useRef<HTMLDivElement>(null),base=useRef(sections),draftRef=useRef(sections),activeRef=useRef<string|null>(null);
 const pending=useRef<DragIntent|null>(null),current=useRef<DragIntent|null>(null),keyboard=useRef(false),cancelled=useRef(false),blockClick=useRef(0);
 const latest=useRef({sections,disabled,onDragging});latest.current={sections,disabled,onDragging};
 const sensors=useSensors(useSensor(PointerSensor,{activationConstraint:{distance:8}}),useSensor(KeyboardSensor,{coordinateGetter:sortableKeyboardCoordinates,keyboardCodes:{start:['Space'],cancel:['Escape'],end:['Space','Enter']}}));
 const clear=useCallback(()=>{
  activeRef.current=null;pending.current=null;current.current=null;setActive(null);setIntent(null);setDraft(null);setOverlay(null);latest.current.onDragging(false);blockClick.current=Date.now()+350;
 },[]);
 const cancel=useCallback(()=>{if(!activeRef.current)return;cancelled.current=true;clear();setSession(n=>n+1);},[clear]);
 useEffect(()=>{
  const blur=()=>cancel(),hidden=()=>{if(document.hidden)cancel();};
  window.addEventListener('blur',blur);window.addEventListener('resize',blur);document.addEventListener('visibilitychange',hidden);
  return()=>{window.removeEventListener('blur',blur);window.removeEventListener('resize',blur);document.removeEventListener('visibilitychange',hidden);};
 },[cancel]);
 useEffect(()=>{
  if(activeRef.current&&(disabled||!sections.some(s=>s.items.includes(activeRef.current!))||
   (current.current&&current.current.kind!=='move'&&!sections.some(s=>s.items.includes((current.current as {target:string}).target)))))cancel();
 },[sections,disabled,cancel]);
 useEffect(()=>()=>{if(activeRef.current)latest.current.onDragging(false);},[]);

 const collision=useCallback<CollisionDetection>(args=>{
  if(!activeRef.current)return [];
  const point=centerOf(args.collisionRect),bounds=root.current?.getBoundingClientRect();
  // The physical pointer leaving this board cannot commit even if the card's
  // center still overlaps it (favorites are a different, independent board).
  const pointer=args.pointerCoordinates??point;
  if(!bounds||pointer.x<bounds.left||pointer.x>bounds.right||pointer.y<bounds.top||pointer.y>bounds.bottom){pending.current=null;return [];}
  const cards:CardBounds[]=[],containers:ContainerBounds[]=[];
  for(const [id,rect] of args.droppableRects){
   const key=String(id);
   if(key.startsWith('container:'))containers.push({id:key.slice(10),rect});
   else {const container=sectionOf(draftRef.current,key);if(container)cards.push({id:key,container,rect});}
  }
  pending.current=resolveIntent({point,source:activeRef.current,sourceContainer:sectionOf(base.current,activeRef.current),sections:draftRef.current,cards,containers,grouping,previous:current.current,keyboard:keyboard.current,bounds});
  const next=pending.current;
  return next?[{id:next.kind==='move'?next.over:activeRef.current}]:[];
 },[grouping]);
 const track=()=>{
  const next=pending.current;if(sameIntent(current.current,next))return;
  current.current=next;setIntent(next);
  if(next?.kind==='move'&&activeRef.current&&sectionOf(draftRef.current,activeRef.current)!==next.container){
   const moved=projectMove(draftRef.current,activeRef.current,next.container,next.before);draftRef.current=moved;setDraft(moved);
  }else if(next&&next.kind!=='move'&&draftRef.current!==base.current){
   // Folder feedback and temporary cross-container reordering are exclusive.
   draftRef.current=base.current;setDraft(base.current);
  }
 };
 const start=(event:DragStartEvent)=>{
  const id=String(event.active.id);
  base.current=sections.map(s=>({...s,items:[...s.items]}));draftRef.current=base.current;setDraft(base.current);
  activeRef.current=id;cancelled.current=false;keyboard.current=event.activatorEvent instanceof KeyboardEvent;current.current=null;pending.current=null;setIntent(null);setActive(id);onDragging(true);
  const element=Array.from(root.current?.querySelectorAll<HTMLElement>('[data-gallery-card]')??[]).find(e=>e.dataset.galleryCard===id)?.querySelector<HTMLElement>('.orb-card');
  if(element){
   const rect=element.getBoundingClientRect(),clone=element.cloneNode(true) as HTMLElement;
   clone.removeAttribute('id');clone.removeAttribute('tabindex');clone.removeAttribute('aria-describedby');clone.classList.remove('selected');
   clone.querySelectorAll<HTMLElement>('[id],[tabindex],[aria-describedby]').forEach(e=>{e.removeAttribute('id');e.removeAttribute('tabindex');e.removeAttribute('aria-describedby');});
   setOverlay({html:clone.outerHTML,width:rect.width,height:rect.height});
  }
 };
 const end=()=>{
  // Revalidate the most recent collision; over can legitimately be the source
  // during folder feedback, because sortable must not move its neighbors then.
  const id=activeRef.current,next=pending.current;
  const exists=(target:string)=>latest.current.sections.some(s=>s.items.includes(target));
  if(id&&!cancelled.current&&!latest.current.disabled&&exists(id)&&next&&
   (next.kind==='move'?latest.current.sections.some(s=>s.id===next.container):exists(next.target))){onCommit(id,next);}
  clear();
 };
 // Rows can disappear while dragging (archive/delete from another window).
 // Filter before render; cancellation in the effect must not be relied on to
 // prevent dereferencing a removed card during this render.
 const available=new Set(sections.flatMap(s=>s.items));
 const visible=(draft??sections).map(s=>({...s,items:s.items.filter(id=>available.has(id))})),folder=intent&&intent.kind!=='move'?intent:null;
 return <div ref={root} className={'gallery-board'+(grouping?' mixed-gallery-board':'')+(active?' is-dragging':'')} onClickCapture={e=>{if(activeRef.current||Date.now()<blockClick.current){e.preventDefault();e.stopPropagation();}}} onDoubleClickCapture={e=>{if(activeRef.current||Date.now()<blockClick.current){e.preventDefault();e.stopPropagation();}}}>
  <DndContext key={session} sensors={sensors} collisionDetection={collision} measuring={{droppable:{strategy:MeasuringStrategy.Always}}} onDragStart={start} onDragMove={track} onDragOver={track} onDragEnd={end} onDragCancel={clear}
   accessibility={{screenReaderInstructions:{draggable:'空格拿起卡片，方向键移动，空格或回车放下，Escape取消。Shift F10打开分组操作。'},announcements:{onDragStart:()=> '已拿起卡片',onDragOver:()=>pending.current?.kind==='create-group'?'松手创建分组':pending.current?.kind==='add-to-group'?'松手加入分组':'正在预览落点',onDragEnd:()=> '拖动结束',onDragCancel:()=> '已取消，恢复原位置'}}}>
   {visible.map(section=><DropSection key={section.id} section={section} header={renderHeader?.(section)} active={intent?.container===section.id&&(intent.kind!=='move'||intent.over.startsWith('container:'))} dragging={!!active}>
    <SortableContext id={section.id} items={section.items} strategy={folder?()=>null:rectSortingStrategy}>
     <div className="orb-gallery">{section.items.map(id=><Tile key={id} id={id} disabled={disabled} folder={folder?.target===id} dragging={!!active} onMenu={onCardMenu}>{renderCard(id)}</Tile>)}</div>
    </SortableContext>
   </DropSection>)}
   <DragOverlay adjustScale={false} zIndex={100} dropAnimation={{duration:180,easing:'cubic-bezier(.2,.8,.2,1)'}}>
    {active&&overlay?<div className={'gallery-drag-overlay'+(folder?' grouping-preview':'')} style={{width:overlay.width,height:overlay.height}} aria-hidden="true" inert dangerouslySetInnerHTML={{__html:overlay.html}}/>:null}
   </DragOverlay>
  </DndContext>
 </div>;
}
