import {formatAtoms,type Daily,type Event} from './model';
export type StatsContext={total_atoms:string;years:number[];colors:string[]};
export type MatrixRow={id:string;days:Daily[];colors:string[]};
export const amountTotal=(days:Daily[])=>days.reduce((sum,d)=>sum+BigInt(d.amount_atoms),0n).toString();
export function dayLabel(day:Daily,unit:string){return `${day.date} · ${day.availability==='not_applicable'?'不适用':`${formatAtoms(day.amount_atoms)} ${unit}${day.amount_atoms==='0'?' · 无记录':''}`}`;}
export function recordedTime(event:Event){return new Date(Number(event.recorded_at_ms)+event.utc_offset_minutes*60000).toISOString().slice(11,19);}
export const levelLabels=(b:{b1:string;b2:string;b3:string},u:string)=>['无记录',`大于 0，少于 ${formatAtoms(b.b1)} ${u}`,`${formatAtoms(b.b1)} 至少于 ${formatAtoms(b.b2)} ${u}`,`${formatAtoms(b.b2)} 至少于 ${formatAtoms(b.b3)} ${u}`,`${formatAtoms(b.b3)} ${u}及以上`];
