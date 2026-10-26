import {formatAtoms,type Group} from './model';

export function groupText(groups:Group[],u:string){
 const total=groups.reduce((sum,g)=>sum+BigInt(g.amount_atoms),0n),count=BigInt(groups.length);
 const average=count===0n?0n:(total+count/2n)/count;
 return {amounts:groups.map(g=>formatAtoms(g.amount_atoms)).join(' / '),summary:`${count}组 × 平均${formatAtoms(average.toString())}${u}，共${formatAtoms(total.toString())}${u}`};
}
export function compactGroupText(groups:Group[],u:string){
 const count=BigInt(groups.length),total=groups.reduce((sum,g)=>sum+BigInt(g.amount_atoms),0n);
 return count?`${count} × ${formatAtoms(((total+count/2n)/count).toString())} ${u}`:'';
}
export function TrainingGroups({groups,unit,compact=false}:{groups:Group[];unit:string;compact?:boolean}){
 if(compact){
  if(!groups.length)return null;
  const summary=compactGroupText(groups,unit),description=summary.replace(' × ','组，平均每组 ');
  return <div className="training-groups compact-groups" title={description} aria-label={description} tabIndex={0} data-no-drag>{summary}</div>;
 }
 const text=groupText(groups,unit);
 return <div className="training-groups" aria-label="训练分组"><div className="training-group-amounts">{groups.length?text.amounts:'暂无分组'}</div><small title="平均量四舍五入至最多三位小数">{text.summary}</small></div>;
}
