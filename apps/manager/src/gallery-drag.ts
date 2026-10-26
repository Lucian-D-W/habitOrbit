/** Launcher3-style exclusive intents, adapted to a wrapping web card grid.
 * Reference: AOSP Launcher3 android-11.0.0_r2, Workspace.manageFolderFeedback /
 * setDragMode / onDrop. Geometry and presentation here are specific to this app.
 */
import {topOrder,type GalleryLayout} from './gallery-layout';
export type Point = {x:number;y:number};
export type Rect = {left:number;top:number;width:number;height:number};
export type DragSection = {id:string;items:string[];beforeGroup?:string};
export type DragIntent =
 | {kind:'move';container:string;before?:string;over:string}
 | {kind:'create-group'|'add-to-group';container:string;target:string};
export type CardBounds = {id:string;container:string;rect:Rect};
export type ContainerBounds = {id:string;rect:Rect};
export const ROOT = 'ungrouped';
export function isLoose(id:string){return id===ROOT||id.startsWith(ROOT+':');}
/** Consecutive loose cards share a grid. Empty loose runs remain available on
 * either side of every group, so extracting a card does not send it upstairs. */
export function layoutSections(layout:GalleryLayout,visible:string[]):DragSection[]{
 const allowed=new Set(visible),sections:DragSection[]=[];let loose:string[]=[];
 for(const key of topOrder(layout)){
  if(key.startsWith('card:')){const id=key.slice(5);if(allowed.has(id))loose.push(id);}
  else {const id=key.slice(6),group=layout.groups.find(g=>g.id===id);if(!group)continue;
   sections.push({id:ROOT+':'+id,items:loose,beforeGroup:id},{id,items:group.activity_ids.filter(x=>allowed.has(x))});loose=[];
  }
 }
 sections.push({id:ROOT,items:loose});return sections;
}

export function contains(rect:Rect,p:Point){
 return p.x>=rect.left&&p.x<=rect.left+rect.width&&p.y>=rect.top&&p.y<=rect.top+rect.height;
}
export function centerOf(rect:Rect):Point{return {x:rect.left+rect.width/2,y:rect.top+rect.height/2};}
export function central(rect:Rect,p:Point,retaining=false){
 const c=centerOf(rect),radius=retaining?.4:.28;
 return ((p.x-c.x)/(rect.width*radius))**2+((p.y-c.y)/(rect.height*radius))**2<=1;
}
export function sectionOf(sections:DragSection[],id:string){return sections.find(s=>s.items.includes(id))?.id;}
export function sameIntent(a:DragIntent|null,b:DragIntent|null){return JSON.stringify(a)===JSON.stringify(b);}
export function projectMove(sections:DragSection[],id:string,container:string,before?:string):DragSection[]{
 if(!sections.some(s=>s.id===container)||!sections.some(s=>s.items.includes(id)))return sections;
 return sections.map(s=>{
  const items=s.items.filter(x=>x!==id);
  if(s.id===container){const index=before?items.indexOf(before):-1;items.splice(index<0?items.length:index,0,id);}
  return {...s,items};
 });
}
export function canGroup(sourceContainer:string|undefined,target:CardBounds,source:string){
 return target.id!==source&&(isLoose(target.container)||target.container!==sourceContainer);
}
/** Only the main board calls this, and only after its outer bounds have been
 * checked. Side gutters choose the nearest side of a group; bottom whitespace
 * chooses the final loose run. Other app surfaces are never drop targets. */
export function exteriorSection(point:Point,sections:DragSection[],containers:ContainerBounds[]){
 const nearest=[...containers].sort((a,b)=>{
  const distance=(c:ContainerBounds)=>Math.max(c.rect.top-point.y,0,point.y-c.rect.top-c.rect.height);
  return distance(a)-distance(b)||Math.abs(centerOf(a.rect).y-point.y)-Math.abs(centerOf(b.rect).y-point.y);
 })[0];
 if(!nearest)return undefined;
 if(isLoose(nearest.id))return nearest;
 const index=sections.findIndex(s=>s.id===nearest.id),after=point.y>=centerOf(nearest.rect).y;
 const adjacent=sections[index+(after?1:-1)];
 return adjacent&&isLoose(adjacent.id)?containers.find(c=>c.id===adjacent.id):undefined;
}
/** Rectangles are untransformed layout slots: neighbor animations cannot move
 * the hit target out from beneath the dragged card. Retain a center target in
 * a larger exit zone, and never create nested groups inside the same group. */
export function resolveIntent(input:{
 point:Point;source:string;sourceContainer:string|undefined;sections:DragSection[];
 cards:CardBounds[];containers:ContainerBounds[];grouping:boolean;
 previous:DragIntent|null;keyboard:boolean;bounds?:Rect;
}):DragIntent|null{
 const {point,source,sourceContainer,sections,cards,containers,grouping,previous,keyboard}=input;
 if(grouping&&!keyboard&&previous&&previous.kind!=='move'){
  const locked=cards.find(c=>c.id===previous.target);
  if(locked&&canGroup(sourceContainer,locked,source)&&central(locked.rect,point,true))return previous;
 }
 const container=containers.filter(c=>contains(c.rect,point)).sort((a,b)=>a.rect.width*a.rect.height-b.rect.width*b.rect.height)[0]??
  (grouping&&input.bounds&&contains(input.bounds,point)?exteriorSection(point,sections,containers):undefined);
 if(!container)return null;
 const list=sections.find(s=>s.id===container.id)?.items??[];
 const candidates=cards.filter(c=>c.container===container.id&&c.id!==source);
 const hit=candidates.find(c=>contains(c.rect,point));
 if(grouping&&!keyboard&&hit&&canGroup(sourceContainer,hit,source)&&central(hit.rect,point)){
  return {kind:isLoose(hit.container)?'create-group':'add-to-group',container:hit.container,target:hit.id};
 }
 // Keep the current slot when the pointer comes back over the source gap.
 const own=cards.find(c=>c.id===source&&c.container===container.id);
 if(own&&contains(own.rect,point)){
  return {kind:'move',container:container.id,before:list[list.indexOf(source)+1],over:source};
 }
 if(!candidates.length)return {kind:'move',container:container.id,over:'container:'+container.id};
 // Prefer the row beneath the visual center, then its closest horizontal slot.
 const distance=(c:CardBounds)=>{
  const dy=Math.max(c.rect.top-point.y,0,point.y-(c.rect.top+c.rect.height));
  return dy*10000+Math.abs(centerOf(c.rect).x-point.x);
 };
 const target=hit??[...candidates].sort((a,b)=>distance(a)-distance(b))[0];
 const after=point.y>target.rect.top+target.rect.height ||
  (point.y>=target.rect.top&&point.x>centerOf(target.rect).x);
 const others=list.filter(id=>id!==source),index=others.indexOf(target.id)+(after?1:0);
 const before=others[index];
 // Sortable moves into over's original slot. Use the desired insertion index,
 // not blindly the item touched, so before/after and cross-row moves agree.
 const over=list.includes(source)?list[Math.min(index,list.length-1)]:target.id;
 return {kind:'move',container:container.id,before,over};
}

/** Only interactive descendants block lifting. The drag root itself may have
 * role=button supplied by dnd-kit; closest() must not exclude that root. */
export function isDragControl(target:EventTarget|null,root:HTMLElement){
 const element=target as Element|null;
 if(!element||typeof element.closest!=='function')return false;
 const control=element.closest('button,input,select,textarea,a,[role="button"],[contenteditable="true"],[data-no-drag]');
 return !!control&&control!==root&&root.contains(control);
}
