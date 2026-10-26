import {describe,it,expect} from 'vitest';
import {axisCutAt,axisLayout,beadPitch,dividerPitch} from './bead-axis';
import {changeCut,recordingSegments} from './day-recording';
import type {DayRecording} from './model';

describe('continuous bead axis',()=>{
 const indices=Array.from({length:8},(_,i)=>String(i+1));
 it('packs adjacent beads against the left edge without reserved divider gaps',()=>{
  const layout=axisLayout(indices,[],400);
  expect([...layout.beads.values()].map(p=>p.x)).toEqual(indices.map((_,i)=>i*beadPitch));
  expect(layout.dividers.size).toBe(0);
 });
 it('accepts the whole axis, including bead centers and empty track, and never creates empty endpoint groups',()=>{
  const layout=axisLayout(indices,[],400);
  expect(axisCutAt(layout,51,20,'8')).toBe('3');
  expect(axisCutAt(layout,0,20,'8')).toBe('1');
  expect(axisCutAt(layout,399,20,'8')).toBe('7');
  expect(axisCutAt(axisLayout(['1'],[],400),100,20,'1')).toBeNull();
 });
 it('moves beads when adding, moving, and removing a divider',()=>{
  const original=axisLayout(indices,[],400),added=axisLayout(indices,['3'],400),moved=axisLayout(indices,['5'],400);
  expect(added.beads.get('3')).toEqual(original.beads.get('3'));
  expect(added.beads.get('4')!.x).toBe(original.beads.get('4')!.x+dividerPitch);
  expect(moved.beads.get('4')).toEqual(original.beads.get('4'));
  expect(moved.beads.get('6')!.x).toBe(original.beads.get('6')!.x+dividerPitch);
  expect(axisLayout(indices,[],400)).toEqual(original);
 });
 it('wraps visually without adding group boundaries or orphaning dividers',()=>{
  const layout=axisLayout(indices,['3'],72);
  expect(layout.dividers.get('3')).toEqual({x:60,y:0});
  expect(layout.beads.get('4')).toEqual({x:0,y:40});
  expect(axisCutAt(layout,21,60,'8')).toBe('4');
  const narrow=axisLayout(indices,['3'],60);
  expect(narrow.beads.get('3')!.y).toBe(narrow.dividers.get('3')!.y);
  expect([...narrow.dividers.keys()]).toEqual(['3']);
 });
 it('avoids occupied boundaries during dragging and supports exact large indices',()=>{
  const layout=axisLayout(indices,['3'],400);
  expect(axisCutAt(layout,60,20,'8',['3'])).not.toBe('3');
  const huge=['9007199254740993','9007199254740994','9007199254740995'];
  expect(axisCutAt(axisLayout(huge,[],100),39,20,'9007199254740996')).toBe(huge[1]);
 });
 it('keeps the same quantity through split, drag, and merge',()=>{
  const recording={units:'8',cuts:[],runs:[{start:'0',count:'8',amount_atoms:'5000'}]} as unknown as DayRecording;
  const added=changeCut([],null,'3','8')!,moved=changeCut(added,'3','5','8')!;
  expect(recordingSegments(recording,added).map(s=>s.count)).toEqual(['3','5']);
  expect(recordingSegments(recording,moved).map(s=>s.count)).toEqual(['5','3']);
  for(const cuts of [[],added,moved])expect(recordingSegments(recording,cuts).reduce((n,s)=>n+BigInt(s.amount_atoms),0n)).toBe(40000n);
 });
});
