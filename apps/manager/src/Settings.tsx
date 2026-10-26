import {useEffect,useState} from 'react';import {invoke} from '@tauri-apps/api/core';import {rpc,subscribe} from './api';import type {Snapshot} from './model';
import {FontPicker} from './fonts';
export function Settings({snapshot,onRefresh}:{snapshot:Snapshot;onRefresh:()=>Promise<unknown>}){
 const [busy,setBusy]=useState(false),[message,setMessage]=useState(''),[error,setError]=useState(''),[environment,setEnvironment]=useState<{data_directory:string;physical_data_directory:string;packaged:boolean}|null>(null);
 useEffect(()=>{rpc<typeof environment>('environment.info').then(setEnvironment).catch(e=>setError(e.message));let dispose:(()=>void)|undefined;let active=true;subscribe<{active:boolean;message:string}>('pulse:maintenance',e=>{if(active){setBusy(e.active);setMessage(e.message);}}).then(fn=>{if(active)dispose=fn;else fn();});return()=>{active=false;dispose?.();};},[]);
 async function setting(params:unknown){setBusy(true);setError('');try{await rpc('settings.update',params);await onRefresh();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function backup(restore:boolean){setBusy(true);setError('');setMessage('');try{const path=await invoke<string|null>('backup_file',{save:!restore});if(!path)return;if(restore){if(!await invoke<boolean>('confirm_restore',{path}))return;setMessage('正在恢复，请等待；暂不接受新增记录。');const result=await rpc<{before_restore_backup:string}>('backup.restore',{path,confirmed:true});setMessage(`恢复完成。恢复前副本：${result.before_restore_backup}`);await onRefresh();}else{await rpc('backup.export',{path});setMessage(`备份已保存：${path}`);}}catch(e){setError(`${(e as Error).message}；若连接中断，请先核对当前记录及backups目录，不自动再次恢复。`);}finally{setBusy(false);}}
 return <section className="settings-page">
  <div className="page-title"><h1>设置与备份</h1></div>
  {error&&<p className="error-banner" role="alert">{error}</p>}
  {message&&<p className="info-banner" role="status">{message}</p>}
  <div className="settings-grid">
   <div className="panel settings-card">
    <h2>运行设置</h2>
    <label className="settings-toggle"><input type="checkbox" checked={snapshot.settings.start_with_windows} disabled={busy} onChange={e=>void setting({startWithWindows:e.target.checked})}/>登录 Windows 时启动</label>
    <div className="settings-fields">
     <label>动画<select value={snapshot.settings.animation_mode} disabled={busy} onChange={e=>void setting({animationMode:e.target.value})}><option value="normal">流畅</option><option value="economy">节能</option></select></label>
     <label>启动页面<select value={snapshot.settings.last_view} disabled={busy} onChange={e=>void setting({lastView:e.target.value})}><option value="gallery">星球总览</option><option value="matrix">记录矩阵</option></select></label>
    </div>
   </div>
   <div className="panel settings-card">
    <h2>备份与恢复</h2>
    <div className="actions"><button disabled={busy} onClick={()=>void backup(false)}>导出备份</button><button disabled={busy} onClick={()=>void backup(true)}>恢复备份…</button></div>
   </div>
   <FontPicker/>
   <div className="panel settings-card settings-data">
    <h2>资料位置</h2>
    <p className="data-path">{environment?.data_directory??'正在读取…'}</p>
    <details><summary>诊断信息</summary><p className="data-path">物理位置：{environment?.physical_data_directory??'正在读取…'}</p><p>运行方式：{environment?(environment.packaged?'MSIX':'非 MSIX'):'正在读取…'}</p></details>
   </div>
  </div>
 </section>;
}
