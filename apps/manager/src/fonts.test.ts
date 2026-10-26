import {afterEach,expect,it,vi} from 'vitest';
import {availableFont,fontStack,readFont,fallbackFont} from './fonts';
afterEach(()=>vi.unstubAllGlobals());
it('falls back when a saved font is removed from Windows',()=>{
 expect(availableFont('已卸载字体',['微软雅黑','Segoe UI'])).toBe('');
 expect(availableFont('微软雅黑',['微软雅黑','Segoe UI'])).toBe('微软雅黑');
 expect(fontStack('')).toBe(fallbackFont);
});
it('quotes family names and retains Chinese glyph fallback',()=>{
 expect(fontStack('A "quoted" family')).toBe('"A \\"quoted\\" family", '+fallbackFont);
});
it('survives unavailable local preferences',()=>{
 vi.stubGlobal('localStorage',{getItem:()=>{throw new Error('blocked');}});
 expect(readFont()).toBe('');
});
