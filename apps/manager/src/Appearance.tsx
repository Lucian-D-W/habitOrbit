import {LivePreview} from './LivePreview';
import {defaultEffects,presets,type OrbEffects,type Visual} from './model';

export function Appearance({value,onChange,name,record}:{value:Visual;onChange:(visual:Visual)=>void;name:string;record:string}){
 const effects={...defaultEffects,...value.effects};
 const patch=(change:Partial<Visual>)=>onChange({...value,...change});
 const effect=(change:Partial<OrbEffects>)=>patch({effects:{...effects,...change}});
 const ranges:[keyof Pick<OrbEffects,'highlight_intensity'|'flow_speed'|'breath_amplitude'>,string,number,number][]=[
  ['highlight_intensity','高光强度',0,2],['flow_speed','内部流动速度',0,3],['breath_amplitude','呼吸幅度',0,2]];
 return <div className="appearance-workbench"><div className="appearance-preview"><LivePreview visual={value}/><h3>{name.trim()||'你的新星球'}</h3><p>{record}</p></div><div className="appearance-controls">
 <section className="appearance-group"><h3>配色与形态</h3><div className="preset-picker">{presets.map(p=><button type="button" key={p.base_preset} onClick={()=>onChange(structuredClone(p))}><i style={{background:`linear-gradient(135deg,${p.palette.join(',')})`}}/>{p.base_preset[0].toUpperCase()+p.base_preset.slice(1)}</button>)}</div>
 <button type="button" onClick={()=>onChange(structuredClone(presets.find(p=>p.base_preset===value.base_preset)??presets[0]))}>恢复当前预设默认</button>
 <label>渐变配色</label><div className="palette-picker">{value.palette.map((color,i)=><input key={i} aria-label={'渐变颜色 '+(i+1)} type="color" value={color} onChange={e=>patch({palette:value.palette.map((v,j)=>j===i?e.target.value:v)})}/>)}<button type="button" disabled={value.palette.length>=5} onClick={()=>patch({palette:[...value.palette,value.accent_color]})}>＋</button><button type="button" disabled={value.palette.length<=2} onClick={()=>patch({palette:value.palette.slice(0,-1)})}>－</button></div>
 <label>身份强调色<input type="color" value={value.accent_color} onChange={e=>patch({accent_color:e.target.value})}/></label><label>光晕颜色<input type="color" value={effects.glow_color??value.accent_color} onChange={e=>effect({glow_color:e.target.value})}/></label>
 <label>内部形态<select value={value.interior_style} onChange={e=>patch({interior_style:e.target.value})}><option value="flowing">柔和流动</option><option value="minimal">简约</option></select></label>
 <label className="check-label"><input type="checkbox" checked={effects.show_particles} onChange={e=>effect({show_particles:e.target.checked})}/>显示内部粒子</label><label>粒子颜色<input type="color" value={effects.particle_color} onChange={e=>effect({particle_color:e.target.value})}/></label></section>
 <section className="appearance-group"><h3>光效与运动</h3><p className="hint">默认光晕外径是球体的 2 倍。调整范围仅改变外观。</p>{ranges.map(([key,label,min,max])=><label key={key}>{label} · {effects[key].toFixed(2)}<input type="range" min={min} max={max} step={.05} value={effects[key]} onChange={e=>effect({[key]:Number(e.target.value)})}/></label>)}{(['x','y'] as const).map(axis=><label key={axis}>高光 {axis.toUpperCase()} · {Math.round(value.highlight[axis]*100)}%<input type="range" min={0} max={100} value={Math.round(value.highlight[axis]*100)} onChange={e=>patch({highlight:{...value.highlight,[axis]:Number(e.target.value)/100}})}/></label>)}</section>
 </div></div>;
}
