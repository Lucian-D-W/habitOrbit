export type GalleryGroup={id:string;name:string;activity_ids:string[]};
export type GalleryLayout={revision:string;ungrouped:string[];groups:GalleryGroup[];favorites:string[];top_order?:string[]|null};
export type Destination={group:string|null;before?:string;topBefore?:string|null};
export const cardKey=(id:string)=>'card:'+id;
export const groupKey=(id:string)=>'group:'+id;
export function topOrder(layout:GalleryLayout){
 const expected=[...layout.ungrouped.map(cardKey),...layout.groups.map(g=>groupKey(g.id))],allowed=new Set(expected);
 return [...new Set([...(layout.top_order??[]),...expected])].filter(key=>allowed.has(key));
}
export function location(layout:GalleryLayout,id:string){return layout.groups.find(g=>g.activity_ids.includes(id))?.id??null;}
export function moveCard(layout:GalleryLayout,id:string,to:Destination):GalleryLayout{
 if(!layout.ungrouped.includes(id)&&!layout.groups.some(g=>g.activity_ids.includes(id)))return layout;
 const previousGroup=location(layout,id),order=topOrder(layout),next=structuredClone(layout);
 next.ungrouped=next.ungrouped.filter(x=>x!==id);for(const g of next.groups)g.activity_ids=g.activity_ids.filter(x=>x!==id);
 const list=to.group===null?next.ungrouped:next.groups.find(g=>g.id===to.group)?.activity_ids;
 if(!list)return layout;
 const index=to.before?list.indexOf(to.before):-1;list.splice(index<0?list.length:index,0,id);
 next.top_order=order.filter(key=>key!==cardKey(id));
 if(to.group===null){
  // Menu extraction stays beside its group; pointer drops supply an exact
  // top-level anchor. Explicit null means the end of the board.
  const anchor=to.topBefore!==undefined?to.topBefore:to.before?cardKey(to.before):previousGroup?order[order.indexOf(groupKey(previousGroup))+1]:undefined;
  const at=anchor?next.top_order.indexOf(anchor):-1;
  next.top_order.splice(at<0?next.top_order.length:at,0,cardKey(id));
  next.ungrouped=next.top_order.filter(key=>key.startsWith('card:')).map(key=>key.slice(5));
 }
 return next;
}
export function mergeCards(layout:GalleryLayout,id:string,target:string,newId:string):GalleryLayout{
 if(id===target||![...layout.ungrouped,...layout.groups.flatMap(g=>g.activity_ids)].includes(id)||![...layout.ungrouped,...layout.groups.flatMap(g=>g.activity_ids)].includes(target))return layout;const group=location(layout,target);
 if(group&&location(layout,id)===group)return layout;
 if(group)return moveCard(layout,id,{group});
 const next=structuredClone(layout);
 next.ungrouped=next.ungrouped.filter(x=>x!==id&&x!==target);for(const g of next.groups)g.activity_ids=g.activity_ids.filter(x=>x!==id);
 next.groups.push({id:newId,name:'新分组',activity_ids:[target,id]});
 next.top_order=topOrder(layout).filter(key=>key!==cardKey(id)).map(key=>key===cardKey(target)?groupKey(newId):key);
 return next;
}
export function dissolveGroup(layout:GalleryLayout,id:string):GalleryLayout{
 const group=layout.groups.find(g=>g.id===id);if(!group)return layout;
 const order=topOrder(layout).flatMap(key=>key===groupKey(id)?group.activity_ids.map(cardKey):[key]);
 return {...layout,ungrouped:order.filter(key=>key.startsWith('card:')).map(key=>key.slice(5)),groups:layout.groups.filter(g=>g.id!==id),top_order:order};
}
export function dropZone(rect:{left:number;top:number;width:number;height:number},point:{x:number;y:number}){
 const x=(point.x-rect.left)/rect.width,y=(point.y-rect.top)/rect.height;
 return x>.25&&x<.75&&y>.22&&y<.78?'center':x<.5?'before':'after';
}
