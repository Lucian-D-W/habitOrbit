import {useEffect,useRef,useState} from 'react';
import {init,use} from 'echarts/core';import {BarChart} from 'echarts/charts';import {GridComponent} from 'echarts/components';import {SVGRenderer} from 'echarts/renderers';
import {type Daily} from './model';import {useFont} from './fonts';import {barLayout} from './bar-model';
use([BarChart,GridComponent,SVGRenderer]);
export default function DailyBars({days,unit,accent,selected,onSelect,light}:{days:Daily[];unit:string;accent:string;selected:string;onSelect:(date:string)=>void;light:boolean}){
 const target=useRef<HTMLDivElement>(null),viewport=useRef<HTMLDivElement>(null),[width,setWidth]=useState(0);const {stack}=useFont();
 const layout=barLayout(days,width),windowKey=days.map(d=>d.date).join('|');
 useEffect(()=>{if(!viewport.current)return;const observer=new ResizeObserver(([entry])=>setWidth(Math.floor(entry.contentRect.width)));observer.observe(viewport.current);return()=>observer.disconnect();},[]);
 useEffect(()=>{const frame=requestAnimationFrame(()=>{if(viewport.current)viewport.current.scrollLeft=viewport.current.scrollWidth;});return()=>cancelAnimationFrame(frame);},[windowKey]);
 useEffect(()=>{if(!target.current||!days.length)return;const chart=init(target.current,undefined,{renderer:'svg',width:layout.width,height:260});
  chart.setOption({animation:false,textStyle:{fontFamily:stack},grid:{left:8,right:8,top:16,bottom:40},
   xAxis:{type:'category',data:days.map(d=>d.date),axisLine:{show:false},axisTick:{show:false},axisLabel:{interval:0,fontSize:11,color:light?'#70747e':'#abb0c1',formatter:(d:string)=>d===selected?`{chosen|${d.slice(5)}}`:d.slice(5),rich:{chosen:{fontFamily:stack,color:light?'#30333b':'#eceaf3',backgroundColor:light?'#e9e9e9':'#39314e',borderRadius:5,padding:[4,6]}}}},
   yAxis:{type:'value',min:0,max:layout.maximum,show:false,splitLine:{show:false}},
   series:[{type:'bar',barWidth:28,label:{show:false},emphasis:{disabled:true},showBackground:true,backgroundStyle:{color:light?'#f3f3f3':'#222737',borderRadius:9},data:days.map((d,i)=>({value:layout.values[i],itemStyle:{color:accent,opacity:d.date===selected?1:.65,borderWidth:d.date===selected?2:0,borderColor:light?'#30333b':'#eceaf3',borderRadius:[9,9,0,0]}}))}]});
  chart.getZr().on('click',e=>{if(!chart.containPixel({gridIndex:0},[e.offsetX,e.offsetY]))return;const index=Math.round(Number(chart.convertFromPixel({xAxisIndex:0},e.offsetX)));if(days[index])onSelect(days[index].date);});
  return()=>chart.dispose();
 },[days,unit,accent,selected,onSelect,light,stack,layout.width,layout.maximum]);
 useEffect(()=>{const el=viewport.current,index=days.findIndex(d=>d.date===selected);if(!el||index<0)return;const x=8+(index+.5)*(layout.width-16)/days.length;if(x<el.scrollLeft+28)el.scrollTo({left:x-48,behavior:'smooth'});else if(x>el.scrollLeft+el.clientWidth-28)el.scrollTo({left:x-el.clientWidth+48,behavior:'smooth'});},[selected,layout.width,windowKey]);
 return <div className="bar-scroll" ref={viewport} role="group" tabIndex={0} aria-label={`每日${unit}柱状图，可用左右方向键选择日期`} onKeyDown={e=>{if(!days.length)return;let index=days.findIndex(d=>d.date===selected);if(e.key==='ArrowLeft')index=index<0?days.length-1:Math.max(0,index-1);else if(e.key==='ArrowRight')index=index<0?0:Math.min(days.length-1,index+1);else if(e.key==='Home')index=0;else if(e.key==='End')index=days.length-1;else return;e.preventDefault();onSelect(days[index].date);}}><div ref={target} className="daily-bars" style={{width:layout.width,height:260}}/></div>;
}
