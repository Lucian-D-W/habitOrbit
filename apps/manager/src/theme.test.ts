import {afterEach,describe,expect,it,vi} from 'vitest';
import {readTheme,saveTheme,themeKey} from './theme';
afterEach(()=>vi.unstubAllGlobals());
describe('local theme preference',()=>{
 it('restores only the two supported themes and persists selection',()=>{
  const values=new Map<string,string>();
  vi.stubGlobal('localStorage',{getItem:(k:string)=>values.get(k)??null,setItem:(k:string,v:string)=>values.set(k,v)});
  expect(readTheme()).toBe('dark');saveTheme('light');expect(readTheme()).toBe('light');
  values.set(themeKey,'obsolete');expect(readTheme()).toBe('dark');
 });
 it('remains usable when storage is unavailable',()=>{
  vi.stubGlobal('localStorage',{getItem:()=>{throw new Error('blocked');},setItem:()=>{throw new Error('blocked');}});
  expect(readTheme()).toBe('dark');expect(()=>saveTheme('light')).not.toThrow();
 });
});
