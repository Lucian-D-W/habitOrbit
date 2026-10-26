import {expect,it} from 'vitest';
import {matrixColumns,matrixDates,matrixPage,matrixSummary} from './matrix-view';
import type {Daily} from './model';

it('按宽度保持舒适列宽，不固定七天也不横向超出',()=>{
 for(const width of [220,400,590,600,900,1600,2200]){
  const {identityWidth,count}=matrixColumns(width);
  expect(identityWidth+count*48).toBeLessThanOrEqual(width);
 }
 expect(matrixColumns(1600).count).toBeGreaterThan(matrixColumns(900).count);
});
it('最新日期在右，整段切换跨闰日且不重复，不显示未来',()=>{
 const dates=matrixDates('2024-03-01',3);
 expect(dates).toEqual(['2024-02-28','2024-02-29','2024-03-01']);
 const older=matrixPage(dates[2],3,-1,'2024-03-01');
 expect(matrixDates(older,3)).toEqual(['2024-02-25','2024-02-26','2024-02-27']);
 expect(matrixPage(older,3,1,'2024-03-01')).toBe('2024-03-01');
 expect(matrixPage('2024-02-29',3,1,'2024-03-01')).toBe('2024-03-01');
 expect(matrixPage('0001-01-02',7,-1,'2024-03-01')).toBe('0001-01-01');
 expect(matrixDates('0001-01-02',7)).toEqual(['0001-01-01','0001-01-02']);
});
it('同范围汇总按有效记录日计算，不按点击数或创建前空日',()=>{
 const days=[{date:'2026-10-05',recorded_units:'0'},{date:'2026-10-04',recorded_units:'20'},{date:'2026-10-03',recorded_units:'1'},{date:'2026-10-02',recorded_units:'0'}] as Daily[];
 expect(matrixSummary(days)).toEqual({recordedDays:2,lastDate:'2026-10-04'});
 expect(matrixSummary([])).toEqual({recordedDays:0,lastDate:null});
});
