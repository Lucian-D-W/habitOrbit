export type OrbEffects = {glow_color:string|null;glow_intensity:number;glow_scale:number;highlight_intensity:number;flow_speed:number;breath_amplitude:number;particle_color:string;show_particles:boolean};
export const defaultEffects:OrbEffects={glow_color:null,glow_intensity:1,glow_scale:2,highlight_intensity:1,flow_speed:1,breath_amplitude:1,particle_color:'#FFFFFF',show_particles:true};
export type Visual = { renderer_version:number; family:string; base_preset:string; palette:string[]; accent_color:string; highlight:{x:number;y:number}; interior_style:string; decorative_seed:number;effects?:OrbEffects|null };
export type Config = {name:string;icon_id:string;description:string;kind:'count'|'duration';unit:string;step_atoms:string;group_gap_ms:string|null;breaks:{b1:string;b2:string;b3:string};visual:Visual};
export type Activity = {id:string;config:Config;visual_key:string;created_at_ms:string;created_local_date:string;updated_at_ms:string;archived_at_ms:string|null};
export type Daily = {date:string;amount_atoms:string;recorded_units:string;level:number|null;availability:'in_scope'|'not_applicable'};
export type Row = {activity:Activity;today:Daily;today_groups?:Group[];today_version?:string;desktop:{visible:boolean;monitor:string;center_x_dip:number;center_y_dip:number}|null;has_history:boolean};
export type Snapshot = {date:string;revision:string;activities:Row[];settings:{animation_mode:string;start_with_windows:boolean;last_view:string};temporary_hidden:boolean};
export type Thumbnail = {png:string;cache_key:string;visual_key:string;renderer_version:number};
export type Event = {event_no:string;id:string;recorded_at_ms:string;local_date:string;utc_offset_minutes:number;delta_steps:number;delta_amount_atoms:string;reverses_id:string|null};
export type Group = {first_event_no:string;last_event_no:string;amount_atoms:string;recorded_units:string};
export type DayDetail = {summary:Daily;groups:Group[];events:Event[];event_count:number;offset:number;has_more:boolean};
export type PortionRun={start:string;count:string;amount_atoms:string;event_no:string};
export type DayRecording={date:string;amount_atoms:string;units:string;step_atoms:string;version:string;cuts:string[];runs:PortionRun[];groups:Group[];grouped:boolean;manual:boolean};

export function atoms(text:string):string {
  text=text.trim();if(!/^\d+(?:\.\d{1,3})?$/.test(text))throw new Error('请输入最多三位小数的十进制数量。');
  const [whole,fraction='']=text.split('.');const value=BigInt(whole)*1000n+BigInt(fraction.padEnd(3,'0'));
  if(value>9223372036854775807n)throw new Error('数量超出支持范围。');return value.toString();
}
export function formatAtoms(value:string):string {
  const amount=BigInt(value);const a=amount<0n?-amount:amount;const fraction=(a%1000n).toString().padStart(3,'0').replace(/0+$/,'');
  return `${amount<0n?'-':''}${a/1000n}${fraction?'.'+fraction:''}`;
}
export const unit=(config:Config)=>config.kind==='duration'?'分钟':config.unit;
export const visualSignature=(visual:Visual)=>{
 const effects={...defaultEffects,...visual.effects,glow_intensity:1,glow_scale:2,glow_color:visual.effects?.glow_color?.toUpperCase()??null,particle_color:(visual.effects?.particle_color??defaultEffects.particle_color).toUpperCase()};
 return JSON.stringify({family:visual.family,palette:visual.palette.map(c=>c.toUpperCase()),accent:visual.accent_color.toUpperCase(),
  highlight:{x:Math.round(visual.highlight.x*1000)/1000,y:Math.round(visual.highlight.y*1000)/1000},interior:visual.interior_style,
  ...(JSON.stringify(effects)!==JSON.stringify(defaultEffects)?{effects}:{})});
};
export const presets:Visual[]=[
 {renderer_version:1,family:'glass-orb',base_preset:'mystic',palette:['#6B5CE7','#508FEF','#DF96F0'],accent_color:'#8B7AEC',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:1842},
 {renderer_version:1,family:'glass-orb',base_preset:'nature',palette:['#45B5C6','#40D7B5','#ADFFCC'],accent_color:'#43DAAA',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:22},
 {renderer_version:1,family:'glass-orb',base_preset:'sunset',palette:['#FA5667','#F28C4E','#FFF5A7'],accent_color:'#FFA04B',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:33},
 {renderer_version:1,family:'glass-orb',base_preset:'minimal',palette:['#A8ADB9','#F8FAFC','#D2D6DF'],accent_color:'#BFC6D4',highlight:{x:.30,y:.25},interior_style:'minimal',decorative_seed:44},
 {renderer_version:1,family:'glass-orb',base_preset:'ocean',palette:['#247ACD','#4ED6E8','#92EFFF'],accent_color:'#46CDEB',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:55},
 {renderer_version:1,family:'glass-orb',base_preset:'cosmic',palette:['#6489D9','#F05C9B','#E5A8FA'],accent_color:'#EA82CB',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:66},
 {renderer_version:1,family:'glass-orb',base_preset:'fire',palette:['#F7CE35','#FFA733','#F2784B'],accent_color:'#F5B333',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:77},
 {renderer_version:1,family:'glass-orb',base_preset:'arctic',palette:['#4A99EF','#DDF9FF','#FFFFFF'],accent_color:'#AFDEF6',highlight:{x:.30,y:.25},interior_style:'flowing',decorative_seed:88},
 {renderer_version:1,family:'glass-orb',base_preset:'shadow',palette:['#343638','#747779','#26282B'],accent_color:'#93979C',highlight:{x:.30,y:.25},interior_style:'minimal',decorative_seed:99},
];
function hsl(h:number,s:number,l:number):string {
 const a=s*Math.min(l,1-l);const f=(n:number)=>{const k=(n+h/30)%12;return l-a*Math.max(-1,Math.min(k-3,9-k,1));};
 return '#'+[0,8,4].map(n=>Math.round(255*f(n)).toString(16).padStart(2,'0')).join('').toUpperCase();
}
export function initialVisual(existing:Row[]):Visual {
 const used=new Set(existing.map(r=>visualSignature(r.activity.config.visual)));
 const preset=presets.find(p=>!used.has(visualSignature(p)));if(preset)return structuredClone(preset);
 for(let i=existing.length;i<existing.length+10000;i++){
  const h=(i*137.508)%360;const visual={...structuredClone(presets[0]),palette:[hsl(h,.65,.52),hsl((h+42)%360,.6,.6),hsl((h+80)%360,.8,.78)],accent_color:hsl(h,.65,.65)};
  if(!used.has(visualSignature(visual)))return visual;
 }
 throw new Error('请手动调整配色后创建。');
}
export function newConfig(existing:Row[]):Config {return {name:'',icon_id:'book-open',description:'',kind:'count',unit:'次',step_atoms:'1000',group_gap_ms:null,breaks:{b1:'5000',b2:'10000',b3:'20000'},visual:initialVisual(existing)};}
export function shiftDate(date:string,days:number):string {const d=new Date(date+'T12:00:00Z');d.setUTCDate(d.getUTCDate()+days);return d.toISOString().slice(0,10);}
