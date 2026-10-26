#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use serde::Serialize;
use std::path::PathBuf;
use tauri::{Emitter, Manager};
mod bridge;
mod ipc_check;
mod files;

#[tauri::command]
async fn host_request(request: serde_json::Value, state: tauri::State<'_, bridge::Bridge>) -> Result<serde_json::Value, String> {
    state.call(request).await
}

#[tauri::command]
fn restart_host(state: tauri::State<'_, bridge::Bridge>) { state.restart(); }

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EnvironmentReport { packaged: bool, data_directory: String }

#[tauri::command]
fn environment_report() -> Result<EnvironmentReport, String> {
    use windows_sys::Win32::{Storage::Packaging::Appx::GetCurrentPackageFullName, UI::Shell::{SHGetKnownFolderPath, FOLDERID_LocalAppData}, System::Com::CoTaskMemFree};
    unsafe {
        let mut length = 0;
        let code = GetCurrentPackageFullName(&mut length, std::ptr::null_mut());
        if code != 15700 { return Err(format!("Pulse requires unpackaged deployment (package result {code})")); }
        let mut pointer = std::ptr::null_mut();
        let result = SHGetKnownFolderPath(&FOLDERID_LocalAppData, 0, std::ptr::null_mut(), &mut pointer);
        if result < 0 { return Err(format!("LocalAppData resolution failed: {result}")); }
        let mut size = 0;
        while *pointer.add(size) != 0 { size += 1; }
        let folder = String::from_utf16(std::slice::from_raw_parts(pointer, size));
        CoTaskMemFree(pointer.cast());
        let path = PathBuf::from(folder.map_err(|error| error.to_string())?).join("Pulse");
        Ok(EnvironmentReport { packaged: false, data_directory: path.to_string_lossy().into_owned() })
    }
}

fn main() {
    let args:Vec<String>=std::env::args().collect();
    if let Some(index)=args.iter().position(|a|a=="--ipc-check") {
        let result=args.get(index+1).ok_or("Missing isolated artifact root".to_string()).and_then(|path|ipc_check::run(PathBuf::from(path)));
        match result {Ok(report)=>{let text=serde_json::to_string_pretty(&report).unwrap();if let Some(path)=args.get(index+1){let _=std::fs::write(PathBuf::from(path).join("ipc-checks.json"),&text);}println!("{text}");},Err(error)=>{if let Some(path)=args.get(index+1){let _=std::fs::write(PathBuf::from(path).join("ipc-checks.json"),serde_json::json!({"passed":false,"error":error}).to_string());}eprintln!("{error}");std::process::exit(1);}}
        return;
    }
    if std::env::args().any(|arg| arg == "--diagnostics") {
        match environment_report() {
            Ok(report) => println!("{}", serde_json::to_string_pretty(&report).expect("serialize report")),
            Err(error) => { eprintln!("{error}"); std::process::exit(1); }
        }
        return;
    }
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            if let Some(window) = app.get_webview_window("main") { let _ = window.show(); let _ = window.set_focus(); }
        }))
        .setup(|app| {
            let args: Vec<String> = std::env::args().collect();
            let data = args.iter().position(|a| a == "--data-dir").and_then(|i| args.get(i+1)).map(PathBuf::from)
                .unwrap_or(PathBuf::from(environment_report().map_err(std::io::Error::other)?.data_directory));
            let mut config = bridge::Config::new(data, false).map_err(std::io::Error::other)?;
            config.manager=true;
            let handle = app.handle().clone();
            app.manage(bridge::Bridge::start(config, std::sync::Arc::new(move |event, value| {
                if event=="pulse:exiting" {handle.exit(0);} else {let _ = handle.emit(event, value);}
            })));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![environment_report, host_request, restart_host, files::backup_file, files::confirm_restore, files::confirm_delete, files::confirm_amount, files::confirm_exit])
        .run(tauri::generate_context!()).expect("Pulse manager startup failed");
}
