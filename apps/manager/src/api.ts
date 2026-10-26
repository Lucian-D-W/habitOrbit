import {invoke,isTauri} from '@tauri-apps/api/core';
import {listen,type UnlistenFn} from '@tauri-apps/api/event';
export type Request={id:string;method:string;params:unknown};
export class TransportError extends Error {constructor(message:string,public request:Request){super(message);}}
export async function send<T>(request:Request):Promise<T>{
 if(!isTauri())throw new Error('请从星以为常 habitOrbit 桌面管理窗口使用；浏览器页面没有连接记录服务。');
 let response:{ok:boolean;result:T;error?:{code:string;message:string}};
 try{response=await invoke('host_request',{request});}catch(e){throw new TransportError(String(e),request);}
 if(!response.ok)throw new Error(response.error?.message??'操作未完成。');return response.result;
}
export const rpc=<T>(method:string,params:unknown={})=>send<T>({id:crypto.randomUUID(),method,params});
export const restart=()=>invoke('restart_host');
export async function subscribe<T>(event:string,handler:(value:T)=>void):Promise<UnlistenFn>{return listen<T>(event,e=>handler(e.payload));}
