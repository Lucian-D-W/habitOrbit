use crate::bridge::{Bridge,Config};
use serde_json::{json,Value};
use std::{path::PathBuf,sync::{Arc,atomic::{AtomicUsize,Ordering}},time::Duration};

async fn call(bridge:&Bridge,method:&str,params:Value)->Result<Value,String>{
 let reply=bridge.call(json!({"id":uuid::Uuid::new_v4().to_string(),"method":method,"params":params})).await?;
 if reply["ok"]!=true{return Err(format!("{method}: {reply}"));}Ok(reply["result"].clone())
}
fn require(value:bool,message:&str)->Result<(),String>{if value{Ok(())}else{Err(message.into())}}

pub fn run(root:PathBuf)->Result<Value,String>{
 tauri::async_runtime::block_on(async move {
  let data=root.join(format!("isolated-{}",uuid::Uuid::new_v4()));
  let config=Config::new(data.clone(),true)?;
  let updates=Arc::new(AtomicUsize::new(0));let snapshots=Arc::new(AtomicUsize::new(0));
  let u=updates.clone();let s=snapshots.clone();
  let events:Arc<dyn Fn(&str,Value)+Send+Sync>=Arc::new(move|event,_|{if event=="pulse:changed"{u.fetch_add(1,Ordering::Relaxed);}if event=="pulse:snapshot"{s.fetch_add(1,Ordering::Relaxed);}});
  let bridge=Bridge::start(config.clone(),events.clone());
  let result=exercise(&bridge,&config,&updates).await;
  if result.is_err(){let _=call(&bridge,"host.exit",json!({})).await;return result;}
  drop(bridge);tokio::time::sleep(Duration::from_millis(250)).await;
  let direct=tokio::net::windows::named_pipe::ClientOptions::new().open(&config.pipe).map_err(|e|format!("Manager bridge closed but Host unavailable: {e}"))?;
  drop(direct);
  let reopened=Bridge::start(config.clone(),events);
  let persisted=call(&reopened,"state.snapshot",json!({})).await?;
  require(persisted["activities"].as_array().is_some_and(|a|a.len()==16),"Reopen lost saved Activities")?;
  call(&reopened,"host.exit",json!({})).await?;
  tokio::time::sleep(Duration::from_secs(4)).await;
  require(tokio::net::windows::named_pipe::ClientOptions::new().open(&config.pipe).is_err(),"Bridge resurrected explicitly exited Host")?;
  reopened.restart();
  let restored=call(&reopened,"state.snapshot",json!({})).await?;
  require(restored["activities"].as_array().is_some_and(|a|a.len()==16),"Restart lost Activities")?;
  call(&reopened,"host.exit",json!({})).await?;
  Ok(json!({"passed":true,"isolated_data_directory":data,"activities":16,"changed_notifications":updates.load(Ordering::Relaxed),"subscription_snapshots":snapshots.load(Ordering::Relaxed),"checks":["same SID/path pipe","16 Activities default hidden","UUID replay","seed-only duplicate rejected","64-bit integer strings","unit locked after history","edit preserves old amounts","archive/restore preserves history","thumbnail cache stable after count","corrupt cache rebuilt","thumbnail failure retains Activity","manager close leaves Host alive","reopen snapshot","explicit exit not resurrected","explicit restart"]}))
 })
}
async fn exercise(bridge:&Bridge,config:&Config,updates:&AtomicUsize)->Result<Value,String>{
 let empty=call(bridge,"state.snapshot",json!({})).await?;require(empty["activities"].as_array().is_some_and(|a|a.is_empty()),"Isolation must start empty")?;
 let visual=json!({"renderer_version":1,"family":"glass-orb","base_preset":"mystic","palette":["#6B5CE7","#508FEF","#DF96F0"],"accent_color":"#8B7AEC","highlight":{"x":0.30,"y":0.25},"interior_style":"flowing","decorative_seed":1842});
 let mut first=Value::Null;
 for i in 0..16{
  let mut v=visual.clone();v["accent_color"]=json!(format!("#{:06X}",0x8b7aec+i));
  let mut c=json!({"name":format!("IPC星球{}",i+1),"icon_id":"book-open","description":"隔离技术测试","kind":"count","unit":"页","step_atoms":"500","group_gap_ms":null,"breaks":{"b1":"5000","b2":"10000","b3":"20000"},"visual":v});
  if i==15{c["step_atoms"]=json!("9007199254740993");}
  let created=call(bridge,"activity.create",json!({"config":c})).await?;if i==0{first=created;}
 }
 let id=first["id"].as_str().ok_or("Missing Activity UUID")?;
 let snapshot=call(bridge,"state.snapshot",json!({})).await?;
 require(snapshot["activities"].as_array().unwrap().iter().all(|a|a["desktop"]["visible"]==false),"Create automatically showed desktop Orbs")?;
 let mut duplicate=first["config"].clone();duplicate["name"]=json!("重复外观");duplicate["visual"]["decorative_seed"]=json!(999);
 let response=bridge.call(json!({"id":uuid::Uuid::new_v4().to_string(),"method":"activity.create","params":{"config":duplicate}})).await?;
 require(response["ok"]==false,"Seed-only duplicate unexpectedly accepted")?;
 let thumb=call(bridge,"thumbnail.get",json!({"activityId":id})).await?;
 let event_id=uuid::Uuid::new_v4().to_string();let request=json!({"id":event_id,"method":"count.add","params":{"activityId":id}});
 let added=bridge.call(request.clone()).await?;let replay=bridge.call(request).await?;
 require(added==replay,"UUID replay changed receipt")?;
 let after=call(bridge,"thumbnail.get",json!({"activityId":id})).await?;
 require(thumb==after,"Daily count regenerated identity thumbnail")?;
 let cache=config.data_directory.join("thumbnails").join(format!("{}.png",thumb["cache_key"].as_str().unwrap()));
 let modified=std::fs::metadata(&cache).map_err(|e|e.to_string())?.modified().map_err(|e|e.to_string())?;
 call(bridge,"thumbnail.get",json!({"activityId":id})).await?;
 require(std::fs::metadata(&cache).unwrap().modified().unwrap()==modified,"Valid cache was needlessly regenerated")?;
 std::fs::write(&cache,b"corrupt PNG").map_err(|e|e.to_string())?;
 let rebuilt=call(bridge,"thumbnail.get",json!({"activityId":id})).await?;
 require(rebuilt==thumb&&std::fs::read(&cache).unwrap().starts_with(b"\x89PNG"),"Corrupt thumbnail was not rebuilt")?;
 let directory=config.data_directory.join("thumbnails");let moved=config.data_directory.join("thumbnails-test-backup");
 std::fs::rename(&directory,&moved).map_err(|e|e.to_string())?;std::fs::write(&directory,b"intentional cache failure").map_err(|e|e.to_string())?;
 let failed=bridge.call(json!({"id":uuid::Uuid::new_v4().to_string(),"method":"thumbnail.get","params":{"activityId":id}})).await;
 std::fs::remove_file(&directory).map_err(|e|e.to_string())?;std::fs::rename(&moved,&directory).map_err(|e|e.to_string())?;
 require(failed?["ok"]==false,"Thumbnail storage failure was hidden")?;
 let latest=call(bridge,"state.snapshot",json!({})).await?;
 let row=latest["activities"].as_array().unwrap().iter().find(|r|r["activity"]["id"]==id).unwrap();
 require(row["has_history"]==true&&row["today"]["amount_atoms"]=="500","Cache failure damaged recorded facts")?;
 let mut edited=first["config"].clone();edited["unit"]=json!("次");
 let response=bridge.call(json!({"id":uuid::Uuid::new_v4().to_string(),"method":"activity.update","params":{"activityId":id,"config":edited}})).await?;
 require(response["ok"]==false,"History unit changed")?;edited["unit"]=json!("页");edited["step_atoms"]=json!("1250");
 call(bridge,"activity.update",json!({"activityId":id,"config":edited})).await?;call(bridge,"count.add",json!({"activityId":id})).await?;
 let date=latest["date"].clone();let detail=call(bridge,"stats.day_detail",json!({"activityId":id,"date":date})).await?;
 require(detail["summary"]["amount_atoms"]=="1750"&&detail["events"][0]["delta_amount_atoms"]=="500","Step edit rewrote historical amount")?;
 call(bridge,"activity.archive",json!({"activityId":id})).await?;call(bridge,"activity.restore",json!({"activityId":id})).await?;
 require(call(bridge,"stats.day_detail",json!({"activityId":id,"date":date})).await?==detail,"Archive lost facts")?;
 let context=call(bridge,"stats.context",json!({"activityId":id,"background":"#171B29"})).await?;
 require(context["total_atoms"]=="1750"&&context["colors"].as_array().is_some_and(|c|c.len()==5),"Statistics context lost precise total or shared colors")?;
 let year=date.as_str().unwrap().get(..4).unwrap();
 let annual=call(bridge,"stats.daily",json!({"activityId":id,"from":format!("{year}-01-01"),"to":format!("{year}-12-31")})).await?;
 require(annual.as_array().is_some_and(|a|a.len()>=365)&&annual.as_array().unwrap().iter().find(|d|d["date"]==date).unwrap()["amount_atoms"]=="1750","Annual calendar differs from detail")?;
 let matrix=call(bridge,"stats.matrix_view",json!({"end":date,"days":7,"background":"#171B29"})).await?;
 let row=matrix.as_array().unwrap().iter().find(|r|r["id"]==id).unwrap();
 require(row["days"][6]["amount_atoms"]=="1750"&&row["colors"]==context["colors"],"Matrix totals or levels differ from the annual calendar")?;
 let big_id=latest["activities"][15]["activity"]["id"].clone();
 let big=call(bridge,"count.add",json!({"activityId":big_id})).await?;require(big["summary"]["amount_atoms"]=="9007199254740993","64-bit precision lost across IPC")?;
 require(updates.load(Ordering::Relaxed)>0,"Subscription never emitted state.changed")?;
 let view=call(bridge,"stats.view",json!({"activityId":id,"year":year.parse::<i32>().unwrap(),"days":30,"background":"#171B29"})).await?;
 require(view["context"]["total_atoms"]=="1750"&&view["recent"].as_array().unwrap().last().unwrap()["amount_atoms"]=="1750","Atomic statistics snapshot differs")?;
 require(call(bridge,"state.activity",json!({"activityId":id})).await?["row"]["today"]["amount_atoms"]=="1750","Incremental Activity state differs")?;
 let backup=config.data_directory.join("ipc.pulse-backup");
 call(bridge,"backup.export",json!({"path":backup})).await?;
 call(bridge,"count.add",json!({"activityId":id})).await?;
 let refused=bridge.call(json!({"id":uuid::Uuid::new_v4().to_string(),"method":"backup.restore","params":{"path":backup,"confirmed":false}})).await?;
 require(refused["ok"]==false&&refused["error"]["code"]=="confirmation_required","Restore did not require confirmation")?;
 let restored=call(bridge,"backup.restore",json!({"path":backup,"confirmed":true})).await?;
 require(std::path::Path::new(restored["before_restore_backup"].as_str().unwrap()).is_file(),"Before-restore backup missing")?;
 require(call(bridge,"stats.day_detail",json!({"activityId":id,"date":date})).await?["summary"]["amount_atoms"]=="1750","Restore failed to return to snapshot")?;
 call(bridge,"count.add",json!({"activityId":id})).await?;
 require(call(bridge,"stats.day_detail",json!({"activityId":id,"date":date})).await?["summary"]["amount_atoms"]=="3000","Writes did not resume after restore")?;
 Ok(json!({"passed":true}))
}
