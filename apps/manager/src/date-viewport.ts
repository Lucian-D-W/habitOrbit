import {shiftDate} from './model';
export function dateWindow(end:string,limit=90):string[]{
 const available=Math.floor((Date.parse(end+'T12:00:00Z')-Date.parse('0001-01-01T12:00:00Z'))/86400000)+1;
 const count=Math.max(0,Math.min(limit,available));return Array.from({length:count},(_,i)=>shiftDate(end,i-count+1));
}
