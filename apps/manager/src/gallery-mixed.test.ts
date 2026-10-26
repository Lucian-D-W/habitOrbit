import {describe,it,expect} from 'vitest';
import {cardKey,groupKey,topOrder,moveCard,mergeCards,dissolveGroup,type GalleryLayout} from './gallery-layout';
import {ROOT,layoutSections,resolveIntent,type DragSection} from './gallery-drag';
import {compactGroupText} from './TrainingGroups';

// All checks are pure object/geometry calculations; no environment or DB.
const layout:GalleryLayout={revision:'11',ungrouped:['a','b'],groups:[{id:'g',name:'G',activity_ids:['c','d']},{id:'h',name:'H',activity_ids:[]}],favorites:['c']};
const members=(value:GalleryLayout)=>[...value.ungrouped,...value.groups.flatMap(g=>g.activity_ids)].sort();
describe('mixed top-level gallery order',()=>{
 it('reads legacy layouts without rewriting the input',()=>{
  expect(topOrder(layout)).toEqual(['card:a','card:b','group:g','group:h']);expect(layout.top_order).toBeUndefined();
 });
 it('extracts below a group without jumping to the beginning',()=>{
  const next=moveCard(layout,'c',{group:null,topBefore:groupKey('h')});
  expect(next.top_order).toEqual(['card:a','card:b','group:g','card:c','group:h']);
  expect(next.favorites).toEqual(['c']);expect(next.revision).toBe('11');expect(members(next)).toEqual(members(layout));
 });
 it('extracts above and after all groups with an explicit anchor',()=>{
  expect(moveCard(layout,'c',{group:null,topBefore:groupKey('g')}).top_order).toEqual(['card:a','card:b','card:c','group:g','group:h']);
  expect(moveCard(layout,'c',{group:null,topBefore:null}).top_order?.at(-1)).toBe('card:c');
 });
 it('context menu extraction lands next to the old group',()=>{
  expect(moveCard(layout,'c',{group:null}).top_order).toEqual(['card:a','card:b','group:g','card:c','group:h']);
 });
 it('moving into another group removes only the loose entry',()=>{
  const next=moveCard(layout,'a',{group:'h'});expect(next.top_order).toEqual(['card:b','group:g','group:h']);
  expect(next.groups[1].activity_ids).toEqual(['a']);expect(members(next)).toEqual(members(layout));
 });
 it('creates a group at the target position between other groups',()=>{
  const mixed=moveCard(layout,'c',{group:null,topBefore:groupKey('h')});
  const next=mergeCards(mixed,'a','c','new');
  expect(next.top_order).toEqual(['card:b','group:g','group:new','group:h']);expect(members(next)).toEqual(members(layout));
 });
 it('dissolves members in place and keeps the surrounding order',()=>{
  const next=dissolveGroup({...layout,top_order:['group:g','card:a','group:h','card:b']},'g');
  expect(next.top_order).toEqual(['card:c','card:d','card:a','group:h','card:b']);expect(members(next)).toEqual(members(layout));
 });
 it('preserves hidden archived references when filtering displayed sections',()=>{
  const mixed={...layout,top_order:['card:a','group:g','card:b','group:h']};
  const sections=layoutSections(mixed,['a','c']);
  expect(sections).toEqual([{id:'ungrouped:g',items:['a'],beforeGroup:'g'},{id:'g',items:['c']},{id:'ungrouped:h',items:[],beforeGroup:'h'},{id:'h',items:[]},{id:ROOT,items:[]}]);
  expect(mixed.ungrouped).toEqual(['a','b']);expect(mixed.groups[0].activity_ids).toEqual(['c','d']);
 });
});
describe('outside-group extraction inside the main board',()=>{
 const sections:DragSection[]=[{id:'ungrouped:g',items:[],beforeGroup:'g'},{id:'g',items:['c','d']},{id:ROOT,items:[]}];
 const containers=[{id:'ungrouped:g',rect:{left:20,top:0,width:600,height:20}},{id:'g',rect:{left:20,top:40,width:600,height:400}},{id:ROOT,rect:{left:20,top:460,width:600,height:20}}];
 const cards=[{id:'c',container:'g',rect:{left:34,top:80,width:260,height:346}},{id:'d',container:'g',rect:{left:310,top:80,width:260,height:346}}];
 const at=(x:number,y:number,grouping=true)=>resolveIntent({point:{x,y},source:'c',sourceContainer:'g',sections,containers,cards,previous:null,keyboard:false,grouping,bounds:{left:0,top:0,width:640,height:900}});
 it('accepts the large blank area below the group',()=>expect(at(300,800)).toMatchObject({kind:'move',container:ROOT}));
 it('accepts upper and lower side gutters with corresponding anchors',()=>{
  expect(at(8,100)).toMatchObject({kind:'move',container:'ungrouped:g'});
  expect(at(632,400)).toMatchObject({kind:'move',container:ROOT});
 });
 it('does not mistake an internal group gap for extraction',()=>expect(at(300,200)).toMatchObject({kind:'move',container:'g'}));
 it('does not accept outside the app board or extend favorites',()=>{
  expect(at(900,800)).toBeNull();expect(at(300,800,false)).toBeNull();
 });
 it('projects the same top position after save/read',()=>{
  const next=moveCard(layout,'c',{group:null,topBefore:null});
  expect(layoutSections(JSON.parse(JSON.stringify(next)),members(next)).at(-1)?.items).toEqual(['c']);
 });
});
describe('compact groups',()=>{
 const groups=(amounts:string[])=>amounts.map(amount_atoms=>({amount_atoms,recorded_units:'1',first_event_no:'1',last_event_no:'1'}));
 it('shows count times real mean and keeps fractional precision',()=>{
  expect(compactGroupText(groups(['8000','7000']),'次')).toBe('2 × 7.5 次');
  expect(compactGroupText(groups(['1000','1000','1']),'次')).toBe('3 × 0.667 次');
 });
 it('hides the line without any valid groups',()=>expect(compactGroupText([],'次')).toBe(''));
});
