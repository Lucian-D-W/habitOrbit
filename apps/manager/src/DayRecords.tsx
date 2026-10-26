import {useCallback,useEffect,useRef,useState,type CSSProperties} from 'react';
import {BeadAxis} from './BeadAxis';
import {rpc,send,TransportError,type Request} from './api';
import {formatAtoms,unit,type DayRecording,type Row} from './model';
import {beadWindow,beadWindowSize,changeCut,recordingSegments} from './day-recording';
import './day-recording.css';

type ExitState={dirty:boolean;busy:boolean}|null;
export function DayRecords({row,today,revision,onExitState}:{row:Row;today:string;revision:string;onExitState:(id:string,state:ExitState)=>void}){
 const [data,setData]=useState<DayRecording|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false),[uncertain,setUncertain]=useState<Request|null>(null);
 const [draft,setDraft]=useState<string[]|null>(null),[page,setPage]=useState(0n),[jump,setJump]=useState('');
 const alive=useRef(true),sequence=useRef(0),inFlight=useRef(false),current=useRef(data),drag=useRef<{from:string;version:string;cuts:string[]}|null>(null);
 current.current=data;
 const u=unit(row.activity.config),disabled=busy||!!uncertain||row.activity.archived_at_ms!==null;
 const load=useCallback(async()=>{
  const token=++sequence.current;
  try{const next=await rpc<DayRecording>('recording.get',{activityId:row.activity.id,date:today});if(alive.current&&token===sequence.current){setData(next);return next;}}
  catch(e){if(alive.current&&token===sequence.current)setError((e as Error).message);}
 },[row.activity.id,today]);
 useEffect(()=>{alive.current=true;return()=>{alive.current=false;sequence.current++;};},[]);
 useEffect(()=>{void load();},[load,revision,row.today_version]);
 useEffect(()=>{setData(null);setPage(0n);drag.current=null;setDraft(null);},[today]);
 useEffect(()=>{if(drag.current&&data?.version!==drag.current.version&&!inFlight.current){drag.current=null;setDraft(null);setError('记录已更新，已取消旧的拖动预览。');}},[data?.version]);
 useEffect(()=>{onExitState('groups',{dirty:!!draft,busy:busy||!!uncertain});return()=>onExitState('groups',null);},[draft,busy,uncertain,onExitState]);
 async function execute(request:Request){
  if(inFlight.current)return;inFlight.current=true;setBusy(true);setError('');
  try{await send(request);if(alive.current){setUncertain(null);setDraft(null);}await load();}
  catch(e){if(alive.current){if(e instanceof TransportError)setUncertain(request);else{setUncertain(null);setDraft(null);await load();}setError((e as Error).message);}}
  finally{inFlight.current=false;if(alive.current)setBusy(false);}
 }
 function commit(cuts:string[],version=data?.version){
  if(!data||disabled||!version)return;setDraft(cuts);
  void execute({id:crypto.randomUUID(),method:'recording.set_groups',params:{activityId:row.activity.id,date:today,expectedVersion:version,cuts}});
 }
 function place(position:string,from:string|null=null){
  if(!data||disabled)return;const next=changeCut(data.cuts,from,position,data.units);if(!next){setError('这里已有隔片，或已到珠串端点。');return;}
  commit(next);
 }
 function move(position:string,direction:bigint){const next=(BigInt(position)+direction).toString();if(data&&changeCut(data.cuts,position,next,data.units)){setPage((BigInt(next)-1n)/beadWindowSize*beadWindowSize);place(next,position);}}
 function remove(position:string){if(data){commit(data.cuts.filter(c=>c!==position));}}
 const window=data?beadWindow(data,page):null,segments=data?recordingSegments(data,draft??data.cuts):[];
 const style={'--bead-one':row.activity.config.visual.palette[0],'--bead-two':row.activity.config.visual.palette[1],'--bead-accent':row.activity.config.visual.accent_color} as CSSProperties;
 return <div className="day-records" style={style}>
  {error&&<p className="error" role="alert">{error}</p>}{busy&&<p role="status" className="hint">正在保存…</p>}{uncertain&&<div role="status"><p>连接中断，结果待核对。核对前暂不接受新操作。</p><button type="button" disabled={busy} onClick={()=>void execute(uncertain)}>核对并重试</button></div>}
  {data?.runs.some(run=>run.amount_atoms!==row.activity.config.step_atoms)&&<p className="hint">旧记录保留原份量，悬停珠子可查看实际量。</p>}
  {!data?<p className="muted">正在载入当日记录…</p>:data.units==='0'?<p className="muted">今天还没有记录。先通过星球记录数量，再在这里分组。</p>:window&&<>
   <BeadAxis beads={window.beads} units={data.units} cuts={data.cuts} previewCuts={draft??data.cuts} version={data.version} unit={u} disabled={disabled}
    onAdd={at=>place(at)} onRemove={remove} onMove={move}
    onStart={from=>{drag.current={from,version:data.version,cuts:data.cuts};setDraft(data.cuts);}}
    onPreview={(from,to)=>{const active=drag.current;if(active&&current.current?.version===active.version)setDraft(changeCut(active.cuts,from,to,data.units)??active.cuts);}}
    onFinish={(from,to)=>{const active=drag.current;drag.current=null;setDraft(null);if(!active||!to||to===from||current.current?.version!==active.version)return;const next=changeCut(active.cuts,from,to,data.units);if(next){commit(next,active.version);}}}/>
   {window.units>beadWindowSize&&<div className="bead-pages"><button disabled={window.start===0n||disabled} onClick={()=>setPage(window.start-beadWindowSize)}>上一段珠串</button><span>第 {window.start+1n+''}–{window.end+''} 颗 / 共 {window.units+''} 颗</span><button disabled={window.end===window.units||disabled} onClick={()=>setPage(window.end)}>下一段珠串</button><label>定位珠子<input aria-label="定位珠子序号" inputMode="numeric" value={jump} onChange={e=>setJump(e.target.value)}/></label><button disabled={disabled} onClick={()=>{if(/^\d+$/.test(jump)&&BigInt(jump)>0n&&BigInt(jump)<=window.units)setPage((BigInt(jump)-1n)/beadWindowSize*beadWindowSize);else setError('请输入珠串范围内的序号。');}}>定位</button></div>}
   <div className="bead-segments" aria-label="各段数量" aria-live="polite">{segments.map((segment,i)=><div key={segment.start}><span>第 {i+1} 段</span><strong>{formatAtoms(segment.amount_atoms)} <small>{u}</small></strong><small>{segment.count} 颗</small></div>)}</div>
   <p className="hint">左键点击轴添加隔片，右键点击隔片删除；拖动隔片调整分组，珠子自动向左吸附。换行不分组。选中隔片可用方向键移动、Delete 删除。</p>
  </>}
 </div>;
}
