import {expect,it} from 'vitest';import {dateWindow} from './date-viewport';import {shiftDate} from './model';
it('连续视口跨年和闰日不重复或漏日',()=>{
 expect(dateWindow('2024-03-01',4)).toEqual(['2024-02-27','2024-02-28','2024-02-29','2024-03-01']);
 const recent=dateWindow('2026-01-05'),older=dateWindow(shiftDate(recent[0],-1)),all=[...older,...recent];
 expect(new Set(all).size).toBe(180);for(let i=1;i<all.length;i++)expect(shiftDate(all[i-1],1)).toBe(all[i]);
 expect(dateWindow('0001-01-03')).toEqual(['0001-01-01','0001-01-02','0001-01-03']);
});
