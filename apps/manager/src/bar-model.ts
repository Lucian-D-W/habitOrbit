import {type Daily} from './model';
export function barLayout(days:Daily[],viewportWidth:number){
 const values=days.map(d=>d.availability==='not_applicable'?null:Number(d.amount_atoms)/1000);
 return {width:Math.max(160,viewportWidth,days.length*48+16),maximum:Math.max(1,...values.map(v=>v??0))*1.08,values};
}
