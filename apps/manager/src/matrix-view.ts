import {dateWindow} from './date-viewport';
import {shiftDate,type Daily} from './model';

export function matrixColumns(width:number){
 const identityWidth=width<600?124:184;
 return {identityWidth,count:Math.max(1,Math.min(90,Math.floor((width-identityWidth)/48)))};
}
export function matrixDates(end:string,count:number){return dateWindow(end,count);}
export function matrixPage(end:string,count:number,direction:-1|1,today:string){
 const daysAvailable=dateWindow(end,90).length;
 const next=shiftDate(end,direction<0?-Math.min(count,daysAvailable-1):count);
 return next>today?today:next;
}
export function matrixSummary(days:Daily[]){
 const recorded=days.filter(d=>BigInt(d.recorded_units)>0n);
 return {recordedDays:recorded.length,lastDate:recorded.reduce<string|null>((latest,d)=>latest===null||d.date>latest?d.date:latest,null)};
}
