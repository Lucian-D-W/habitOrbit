import {useEffect,useRef,useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {send,TransportError,type Request} from './api';
import {atoms,formatAtoms,unit,type Row} from './model';

export function TodayAmount({row,disabled,onSaved,onExitState}:{row:Row;disabled:boolean;onSaved:()=>void;onExitState:(id:string,state:{dirty:boolean;busy:boolean}|null)=>void}){
 const [draft,setDraft]=useState<string|null>(null),[saving,setSaving]=useState(false),[error,setError]=useState('');
 const inFlight=useRef(false),uncertain=useRef<Request|null>(null),editDate=useRef(row.today.date),editVersion=useRef(row.today_version);
 useEffect(()=>{onExitState(row.activity.id,{dirty:draft!==null&&draft!==formatAtoms(row.today.amount_atoms),busy:saving||!!uncertain.current});},[draft,saving,row.activity.id,row.today.amount_atoms,onExitState]);
 useEffect(()=>()=>onExitState(row.activity.id,null),[row.activity.id,onExitState]);
 const begin=()=>{if(disabled||saving)return;editDate.current=row.today.date;editVersion.current=row.today_version;setError('');setDraft(formatAtoms(row.today.amount_atoms));};
 const finish=async()=>{
  if(draft===null||inFlight.current)return;inFlight.current=true;setSaving(true);setError('');
  try{
   const target=atoms(draft);if(!uncertain.current&&target===row.today.amount_atoms){setDraft(null);return;}
   if(!uncertain.current&&BigInt(target)%BigInt(row.activity.config.step_atoms)!==0n)throw new Error(`数量须为每次记录量 ${formatAtoms(row.activity.config.step_atoms)} 的整数倍。`);
   if(!uncertain.current&&!await invoke<boolean>('confirm_amount',{name:row.activity.config.name,before:formatAtoms(row.today.amount_atoms),after:formatAtoms(target),unit:unit(row.activity.config),date:editDate.current})){setDraft(null);return;}
   const request=uncertain.current??{id:crypto.randomUUID(),method:'count.set_today',params:{activityId:row.activity.id,date:editDate.current,amount:formatAtoms(target),confirmed:true,expectedVersion:editVersion.current}};
   try{await send(request);uncertain.current=null;setDraft(null);onSaved();}catch(e){uncertain.current=e instanceof TransportError?request:null;throw e;}
  }catch(e){setError((e as Error).message);}finally{inFlight.current=false;setSaving(false);}
 };
 return <div className="today-amount-editor" onDoubleClick={e=>e.stopPropagation()}>
  <p className="today-amount">{draft===null?<span className="editable-amount" role="button" tabIndex={disabled?-1:0} aria-label={`编辑${row.activity.config.name}今日数量`} aria-disabled={disabled} onDoubleClick={begin} onKeyDown={e=>{if(e.key==='Enter'||e.key==='F2'){e.preventDefault();e.stopPropagation();begin();}}}>{formatAtoms(row.today.amount_atoms)}</span>:<input autoFocus aria-label="今日数量" inputMode="decimal" value={draft} disabled={saving||!!uncertain.current} onClick={e=>e.stopPropagation()} onChange={e=>setDraft(e.target.value)} onBlur={()=>void finish()} onKeyDown={e=>{e.stopPropagation();if(e.key==='Enter'){e.preventDefault();e.currentTarget.blur();}if(e.key==='Escape'&&!uncertain.current){setDraft(null);setError('');}}}/>} <span>{unit(row.activity.config)}</span></p>
  {saving&&<small role="status">等待确认或保存…</small>}{error&&<small className="error" role="alert">{error}</small>}{uncertain.current&&<button type="button" disabled={saving||disabled} onClick={e=>{e.stopPropagation();void finish();}}>核对并重试保存</button>}
 </div>;
}
