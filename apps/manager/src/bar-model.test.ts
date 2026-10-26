import {expect,it} from 'vitest';import {barLayout} from './bar-model';import {type Daily} from './model';
it('日期增加不挤细柱子，零量和不适用分开，视口不改数量尺度',()=>{
 const days:Daily[]=[{date:'2026-10-03',amount_atoms:'5125',recorded_units:'1',level:2,availability:'in_scope'},{date:'2026-10-04',amount_atoms:'0',recorded_units:'0',level:0,availability:'in_scope'},{date:'2026-10-05',amount_atoms:'0',recorded_units:'0',level:null,availability:'not_applicable'}];
 const small=barLayout(days,200),large=barLayout(days,1200);expect(small.values).toEqual([5.125,0,null]);expect(small.maximum).toBe(large.maximum);
 const long=barLayout(Array.from({length:90},(_,i)=>days[i%3]),500);expect(long.width).toBeGreaterThan(90*48);expect(long.maximum).toBe(small.maximum);
});
