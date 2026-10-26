use tauri_plugin_dialog::{DialogExt,MessageDialogButtons,MessageDialogKind};

#[tauri::command]
pub async fn confirm_exit(app:tauri::AppHandle)->Result<bool,String>{
 tauri::async_runtime::spawn_blocking(move||app.dialog().message("有尚未保存的编辑。放弃修改并完全退出？").title("完全退出").kind(MessageDialogKind::Warning).buttons(MessageDialogButtons::OkCancelCustom("放弃修改并退出".into(),"取消".into())).blocking_show()).await.map_err(|e|e.to_string())
}

#[tauri::command]
pub async fn confirm_amount(app:tauri::AppHandle,name:String,before:String,after:String,unit:String,date:String)->Result<bool,String>{
 tauri::async_runtime::spawn_blocking(move||app.dialog().message(format!("保存星球“{name}”在 {date} 的数量修改？\n\n{before} → {after} {unit}\n\n原始记录保留，追加调整或撤销记录。之后撤销仍按单条记录。")).title("确认保存今日数量").buttons(MessageDialogButtons::OkCancelCustom("保存".into(),"取消".into())).blocking_show()).await.map_err(|e|e.to_string())
}

#[tauri::command]
pub async fn confirm_delete(app:tauri::AppHandle,name:String)->Result<bool,String>{
 tauri::async_runtime::spawn_blocking(move||app.dialog().message(format!("永久删除星球“{name}”及其全部历史记录？\n\n删除后无法恢复。其他星球和记录不受影响。")).title("删除已归档星球").kind(MessageDialogKind::Warning).buttons(MessageDialogButtons::OkCancelCustom("永久删除".into(),"取消".into())).blocking_show()).await.map_err(|e|e.to_string())
}

#[tauri::command]
pub async fn backup_file(app:tauri::AppHandle,save:bool)->Result<Option<String>,String>{
 tauri::async_runtime::spawn_blocking(move||{
  let picker=app.dialog().file().add_filter("星以为常 habitOrbit 备份",&["pulse-backup"]);
  let selected=if save{picker.set_file_name(format!("habitOrbit-{}.pulse-backup",uuid::Uuid::new_v4())).blocking_save_file()}else{picker.blocking_pick_file()};
  selected.map(|f|f.into_path().map(|p|p.to_string_lossy().into_owned()).map_err(|e|e.to_string())).transpose()
 }).await.map_err(|e|e.to_string())?
}
#[tauri::command]
pub async fn confirm_restore(app:tauri::AppHandle,path:String)->Result<bool,String>{
 tauri::async_runtime::spawn_blocking(move||app.dialog().message(format!("恢复这个备份会替换当前星以为常 habitOrbit 的记录与布局，不合并记录。恢复前会自动保留当前数据副本。\n\n{path}\n\n确定恢复吗？")).title("恢复星以为常 habitOrbit 备份").kind(MessageDialogKind::Warning).buttons(MessageDialogButtons::YesNo).blocking_show()).await.map_err(|e|e.to_string())
}
