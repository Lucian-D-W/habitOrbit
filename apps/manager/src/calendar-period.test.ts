import {expect,it} from 'vitest';
import {calendarPeriod} from './calendar-period';
import {shiftDate,type Daily} from './model';

it('年月筛选保留闰日、真实数量与不适用状态，跨年不混入',()=>{
 const days:Daily[]=[];
 for(let date='2027-12-31';date<='2029-01-01';date=shiftDate(date,1))days.push({date,amount_atoms:date==='2028-02-29'?'3125':'0',recorded_units:date==='2028-02-29'?'1':'0',level:date==='2028-02-29'?2:null,availability:date<'2028-02-29'?'not_applicable':'in_scope'});
 const feb=calendarPeriod(days,2028,2);
 expect(feb).toHaveLength(29);expect(feb[0].date).toBe('2028-02-01');expect(feb.at(-1)).toEqual({date:'2028-02-29',amount_atoms:'3125',recorded_units:'1',level:2,availability:'in_scope'});
 expect(feb[0].availability).toBe('not_applicable');
 expect(calendarPeriod(days,2028,4)).toHaveLength(30);
 expect(calendarPeriod(days,2028,12)).toHaveLength(31);
 const annual=calendarPeriod(days,2028,0);expect(annual).toHaveLength(366);expect(annual[0].date).toBe('2028-01-01');expect(annual.at(-1)?.date).toBe('2028-12-31');
 const jan=calendarPeriod(days,2029,1);expect(jan).toHaveLength(1);expect(jan[0].date).toBe('2029-01-01');
});

it('普通年二月没有闰日，月份按完整日期筛选',()=>{
 const days:Daily[]=[];
 for(let date='2026-02-01';date<='2026-03-01';date=shiftDate(date,1))days.push({date,amount_atoms:'0',recorded_units:'0',level:0,availability:'in_scope'});
 const feb=calendarPeriod(days,2026,2);expect(feb).toHaveLength(28);expect(feb.at(-1)?.date).toBe('2026-02-28');expect(calendarPeriod(days,2026,3)[0].date).toBe('2026-03-01');
});
