import {useCallback,useEffect,useRef,useState,type ReactNode} from 'react';
import {createPortal} from 'react-dom';
import GridLayout,{useContainerWidth,type Layout} from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
export type BentoCard={id:string;title:string;content:ReactNode;wide?:boolean;column?:0|1};
function CardBody({card,onHeight}:{card:BentoCard;onHeight:(id:string,h:number)=>void}){
 const ref=useRef<HTMLDivElement>(null);
 useEffect(()=>{if(!ref.current)return;const observer=new ResizeObserver(([entry])=>onHeight(card.id,Math.ceil((entry.borderBoxSize[0].blockSize+16)/24)));observer.observe(ref.current);return()=>observer.disconnect();},[card.id,onHeight]);
 return <div ref={ref} className="bento-inner"><h2 className="bento-handle"><span aria-hidden="true">⠿</span>{card.title}</h2><div className="bento-body">{card.content}</div></div>;
}
export function Bento({cards,name,resetTarget,resetDisabled=false}:{cards:BentoCard[];name:string;resetTarget?:HTMLElement|null;resetDisabled?:boolean}){
 const {width,containerRef,mounted}=useContainerWidth();const narrow=width<900,key=`pulse.bento.${name}.${narrow?'narrow':'wide'}`;
 const [layouts,setLayouts]=useState<Record<string,Layout>>({}),[heights,setHeights]=useState<Record<string,number>>({});
 const onHeight=useCallback((id:string,h:number)=>setHeights(old=>old[id]===h?old:{...old,[id]:h}),[]);
 const defaults=cards.map((card,i)=>({i:card.id,x:narrow||card.wide?0:(card.column??i%2)*6,y:i*35,w:narrow||card.wide?12:6,h:heights[card.id]??25}));
 useEffect(()=>{try{const saved=JSON.parse(localStorage.getItem(key)??'null');if(Array.isArray(saved)&&saved.every(i=>typeof i.i==='string'&&Number.isFinite(i.x)&&Number.isFinite(i.y)&&i.x>=0&&i.x<12&&i.y>=0))setLayouts(old=>({...old,[key]:saved}));}catch{/* reset invalid preferences */}},[key]);
 const layout=defaults.map(item=>{const saved=layouts[key]?.find(x=>x.i===item.i);return {...item,x:narrow?0:Math.min(saved?.x??item.x,12-item.w),y:saved?.y??item.y};});
 function save(next:Layout){setLayouts(old=>({...old,[key]:next}));try{localStorage.setItem(key,JSON.stringify(next));}catch{/* session layout remains usable */}}
 function reset(){setLayouts(old=>({...old,[key]:[]}));try{localStorage.removeItem(key);}catch{/* session only */}}
 const resetButton=<button type="button" disabled={resetDisabled} onClick={reset}>复位布局</button>;
 return <div ref={containerRef} className="bento">{resetTarget?createPortal(resetButton,resetTarget):resetTarget===undefined?<div className="bento-toolbar">{resetButton}</div>:null}{mounted&&<GridLayout width={width} layout={layout} gridConfig={{cols:12,rowHeight:8,margin:[16,16],containerPadding:[0,0]}} dragConfig={{enabled:true,handle:'.bento-handle',cancel:'input,button,select,textarea,a'}} resizeConfig={{enabled:false}} onDragStop={save}>{cards.map(card=><div key={card.id} data-card={card.id} className="panel bento-card"><CardBody card={card} onHeight={onHeight}/></div>)}</GridLayout>}</div>;
}
