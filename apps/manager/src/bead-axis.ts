// Geometry is shared by rendering and axis hit testing; there are no gap elements.
export const beadPitch=20,dividerPitch=12,axisRowHeight=40;
export type AxisPoint={x:number;y:number};
export function axisLayout(indices:string[],cuts:string[],width:number){
 const beads=new Map<string,AxisPoint>(),dividers=new Map<string,AxisPoint>(),boundaries=new Map<string,AxisPoint>();
 const cutSet=new Set(cuts),lineWidth=Math.max(40,width);let x=0,y=0;
 for(const index of indices){
  const extent=beadPitch+(cutSet.has(index)?dividerPitch:0);
  if(x+extent>lineWidth&&x>0){x=0;y+=axisRowHeight;}
  beads.set(index,{x,y});x+=beadPitch;
  boundaries.set(index,{x,y});
  if(cutSet.has(index)){dividers.set(index,{x,y});x+=dividerPitch;}
 }
 return {beads,dividers,boundaries,height:y+axisRowHeight};
}
export function axisCutAt(layout:ReturnType<typeof axisLayout>,x:number,y:number,units:string,occupied:string[]=[]){
 const blocked=new Set(occupied);let best:string|null=null,distance=Infinity;
 for(const [index,point] of layout.boundaries){
  if(BigInt(index)>=BigInt(units)||blocked.has(index))continue;
  // Prefer the pointer's row; a distant row must not win near the left edge.
  const rowDistance=Math.abs(Math.floor(Math.max(0,y)/axisRowHeight)-point.y/axisRowHeight);
  const score=rowDistance*100000+Math.abs(x-point.x);
  if(score<distance){best=index;distance=score;}
 }
 return best;
}
