import {Gallery,type ActivationControl} from './Gallery';
import {Monitor} from 'lucide-react';
import {useCallback,useEffect,useRef,useState} from 'react';import {rpc,restart,subscribe} from './api';
import {Settings} from './Settings';
import {Matrix,Statistics} from './Statistics';
import {Editor} from './Editor';import {Thumbnail} from './Thumbnail';import {TopBar} from './TopBar';
import {type Activity,type Row,type Snapshot} from './model';
import {readTheme,saveTheme} from './theme';
import {invoke} from '@tauri-apps/api/core';
import {TrainingGroups} from './TrainingGroups';
import {unit} from './model';
import {TodayAmount} from './TodayAmount';
export function App(){
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);const [connection,setConnection]=useState({connected:false,message:'正在连接记录服务…'});
 const [view,setView]=useState<'gallery'|'archive'|'editor'|'detail'|'matrix'|'settings'>('gallery');const [selected,setSelected]=useState<string|null>(null);const [editing,setEditing]=useState<Row|null>(null);
 const [maintenance,setMaintenance]=useState(false);
 const [detailDate,setDetailDate]=useState<string|null>(null);const [light,setLight]=useState(()=>readTheme()==='light');
 useEffect(()=>{const theme=light?'light':'dark';document.documentElement.dataset.theme=theme;saveTheme(theme);},[light]);
 const [error,setError]=useState('');const [busy,setBusy]=useState(false);const refreshTimer=useRef<ReturnType<typeof setTimeout>|null>(null);const toggles=useRef(new Set<string>());
 const editorExit=useRef({dirty:false,busy:false}),amountExit=useRef(new Map<string,{dirty:boolean;busy:boolean}>()),exitPrompt=useRef(false),operation=useRef(false);
 operation.current=busy||maintenance;
 const editorExitState=useCallback((state:{dirty:boolean;busy:boolean})=>{editorExit.current=state;},[]);
 const amountExitState=useCallback((id:string,state:{dirty:boolean;busy:boolean}|null)=>{if(state)amountExit.current.set(id,state);else amountExit.current.delete(id);},[]);
 const detailExitState=useCallback((id:string,state:{dirty:boolean;busy:boolean}|null)=>amountExitState('detail:'+id,state),[amountExitState]);
 const navigateView=(next:typeof view)=>{if([...amountExit.current.values()].some(s=>s.busy||s.dirty)){setError('请先完成或取消正在编辑的记录，再切换页面。');return;}setView(next);};
 useEffect(()=>{
  let dispose:(()=>void)|undefined,active=true;
  void subscribe<{requestId:string;managerSession:string}>('pulse:exit-requested',async request=>{
   if(exitPrompt.current)return;exitPrompt.current=true;
   try{
    const states=[editorExit.current,...amountExit.current.values()];
    let allow=!operation.current&&!states.some(s=>s.busy);
    if(!allow)setError('正在保存、等待确认或核对结果，请完成后再完全退出。');
    else if(states.some(s=>s.dirty))allow=await invoke<boolean>('confirm_exit');
    if(allow&&(operation.current||editorExit.current.busy||[...amountExit.current.values()].some(s=>s.busy)))allow=false;
    await rpc('host.exit_reply',{...request,allow});
   }catch(e){setError(String(e));}finally{exitPrompt.current=false;}
  }).then(fn=>{if(active)dispose=fn;else fn();});
  return()=>{active=false;dispose?.();};
 },[]);
 const refresh=()=>rpc<Snapshot>('state.snapshot').then(setSnapshot).catch(e=>setError(e.message));
 useEffect(()=>{let active=true;const disposers:(()=>void)[]=[];
  const add=<T,>(event:string,handler:(value:T)=>void)=>subscribe<T>(event,v=>{if(active)handler(v);}).then(fn=>{if(active)disposers.push(fn);else fn();});
  Promise.all([add<Snapshot>('pulse:snapshot',setSnapshot),add<{active:boolean}>('pulse:maintenance',v=>setMaintenance(v.active)),add<{connected:boolean;message?:string}>('pulse:connection',v=>setConnection({connected:v.connected,message:v.message??''})),add<{activityId:string|null}>('pulse:changed',event=>{if(!event.activityId){void refresh();return;}rpc<{row:Row;date:string;revision:string}>('state.activity',{activityId:event.activityId}).then(v=>{if(active)setSnapshot(s=>s?{...s,date:v.date,revision:v.revision,activities:s.activities.some(r=>r.activity.id===v.row.activity.id)?s.activities.map(r=>r.activity.id===v.row.activity.id?v.row:r):[...s.activities,v.row]}:s);}).catch(()=>{if(active)void refresh();});})])
   .then(()=>rpc<Snapshot>('state.snapshot')).then(v=>{if(active){setSnapshot(v);setView(v.settings.last_view==='matrix'?'matrix':'gallery');setConnection({connected:true,message:''});}}).catch(e=>{if(active)setConnection({connected:false,message:e.message});});
  return()=>{active=false;disposers.forEach(fn=>fn());if(refreshTimer.current)clearTimeout(refreshTimer.current);};
 },[]);
 const rows=(snapshot?.activities??[]).filter(r=>view==='archive'?r.activity.archived_at_ms!==null:r.activity.archived_at_ms===null);
 const row=(view==='detail'?snapshot?.activities:rows)?.find(r=>r.activity.id===selected)??null;
 async function change(method:string,params:unknown){setBusy(true);setError('');try{await rpc(method,params);await refresh();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function removeArchived(){
  if(!row||view!=='archive'||busy)return;const target=row.activity;setBusy(true);setError('');
  try{if(!await invoke<boolean>('confirm_delete',{name:target.config.name}))return;await rpc('activity.delete',{activityId:target.id,confirmed:true});setSelected(null);setDetailDate(null);await refresh();}
  catch(e){setError((e as Error).message);}finally{setBusy(false);}
 }
 async function toggle(r:Row){if(!connection.connected||toggles.current.has(r.activity.id)||r.activity.archived_at_ms!==null)return;toggles.current.add(r.activity.id);try{await change('desktop.set_visible',{activityId:r.activity.id,visible:!r.desktop?.visible});}finally{toggles.current.delete(r.activity.id);}}
 const openDetail=(id:string,date?:string)=>{setSelected(id);setDetailDate(date??snapshot?.date??null);setView('detail');};
 const begin=(r:Row|null)=>{setEditing(r);setView('editor');setError('');};const saved=(a:Activity)=>{setSelected(a.id);setView('gallery');void refresh();};
 const renderCard=(r:Row,copy='main',activation?:ActivationControl)=><article key={r.activity.id} tabIndex={0} className={'orb-card gallery-card '+(selected===r.activity.id?'selected':'')} aria-label={r.activity.config.name}
  onClick={()=>setSelected(r.activity.id)} onFocus={()=>setSelected(r.activity.id)} onDoubleClick={()=>void toggle(r)}
  onKeyDown={e=>{if(e.key==='Enter'&&e.target===e.currentTarget){e.preventDefault();openDetail(r.activity.id);}}}>
  {activation&&<button type="button" className={'activation-dot'+(activation.active?' is-on':'')} aria-pressed={activation.active} aria-label={activation.active?'移出激活栏':'加入激活栏'} title={activation.active?'移出激活栏':'加入激活栏'} disabled={activation.disabled}
   onClick={e=>{e.stopPropagation();activation.toggle();}} onDoubleClick={e=>e.stopPropagation()} onKeyDown={e=>e.stopPropagation()}><span/></button>}
  {view==='archive'?<span className="archived-mark">已归档</span>:<button type="button" className={'desktop-toggle'+(r.desktop?.visible?' is-on':'')} aria-pressed={!!r.desktop?.visible} aria-label={r.desktop?.visible?'从桌面隐藏':'显示到桌面'} title={r.desktop?.visible?'从桌面隐藏':'显示到桌面'} disabled={busy||!connection.connected||activation?.disabled}
   onClick={e=>{e.stopPropagation();void toggle(r);}} onDoubleClick={e=>e.stopPropagation()} onKeyDown={e=>e.stopPropagation()}><Monitor size={18} strokeWidth={1.7}/></button>}
  <div className="card-main"><div className="card-orb-stage"><Thumbnail activity={r.activity}/></div><div className="card-copy"><h2 title={r.activity.config.name}>{r.activity.config.name}</h2>
   <TodayAmount row={r} disabled={view==='archive'||busy||!connection.connected||!!activation?.disabled} onSaved={()=>void refresh()} onExitState={(id,state)=>amountExitState(copy+':'+id,state)}/>
  </div></div>
  <div className="card-summary-slot"><TrainingGroups groups={r.today_groups??[]} unit={unit(r.activity.config)} compact/></div>
 </article>;
 return <div className="app-shell"><TopBar view={view} onView={navigateView} light={light} onTheme={()=>setLight(v=>!v)} connected={connection.connected} onError={setError}/>
  {maintenance&&<div className="maintenance-overlay" role="status">正在恢复备份，请等待；暂不接受新增记录。</div>}<main className={'workspace '+(view==='gallery'?'gallery-workspace':'')} inert={maintenance}>{!connection.connected&&<div className="connection-banner" role="status"><div><strong>等待记录服务</strong><p>{connection.message}</p></div><button onClick={()=>{void restart().catch(e=>setError(String(e)));}}>重新启动 Host</button></div>}{error&&<div className="error-banner" role="alert">{error}<button aria-label="关闭提示" onClick={()=>setError('')}>×</button></div>}
  {view==='settings'&&snapshot?<Settings snapshot={snapshot} onRefresh={refresh}/>:view==='editor'?<Editor key={editing?.activity.id??'new'} rows={snapshot?.activities??[]} editing={editing} onDone={saved} onExitState={editorExitState} onCancel={()=>{setView('gallery');void refresh();}}/>:view==='detail'&&row?<Statistics key={row.activity.id} row={row} today={snapshot!.date} initialDate={detailDate??snapshot!.date} revision={snapshot!.revision} light={light} onBack={()=>setView('gallery')} onEdit={()=>begin(row)} onExitState={detailExitState}/>:view==='matrix'&&snapshot?<Matrix rows={rows} date={snapshot.date} revision={snapshot.revision} light={light} onSelect={openDetail}/>:
   <><div className="page-title gallery-heading"><div className="gallery-title"><h1>{view==='archive'?'已归档星球':'我的星球'}</h1><span className="muted">{rows.length} 颗星球</span></div><div className="actions"><button disabled={!row||busy||!connection.connected} onClick={()=>{if(row)openDetail(row.activity.id);}}>查看详情</button><button disabled={!row||busy||!connection.connected} onClick={()=>begin(row)}>编辑</button><button disabled={!row||busy||!connection.connected} onClick={()=>{if(row)void change(view==='archive'?'activity.restore':'activity.archive',{activityId:row.activity.id});}}>{view==='archive'?'恢复':'归档'}</button>{view==='archive'&&<button disabled={!row||busy||!connection.connected} onClick={()=>void removeArchived()}>删除</button>}<button className="primary" disabled={!connection.connected} onClick={()=>begin(null)}>＋ 创建星球</button></div></div>
   {snapshot?.temporary_hidden&&<div className="info-banner">桌面星球已由托盘临时隐藏；卡片标记仍表示你保存的显示设置。</div>}
   {view==='archive'?<div className="orb-gallery">{rows.map(r=>renderCard(r))}</div>:<Gallery rows={rows} revision={snapshot?.revision??'0'} disabled={busy||!connection.connected} renderCard={renderCard} onError={setError}/>}
   {rows.length===0&&<div className="empty-state"><span className="empty-orb"/><h2>{view==='archive'?'暂无归档':'从第一颗星球开始'}</h2><p>{view==='archive'?'归档的星球会保留在这里。':'创建一个记录条目，再选择是否放到桌面。'}</p>{view!=='archive'&&<button className="primary" disabled={!connection.connected} onClick={()=>begin(null)}>创建星球</button>}</div>}</>}
  </main></div>;
}
