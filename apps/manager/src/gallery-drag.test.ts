import {describe,expect,it} from 'vitest';
import {ROOT,central,isDragControl,projectMove,resolveIntent,type CardBounds,type DragIntent,type DragSection} from './gallery-drag';
import {mergeCards,moveCard,type GalleryLayout} from './gallery-layout';

// Pure geometry/layout assertions. No browser, Host, test installation, data
// directory, or SQLite is created; run with vitest --no-isolate.
const sections:DragSection[]=[{id:ROOT,items:['a','b','c','d']},{id:'group',items:['e','f']},{id:'empty',items:[]}];
const cards:CardBounds[]=[
 {id:'a',container:ROOT,rect:{left:0,top:0,width:280,height:210}},
 {id:'b',container:ROOT,rect:{left:300,top:0,width:280,height:210}},
 {id:'c',container:ROOT,rect:{left:0,top:230,width:280,height:210}},
 {id:'d',container:ROOT,rect:{left:300,top:230,width:280,height:210}},
 {id:'e',container:'group',rect:{left:0,top:500,width:280,height:210}},
 {id:'f',container:'group',rect:{left:300,top:500,width:280,height:210}},
];
const containers=[{id:ROOT,rect:{left:0,top:0,width:580,height:450}},{id:'group',rect:{left:0,top:480,width:580,height:240}},{id:'empty',rect:{left:0,top:750,width:580,height:90}}];
function intent(x:number,y:number,options:{source?:string;sourceContainer?:string;previous?:DragIntent|null;grouping?:boolean;keyboard?:boolean}={}){
 return resolveIntent({point:{x,y},source:options.source??'a',sourceContainer:options.sourceContainer??ROOT,sections,cards,containers,grouping:options.grouping??true,previous:options.previous??null,keyboard:options.keyboard??false});
}
describe('exclusive launcher-style drag intentions',()=>{
 it('immediately previews a new group at another loose card center, without an 800ms gate',()=>{
  expect(intent(440,105)).toEqual({kind:'create-group',container:ROOT,target:'b'});
 });
 it('sorts at an edge, and central feedback replaces that intent',()=>{
  expect(intent(570,105)).toEqual({kind:'move',container:ROOT,before:'c',over:'b'});
  expect(intent(440,105,{previous:intent(570,105)})).toMatchObject({kind:'create-group'});
 });
 it('uses a wider exit zone to avoid preview flicker, then leaves it',()=>{
  const previous=intent(440,105);
  expect(intent(530,105,{previous})).toEqual(previous);
  expect(intent(577,105,{previous})?.kind).toBe('move');
 });
 it('clears a preview outside all containers instead of committing a stale target',()=>{
  expect(intent(900,105,{previous:intent(440,105)})).toBeNull();
 });
 it('does not group a card with itself',()=>expect(intent(140,105)?.kind).toBe('move'));
 it('adds to an existing group instead of creating a nested group',()=>{
  expect(intent(140,605)).toEqual({kind:'add-to-group',container:'group',target:'e'});
 });
 it('never creates a group inside the source group',()=>{
  expect(intent(440,605,{source:'e',sourceContainer:'group'})?.kind).toBe('move');
 });
 it('can create a group with a loose card when dragging out of a group',()=>{
  expect(intent(440,105,{source:'e',sourceContainer:'group'})?.kind).toBe('create-group');
 });
 it('favorites and keyboard arrows reorder rather than accidentally grouping',()=>{
  expect(intent(440,105,{grouping:false})?.kind).toBe('move');
  expect(intent(440,105,{keyboard:true})?.kind).toBe('move');
 });
 it('chooses the lower row even when its target is farther horizontally',()=>{
  expect(intent(290,400,{grouping:false})).toMatchObject({kind:'move',before:'d',over:'c'});
 });
 it('distinguishes above/below from left/right between rows',()=>{
  expect(intent(440,225,{grouping:false})).toMatchObject({kind:'move',before:'d',over:'c'});
 });
 it('accepts an empty destination container',()=>{
  expect(intent(140,790)).toEqual({kind:'move',container:'empty',over:'container:empty'});
 });
 it('scales the center zone with actual card dimensions',()=>{
  expect(central({left:0,top:0,width:400,height:300},{x:200,y:150})).toBe(true);
  expect(central({left:0,top:0,width:400,height:300},{x:390,y:150})).toBe(false);
 });
 it('projects cross-group membership without mutating the starting layout',()=>{
  const before=JSON.stringify(sections),next=projectMove(sections,'a','group','f');
  expect(next[0].items).toEqual(['b','c','d']);expect(next[1].items).toEqual(['e','a','f']);
  expect(next.flatMap(s=>s.items).filter(id=>id==='a')).toHaveLength(1);
  expect(JSON.stringify(sections)).toBe(before);
 });
});
describe('the draggable root is not an interactive child',()=>{
 const control={} as Element;
 const root={contains:(node:Element)=>node===control} as HTMLElement;
 const target=(match:Element|null)=>({closest:()=>match} as unknown as EventTarget);
 it('allows a title/image descendant whose closest role=button is the drag root',()=>expect(isDragControl(target(root),root)).toBe(false));
 it('blocks a real descendant button or amount editor',()=>expect(isDragControl(target(control),root)).toBe(true));
 it('allows ordinary noninteractive content',()=>expect(isDragControl(target(null),root)).toBe(false));
});
describe('committed layout keeps references and facts separate',()=>{
 const layout:GalleryLayout={revision:'4',ungrouped:['a','b','c'],groups:[{id:'g',name:'练习',activity_ids:['d','e']}],favorites:['a','d']};
 it('creates a first group from two original cards without touching favorites/revision or input',()=>{
  const before=JSON.stringify(layout),next=mergeCards(layout,'a','b','new');
  expect(next.groups.find(g=>g.id==='new')).toEqual({id:'new',name:'新分组',activity_ids:['b','a']});
  expect(next.top_order).toEqual(['group:new','card:c','group:g']);
  expect(next.ungrouped).toEqual(['c']);expect(next.favorites).toEqual(['a','d']);expect(next.revision).toBe('4');expect(JSON.stringify(layout)).toBe(before);
 });
 it('moving a card into an existing group neither clones it nor creates nesting',()=>{
  const next=mergeCards(layout,'a','d','unused');expect(next.groups).toHaveLength(1);expect(next.groups[0].activity_ids).toEqual(['d','e','a']);
 });
 it('rejects deleted or identical targets and grouping within the same group',()=>{
  expect(mergeCards(layout,'a','missing','new')).toBe(layout);
  expect(mergeCards(layout,'missing','b','new')).toBe(layout);
  expect(mergeCards(layout,'d','e','new')).toBe(layout);
  expect(mergeCards(layout,'a','a','new')).toBe(layout);
 });
 it('moves out and preserves every member once',()=>{
  const next=moveCard(layout,'d',{group:null,before:'b'});
  expect(next.ungrouped).toEqual(['a','d','b','c']);expect(next.groups[0].activity_ids).toEqual(['e']);
  expect(new Set([...next.ungrouped,...next.groups.flatMap(g=>g.activity_ids)]).size).toBe(5);
 });
});
