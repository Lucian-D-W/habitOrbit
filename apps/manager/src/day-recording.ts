import type {DayRecording} from './model';

const ordered=(a:string,b:string)=>BigInt(a)<BigInt(b)?-1:BigInt(a)>BigInt(b)?1:0;
export function changeCut(cuts:string[],from:string|null,to:string,units:string):string[]|null{
 const at=BigInt(to);if(at<=0n||at>=BigInt(units))return null;
 const rest=cuts.filter(c=>c!==from);if(rest.includes(to))return null;
 return [...rest,to].sort(ordered);
}
export function recordingSegments(recording:DayRecording,cuts=recording.cuts){
 let start=0n;
 return [...cuts,recording.units].map(endText=>{
  const end=BigInt(endText);let amount=0n;
  for(const run of recording.runs){const from=BigInt(run.start),until=from+BigInt(run.count),left=start>from?start:from,right=end<until?end:until;if(right>left)amount+=(right-left)*BigInt(run.amount_atoms);}
  const segment={start:start.toString(),end:end.toString(),count:(end-start).toString(),amount_atoms:amount.toString()};start=end;return segment;
 }).filter(s=>BigInt(s.count)>0n);
}
export const beadWindowSize=1000n;
export function beadWindow(recording:DayRecording,requestedStart:bigint){
 const units=BigInt(recording.units),last=units===0n?0n:(units-1n)/beadWindowSize*beadWindowSize;
 const start=requestedStart<0n?0n:requestedStart>last?last:requestedStart/beadWindowSize*beadWindowSize;
 const end=start+beadWindowSize<units?start+beadWindowSize:units;
 const beads:{index:string;amount_atoms:string}[]=[];let runIndex=0;
 for(let i=start;i<end;i++){
  while(runIndex<recording.runs.length-1 && i>=BigInt(recording.runs[runIndex].start)+BigInt(recording.runs[runIndex].count))runIndex++;
  beads.push({index:(i+1n).toString(),amount_atoms:recording.runs[runIndex].amount_atoms});
 }
 return {start,end,units,beads};
}
