import {createContext,useContext,useEffect,useState,type ReactNode} from 'react';
import {rpc} from './api';
export const fontKey='pulse.manager.font';
export const fallbackFont='"Segoe UI", "Microsoft YaHei", sans-serif';
export function readFont(){try{return localStorage.getItem(fontKey)??'';}catch{return '';}}
export function fontStack(name:string){return name?`${JSON.stringify(name)}, ${fallbackFont}`:fallbackFont;}
export function availableFont(name:string,fonts:string[]){return fonts.includes(name)?name:'';}
const FontContext=createContext({family:'',stack:fallbackFont,fonts:[] as string[],loading:true,error:'',missing:false,select:(_name:string)=>{},reload:()=>{}});
export function FontProvider({children}:{children:ReactNode}){
 const [family,setFamily]=useState(''),[fonts,setFonts]=useState<string[]>([]),[loading,setLoading]=useState(true),[error,setError]=useState(''),[missing,setMissing]=useState(false),[revision,setRevision]=useState(0);
 useEffect(()=>{let active=true;setLoading(true);setError('');rpc<string[]>('environment.fonts').then(list=>{if(!active)return;const saved=readFont(),selected=availableFont(saved,list);setFonts(list);setFamily(selected);setMissing(!!saved&&!selected);}).catch(e=>{if(active)setError((e as Error).message);}).finally(()=>{if(active)setLoading(false);});return()=>{active=false;};},[revision]);
 const stack=fontStack(family);
 useEffect(()=>{document.documentElement.style.fontFamily=stack;},[stack]);
 function select(name:string){const next=availableFont(name,fonts);setFamily(next);setMissing(false);try{localStorage.setItem(fontKey,next);}catch{/* keep the session usable */}}
 return <FontContext.Provider value={{family,stack,fonts,loading,error,missing,select,reload:()=>setRevision(v=>v+1)}}>{children}</FontContext.Provider>;
}
export function useFont(){return useContext(FontContext);}
export function FontPicker(){
 const font=useFont(),[search,setSearch]=useState('');
 const matches=font.fonts.filter(name=>name.toLocaleLowerCase().includes(search.toLocaleLowerCase()));
 return <div className="panel settings-card font-card"><h2>界面字体</h2><div className="settings-fields"><label>搜索字体<input type="search" value={search} onChange={e=>setSearch(e.target.value)} placeholder="字体名称"/></label><label>本机字体<select value={font.family} disabled={font.loading} onChange={e=>font.select(e.target.value)}><option value="">系统默认</option>{font.family&&!matches.includes(font.family)&&<option>{font.family}</option>}{matches.map(name=><option key={name}>{name}</option>)}</select></label></div><p className="font-sample">把日常，轻轻记下来。<br/>星以为常 habitOrbit · 0123456789</p>{font.loading&&<p role="status">正在读取本机字体…</p>}{font.missing&&<p role="status">原字体已不可用，当前使用系统默认。</p>}{font.error&&<p role="alert">字体列表读取失败。<button onClick={font.reload}>重试</button></p>}</div>;
}
