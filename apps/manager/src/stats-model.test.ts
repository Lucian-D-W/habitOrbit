import {describe,it,expect} from 'vitest';import {amountTotal,dayLabel,recordedTime} from './stats-model';import {type Daily,type Event} from './model';
describe('statistical presentation preserves facts',()=>{
 it('sums beyond the safe floating integer without rounding',()=>{const days=[{amount_atoms:'9007199254740993'},{amount_atoms:'1250'}] as Daily[];expect(amountTotal(days)).toBe('9007199254742243');});
 it('does not call an inapplicable empty date no record',()=>{expect(dayLabel({date:'2026-01-01',amount_atoms:'0',availability:'not_applicable'} as Daily,'页')).toBe('2026-01-01 · 不适用');expect(dayLabel({date:'2026-10-05',amount_atoms:'1250',availability:'in_scope'} as Daily,'杯')).toBe('2026-10-05 · 1.25 杯');});
 it('uses the offset stored with the event, independent of the current timezone',()=>{const event={recorded_at_ms:String(Date.UTC(2026,9,4,16,30)),utc_offset_minutes:480} as Event;expect(recordedTime(event)).toBe('00:30:00');});
});
