import {type Daily} from './model';

export function calendarPeriod(days:Daily[],year:number,month:number):Daily[]{
 const prefix=String(year).padStart(4,'0')+'-'+(month?String(month).padStart(2,'0')+'-':'');
 return days.filter(d=>d.date.startsWith(prefix));
}
