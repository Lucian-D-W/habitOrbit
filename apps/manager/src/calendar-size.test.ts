import {expect,it} from 'vitest';import {calendarSize} from './calendar-size';
it('全年周结构在宽卡填充而窄卡保留可读尺寸',()=>{
 for(const [date,count] of [['2026-01-01',365],['2028-01-01',366]] as const){
  const wide=calendarSize(1500,date,count),narrow=calendarSize(500,date,count);
  expect(wide.weeks).toBe(date.startsWith('2028')?54:53);expect(wide.block).toBeGreaterThan(12);
  const rendered=wide.weeks*(wide.block+wide.gap)-wide.gap;expect(rendered).toBeLessThanOrEqual(1500);expect(1500-rendered).toBeLessThan(wide.weeks);
  expect(narrow.block).toBe(12);expect(narrow.weeks*(narrow.block+narrow.gap)-narrow.gap).toBeGreaterThan(500);
 }
});
