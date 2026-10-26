import {useEffect,useLayoutEffect,useRef,useState,type PointerEvent} from 'react';
import {axisCutAt,axisLayout,axisRowHeight,type AxisPoint} from './bead-axis';
import {formatAtoms} from './model';

type Props={beads:{index:string;amount_atoms:string}[];units:string;cuts:string[];previewCuts:string[];version:string;unit:string;disabled:boolean;
 onAdd:(at:string)=>void;onRemove:(at:string)=>void;onMove:(at:string,direction:bigint)=>void;
 onStart:(at:string)=>void;onPreview:(from:string,to:string)=>void;onFinish:(from:string,to:string|null)=>void};
export function BeadAxis(props:Props){
 const surface=useRef<HTMLDivElement>(null),[width,setWidth]=useState(0),[active,setActive]=useState<string|null>(null),[selected,setSelected]=useState<string|null>(null);
 const gesture=useRef<{id:number;from:string;to:string;version:string;x:number;y:number;moved:boolean}|null>(null);
 const latest=useRef(props);latest.current=props;
 const indices=props.beads.map(b=>b.index),layout=axisLayout(indices,props.previewCuts,width);
 useLayoutEffect(()=>{const el=surface.current;if(!el)return;setWidth(el.clientWidth);const observer=new ResizeObserver(()=>setWidth(el.clientWidth));observer.observe(el);return()=>observer.disconnect();},[]);
 useEffect(()=>{if(gesture.current&&(props.version!==gesture.current.version||props.disabled)){const g=gesture.current;gesture.current=null;setActive(null);latest.current.onFinish(g.from,null);}},[props.version,props.disabled]);
 const position=(p:AxisPoint)=>({transform:`translate(${p.x}px,${p.y}px)`});
 function hit(event:PointerEvent<HTMLDivElement>,from:string|null){
  const el=surface.current;if(!el)return null;const rect=el.getBoundingClientRect(),x=event.clientX-rect.left,y=event.clientY-rect.top;
  if(x<0||x>rect.width||y<0||y>rect.height)return null;
  const other=props.cuts.filter(c=>c!==from);
  return axisCutAt(axisLayout(indices,other,width),x,y,props.units,from?other:[]);
 }
 function finish(event:PointerEvent<HTMLDivElement>,cancel=false){
  const g=gesture.current;if(!g||event.pointerId!==g.id)return;
  gesture.current=null;setActive(null);
  if(surface.current?.hasPointerCapture(g.id))surface.current.releasePointerCapture(g.id);
  const at=cancel?null:hit(event,g.from);setSelected(at??g.from);props.onFinish(g.from,g.moved?at:null);
 }
 return <div className="bead-scroll"><div ref={surface} className={'bead-axis'+(active?' axis-dragging':'')} style={{height:layout.height}} role="group" aria-label={`当日${props.units}颗珠子的分组轴`} aria-disabled={props.disabled}
  onContextMenu={e=>e.preventDefault()}
  onPointerDown={e=>{
   if(e.button!==0||props.disabled)return;
   const divider=(e.target as HTMLElement).closest<HTMLButtonElement>('[data-divider]');
   if(divider){const from=divider.dataset.divider!;e.preventDefault();surface.current?.setPointerCapture(e.pointerId);gesture.current={id:e.pointerId,from,to:from,version:props.version,x:e.clientX,y:e.clientY,moved:false};setSelected(from);setActive(from);props.onStart(from);}
   else{const at=hit(e,null);if(at&&!props.cuts.includes(at)){setSelected(at);props.onAdd(at);}}
  }}
  onPointerMove={e=>{const g=gesture.current;if(!g||g.id!==e.pointerId)return;if(Math.hypot(e.clientX-g.x,e.clientY-g.y)<3&&!g.moved)return;g.moved=true;const at=hit(e,g.from);if(at&&at!==g.to){g.to=at;props.onPreview(g.from,at);}}}
  onPointerUp={e=>finish(e)} onPointerCancel={e=>finish(e,true)} onLostPointerCapture={e=>{if(gesture.current)finish(e,true);}}
  onKeyDown={e=>{if(e.key==='Escape'&&gesture.current){const g=gesture.current;gesture.current=null;setActive(null);props.onFinish(g.from,null);}}}>
  {Array.from({length:layout.height/axisRowHeight},(_,i)=><span key={i} className="bead-axis-line" style={{top:i*axisRowHeight+20}} aria-hidden="true"/>)}
  {props.beads.map(bead=><span key={bead.index} className="axis-bead-position" style={position(layout.beads.get(bead.index)!)}><span className="recording-bead" title={`第${bead.index}颗 · ${formatAtoms(bead.amount_atoms)} ${props.unit}`} aria-label={`第${bead.index}颗，${formatAtoms(bead.amount_atoms)}${props.unit}`}/></span>)}
  {[...layout.dividers].map(([at,p])=><button key={at} type="button" data-divider={at} className={'bead-divider'+(selected===at?' selected':'')+(active&&props.previewCuts.includes(at)&&!props.cuts.filter(c=>c!==active).includes(at)?' dragging':'')} style={position(p)} disabled={props.disabled} aria-label={`第${at}颗后的隔片`} title="拖动调整分组；右键删除；方向键移动，Delete 删除"
   onContextMenu={e=>{e.preventDefault();e.stopPropagation();if(!props.disabled&&!gesture.current)props.onRemove(at);}}
   onFocus={()=>setSelected(at)} onKeyDown={e=>{if(props.disabled||gesture.current)return;if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();props.onMove(at,e.key==='ArrowLeft'?-1n:1n);}if(e.key==='Delete'||e.key==='Backspace'){e.preventDefault();props.onRemove(at);}}}><span/></button>)}
 </div></div>;
}
