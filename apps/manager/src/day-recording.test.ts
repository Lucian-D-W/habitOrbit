import {describe,it,expect} from 'vitest';
import {beadWindow,changeCut,recordingSegments} from './day-recording';
import type {DayRecording} from './model';
const record=(units='5'):DayRecording=>({date:'2026-10-11',amount_atoms:(BigInt(units)*5000n).toString(),units,step_atoms:'5000',version:'v',cuts:['2'],runs:[{start:'0',count:units,amount_atoms:'5000',event_no:'1'}],groups:[],grouped:true,manual:true});
describe('portion-based bead editing',()=>{
 it('shows five beads for 25 minutes and moving a divider preserves total',()=>{const r=record();expect(beadWindow(r,0n).beads).toHaveLength(5);expect(recordingSegments(r).map(g=>g.amount_atoms)).toEqual(['10000','15000']);expect(recordingSegments(r,['3']).map(g=>g.amount_atoms)).toEqual(['15000','10000']);});
 it('rejects endpoints and collisions while allowing moves across rows',()=>{expect(changeCut(['2','4'],'2','3','5')).toEqual(['3','4']);expect(changeCut(['2','4'],'2','4','5')).toBeNull();expect(changeCut([],null,'0','5')).toBeNull();expect(changeCut([],null,'5','5')).toBeNull();expect(changeCut(['9'],'9','21','25')).toEqual(['21']);});
 it('deleting a divider merges exact quantities including older portion sizes',()=>{const r=record();r.runs=[{start:'0',count:'2',amount_atoms:'1000',event_no:'1'},{start:'2',count:'3',amount_atoms:'5000',event_no:'2'}];expect(recordingSegments(r,[])).toEqual([{start:'0',end:'5',count:'5',amount_atoms:'17000'}]);});
 it('keeps huge amounts exact and windows every bead without making clusters',()=>{const r=record('9007199254740993');r.cuts=['9007199254740992'];const w=beadWindow(r,9007199254740992n);expect(w.beads.length).toBeLessThanOrEqual(1000);expect(w.beads.at(-1)?.index).toBe('9007199254740993');expect(recordingSegments(r).at(-1)?.amount_atoms).toBe('5000');expect(beadWindow(r,0n).beads).toHaveLength(1000);});
 it('clamps the visible window after tail reduction and handles zero',()=>{const r=record('3');expect(beadWindow(r,1000n).start).toBe(0n);const empty=record('0');empty.cuts=[];expect(beadWindow(empty,0n).beads).toEqual([]);expect(recordingSegments(empty)).toEqual([]);});
});
