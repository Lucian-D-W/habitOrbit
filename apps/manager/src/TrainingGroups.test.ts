import {describe,it,expect} from 'vitest';
import {groupText} from './TrainingGroups';
const groups=(amounts:string[])=>amounts.map(amount_atoms=>({amount_atoms,recorded_units:'1',first_event_no:'1',last_event_no:'1'}));
describe('训练量显示',()=>{
 it('按实际数量显示四组，不能把份数当数量',()=>{expect(groupText(groups(['6000','7000','7000','10000']),'次')).toEqual({amounts:'6 / 7 / 7 / 10',summary:'4组 × 平均7.5次，共30次'});});
 it('零组没有无效平均数',()=>{expect(groupText([],'次').summary).toBe('0组 × 平均0次，共0次');});
 it('三位小数四舍五入且保持大整数精度',()=>{expect(groupText(groups(['1000','1000','1']),'次').summary).toBe('3组 × 平均0.667次，共2.001次');expect(groupText(groups(['9007199254740993']),'次').amounts).toBe('9007199254740.993');});
});
