import {describe,it,expect} from 'vitest';import {atoms,formatAtoms,initialVisual,newConfig,visualSignature,presets,type Row} from './model';
describe('精确计量边界',()=>{
 it('保留大于JavaScript整数上限的三位小数',()=>{expect(atoms('9007199254740.993')).toBe('9007199254740993');expect(formatAtoms('9007199254740993')).toBe('9007199254740.993');});
 it('拒绝超精度和指数文本',()=>{for(const text of ['1.0000','1e3','-1','NaN'])expect(()=>atoms(text)).toThrow();expect(formatAtoms('-5125')).toBe('-5.125');});
});
describe('外观与数量无关',()=>{
 it('seed和母版标签不规避重复',()=>{expect(visualSignature({...presets[0],decorative_seed:999,base_preset:'other'})).toBe(visualSignature(presets[0]));});
 it('超过九颗后仍提供未使用身份',()=>{
  const rows:Row[]=[];for(let i=0;i<16;i++){const visual=initialVisual(rows);expect(rows.some(r=>visualSignature(r.activity.config.visual)===visualSignature(visual))).toBe(false);
   rows.push({activity:{id:String(i),config:{...newConfig([]),visual}}} as Row);}
  expect(rows).toHaveLength(16);
 });
});
