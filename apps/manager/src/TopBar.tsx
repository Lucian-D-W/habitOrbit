import {useEffect,useState} from 'react';
import {isTauri} from '@tauri-apps/api/core';
import {getCurrentWindow} from '@tauri-apps/api/window';
import {Circle,Archive,CalendarDays,Settings,Sun,Moon,Minus,Square,Copy,X} from 'lucide-react';

type View='gallery'|'archive'|'editor'|'detail'|'matrix'|'settings';
export function TopBar({view,onView,light,onTheme,connected,onError}:{view:View;onView:(view:View)=>void;light:boolean;onTheme:()=>void;connected:boolean;onError:(message:string)=>void}){
 const native=isTauri(),[maximized,setMaximized]=useState(false);
 useEffect(()=>{
  if(!native)return;const window=getCurrentWindow();let active=true,dispose:(()=>void)|undefined;
  const refresh=()=>window.isMaximized().then(v=>{if(active)setMaximized(v);}).catch(()=>{});
  void refresh();void window.onResized(()=>{void refresh();}).then(fn=>{if(active)dispose=fn;else fn();}).catch(()=>{});
  return()=>{active=false;dispose?.();};
 },[native]);
 function control(action:'minimize'|'maximize'|'close'){
  const window=getCurrentWindow();const result=action==='minimize'?window.minimize():action==='maximize'?window.toggleMaximize():window.close();
  void result.catch(error=>onError(`窗口操作失败：${String(error)}`));
 }
 const pages=[{id:'gallery' as const,label:'星球总览',Icon:Circle},{id:'matrix' as const,label:'记录矩阵',Icon:CalendarDays},{id:'archive' as const,label:'归档',Icon:Archive}];
 return <header className="topbar">
  <nav aria-label="页面">{pages.map(p=><button key={p.id} className={'icon-button '+(view===p.id?'active':'')} aria-label={p.label} title={p.label} aria-current={view===p.id?'page':undefined} onClick={()=>onView(p.id)}><p.Icon size={19} strokeWidth={1.7}/></button>)}</nav>
  <div className="titlebar-drag" data-tauri-drag-region/>
  <span className={'connection-dot '+(connected?'online':'')} role="status" aria-label={connected?'记录服务已连接':'记录服务未连接'} title={connected?'记录服务已连接':'记录服务未连接'}/>
  <button className={'icon-button '+(view==='settings'?'active':'')} title="设置与备份" aria-label="设置与备份" aria-current={view==='settings'?'page':undefined} onClick={()=>onView('settings')}><Settings size={19} strokeWidth={1.7}/></button>
  <button className="icon-button" title={light?'切换到深夜':'切换到白天'} aria-label={light?'切换到深夜':'切换到白天'} onClick={onTheme}>{light?<Moon size={19}/>:<Sun size={19}/>}</button>
  {native&&<div className="window-controls"><button title="最小化" aria-label="最小化" onClick={()=>control('minimize')}><Minus size={16}/></button><button title={maximized?'还原':'最大化'} aria-label={maximized?'还原':'最大化'} onClick={()=>control('maximize')}>{maximized?<Copy size={14}/>:<Square size={14}/>}</button><button className="window-close" title="关闭管理窗口" aria-label="关闭管理窗口" onClick={()=>control('close')}><X size={17}/></button></div>}
 </header>;
}
