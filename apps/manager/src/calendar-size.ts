export function calendarSize(width:number,firstDate:string,count:number){
 const weeks=Math.ceil((count+new Date(firstDate+'T12:00:00Z').getUTCDay())/7),gap=width>=1200?4:3;
 return {weeks,gap,block:Math.max(12,Math.min(40,Math.floor((width+gap)/weeks)-gap))};
}
