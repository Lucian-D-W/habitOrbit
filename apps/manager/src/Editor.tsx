import {useEffect,useRef,useState,type FormEvent} from 'react';
import {rpc,send,TransportError,type Request} from './api';
import {atoms,formatAtoms,newConfig,unit,visualSignature,type Activity,type Config,type Row} from './model';
import {icons,Icon} from './icons';import {Thumbnail} from './Thumbnail';
import {Bento,type BentoCard} from './Bento';
import {Appearance} from './Appearance';
type Rules={colors:string[];samples:{amount_atoms:string;level:number}[]};
export function Editor({rows,editing,onDone,onCancel,onExitState}:{rows:Row[];editing:Row|null;onDone:(a:Activity)=>void;onCancel:()=>void;onExitState:(state:{dirty:boolean;busy:boolean})=>void}){
 const [resetTarget,setResetTarget]=useState<HTMLSpanElement|null>(null);
 const [config,setConfig]=useState<Config>(()=>structuredClone(editing?.activity.config??newConfig(rows)));
 const [step,setStep]=useState(formatAtoms(config.step_atoms));const [breaks,setBreaks]=useState([config.breaks.b1,config.breaks.b2,config.breaks.b3].map(formatAtoms));
 const [gap,setGap]=useState(config.group_gap_ms?formatAtoms(config.group_gap_ms):'4');
 const [rules,setRules]=useState<Rules|null>(null);
 const [error,setError]=useState('');const [pending,setPending]=useState(false);const [uncertain,setUncertain]=useState<Request|null>(null);
 const initial=useRef(JSON.stringify({config,step,breaks,gap}));
 useEffect(()=>{onExitState({dirty:JSON.stringify({config,step,breaks,gap})!==initial.current,busy:pending||!!uncertain});},[config,step,breaks,gap,pending,uncertain,onExitState]);
 useEffect(()=>()=>onExitState({dirty:false,busy:false}),[onExitState]);
 const [unitChoice,setUnitChoice]=useState(()=>config.kind==='duration'?'分钟':['次','页','分钟','毫升'].includes(config.unit)?config.unit:'custom');
 const [customUnit,setCustomUnit]=useState(()=>unitChoice==='custom'?config.unit:'');
 const duplicate=rows.some(r=>r.activity.id!==editing?.activity.id&&visualSignature(r.activity.config.visual)===visualSignature(config.visual));
 const patch=(v:Partial<Config>)=>setConfig(c=>({...c,...v}));
 useEffect(()=>{let active=true;const timer=setTimeout(()=>{
  try{const candidate={...config,name:config.name.trim()||'分级预览',step_atoms:atoms(step),breaks:{b1:atoms(breaks[0]),b2:atoms(breaks[1]),b3:atoms(breaks[2])},group_gap_ms:config.group_gap_ms===null?null:atoms(gap)};
   rpc<Rules>('rules.describe',{config:candidate}).then(v=>{if(active)setRules(v);}).catch(()=>{if(active)setRules(null);});
  }catch{setRules(null);}
 },200);return()=>{active=false;clearTimeout(timer);};},[config,step,breaks,gap]);
 async function save(e?:FormEvent){e?.preventDefault();setError('');let request=uncertain;
  if(!request){try{
   if([...config.name.trim()].length<1||[...config.name.trim()].length>64)throw new Error('名称需为1至64个字符。');
   if(duplicate)throw new Error('这套外观已经在使用，请调整配色、高光或内部形态。');
   const value={...config,name:config.name.trim(),step_atoms:atoms(step),group_gap_ms:config.group_gap_ms===null?null:atoms(gap),breaks:{b1:atoms(breaks[0]),b2:atoms(breaks[1]),b3:atoms(breaks[2])}};
   request={id:crypto.randomUUID(),method:editing?'activity.update':'activity.create',params:editing?{activityId:editing.activity.id,config:value}:{config:value}};
  }catch(e){setError((e as Error).message);return;}}
  setPending(true);try{const activity=await send<Activity>(request);setUncertain(null);onDone(activity);}
  catch(e){if(e instanceof TransportError)setUncertain(e.request);else setUncertain(null);setError((e as Error).message);}finally{setPending(false);}
 }
 const cards:BentoCard[]=[
  {id:'identity',title:'名称与身份',content:<>   <label>名称<input autoFocus value={config.name} onChange={e=>patch({name:e.target.value})} placeholder="例如：阅读、饮水、步行" required/></label>
   <label>图标</label><div className="icon-picker">{Object.keys(icons).map(id=><button key={id} type="button" title={id} aria-label={id} aria-pressed={config.icon_id===id} className={config.icon_id===id?'chosen':''} onClick={()=>patch({icon_id:id})}><Icon id={id}/></button>)}</div>
   <label>描述 <span className="muted">可选</span><textarea rows={2} value={config.description} onChange={e=>patch({description:e.target.value})} placeholder="这个条目记录什么？"/></label>
</>},
  {id:'rules',title:'记录方式',content:<>
   {editing?<><p className="fixed-measurement">点一下 = <strong>{step} {unit(config)}</strong></p><p className="hint">单位和每次记录量在创建后固定。需要不同记录方式时请新建星球。</p></>:<><div className="measurement-row"><span>点一下 =</span><label><span className="sr-only">每次记录量</span><input aria-label="每次记录量" inputMode="decimal" value={step} onChange={e=>setStep(e.target.value)} required/></label><label><span className="sr-only">记录单位</span><select aria-label="记录单位" value={unitChoice} onChange={e=>{const choice=e.target.value;setUnitChoice(choice);patch({kind:choice==='分钟'?'duration':'count',unit:choice==='分钟'?'min':choice==='custom'?customUnit:choice});}}>{['次','页','分钟','毫升'].map(u=><option key={u}>{u}</option>)}<option value="custom">自定义</option></select></label></div>{unitChoice==='custom'&&<label>自定义单位<input value={customUnit} onChange={e=>{setCustomUnit(e.target.value);patch({unit:e.target.value});}} placeholder="例如：米、组、千卡" required maxLength={24}/></label>}<p className="hint">每次记录量大于0，最多三位小数。创建后单位和每次记录量固定。</p></>}
   <div className="group-option"><label className="check-label"><input type="checkbox" checked={config.group_gap_ms!==null} onChange={e=>patch({group_gap_ms:e.target.checked?'4000':null})}/>按点击间隔分组</label>{config.group_gap_ms!==null&&<label>间隔（秒）<input inputMode="decimal" value={gap} onChange={e=>setGap(e.target.value)}/></label>}</div>
</>},
  {id:'heat',title:'热力图分级',content:<><p className="hint">0 单独表示无记录。三个分界严格递增，统计颜色随星球外观。</p><div className="form-row thresholds">{breaks.map((v,i)=><label key={i}>分界 {i+1}<input inputMode="decimal" value={v} onChange={e=>setBreaks(b=>b.map((old,j)=>j===i?e.target.value:old))}/></label>)}</div>
   {rules&&<div className="level-preview">{rules.samples.map((s,i)=><span key={i}><i style={{background:rules.colors[s.level]}}/>{formatAtoms(s.amount_atoms)} {unit(config)}</span>)}</div>}
</>},
  {id:'appearance',title:'预览与外观',content:<Appearance value={config.visual} onChange={v=>patch({visual:v})} name={config.name} record={`点一下 = ${step||'…'} ${unit(config)}`}/>},
  {id:'compare',title:'已有星球对照',content:<><div className="compare-panel"><p className="hint">程序检查参数重复，辨识度由你确认。</p><div className="compare-grid">{rows.map(r=><div key={r.activity.id}><Thumbnail activity={r.activity} small/><span>{r.activity.config.name}</span></div>)}</div>{rows.length===0&&<p className="muted">这是你的第一颗星球。</p>}</div></>}
 ];
 return <section className="editor-page"><div className="page-title"><h1>{editing?'编辑星球':'创建星球'}</h1><div className="actions"><button type="button" onClick={onCancel} disabled={pending}>返回总览</button><span className="page-reset-slot" ref={setResetTarget}/></div></div>
 <form onSubmit={save}><fieldset disabled={pending||!!uncertain} className="editor-fieldset"><Bento name={editing?'edit':'create'} resetTarget={resetTarget} resetDisabled={pending||!!uncertain} cards={['identity','rules','heat','compare','appearance'].map(id=>({...cards.find(card=>card.id===id)!,column:(id==='compare'||id==='appearance'?1:0) as 0|1}))}/></fieldset>
 <div className="editor-save">{duplicate&&<p className="error" role="alert">外观参数与已有星球相同，请调整后保存。</p>}{error&&<p className="error" role="alert">{error}</p>}{uncertain&&<p className="hint">连接中断，保存结果待核对。重试沿用原请求。</p>}
 <button className="primary" disabled={pending||(!uncertain&&duplicate)} type="submit">{pending?'保存中…':uncertain?'核对并重试保存':editing?'保存修改':'保存星球'}</button></div>
 </form></section>;
}
