use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{collections::HashMap, path::PathBuf, sync::{Arc, atomic::{AtomicBool, Ordering}}, time::Duration};
use tokio::{io::{AsyncBufReadExt, AsyncWriteExt, BufReader}, net::windows::named_pipe::{ClientOptions, NamedPipeClient}, sync::{mpsc, oneshot}};

const MAX_BYTES: usize = 2 * 1024 * 1024;
type Events = Arc<dyn Fn(&str, Value) + Send + Sync>;

#[derive(Clone)]
pub struct Config { pub data_directory: PathBuf, pub host: PathBuf, pub headless: bool, pub pipe: String, pub manager: bool }
impl Config {
    pub fn new(data_directory: PathBuf, headless: bool) -> Result<Self, String> {
        let sid = user_sid()?;
        let full = std::path::absolute(data_directory).map_err(|e| e.to_string())?;
        let text = format!("{sid}|{}", full.to_string_lossy().replace('/', "\\").trim_start_matches(r"\\?\").to_uppercase());
        let hash = Sha256::digest(text.as_bytes());
        let suffix: String = hash[..10].iter().map(|b| format!("{b:02X}")).collect();
        let adjacent = std::env::current_exe().map_err(|e| e.to_string())?.with_file_name("Pulse.Host.exe");
        let development = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../apps/host/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Pulse.Host.exe");
        let host = if adjacent.is_file() { adjacent } else { development };
        Ok(Self { data_directory: full, host, headless, pipe: format!(r"\\.\pipe\Pulse.v1.{suffix}"), manager: false })
    }
}

struct Request { id: String, frame: Vec<u8>, response: oneshot::Sender<Result<Value, String>> }
#[derive(Clone)]
pub struct Bridge { sender: mpsc::Sender<Request>, restart: Arc<AtomicBool>, connected: Arc<AtomicBool> }
impl Bridge {
    pub fn start(config: Config, events: Events) -> Self {
        let (sender, receiver) = mpsc::channel(32);
        let restart = Arc::new(AtomicBool::new(true));
        let connected = Arc::new(AtomicBool::new(false));
        tauri::async_runtime::spawn(run(config, events, receiver, restart.clone(), connected.clone()));
        Self { sender, restart, connected }
    }
    pub fn restart(&self) { if !self.connected.load(Ordering::Acquire) { self.restart.store(true, Ordering::Release); } }
    pub async fn call(&self, request: Value) -> Result<Value, String> {
        let id = request.get("id").and_then(Value::as_str).ok_or("请求缺少UUID")?.to_string();
        uuid::Uuid::parse_str(&id).map_err(|_| "请求UUID无效".to_string())?;
        let mut frame = serde_json::to_vec(&request).map_err(|e| e.to_string())?;
        if frame.len() > MAX_BYTES { return Err("请求超过2 MiB".into()); }
        frame.push(b'\n');
        let (response, result) = oneshot::channel();
        self.sender.send(Request { id, frame, response }).await.map_err(|_| "连接任务已结束".to_string())?;
        tokio::time::timeout(Duration::from_secs(15), result).await.map_err(|_| "Host响应超时；操作结果待核对，重试须沿用原UUID".to_string())?
            .map_err(|_| "连接断开；操作结果待核对，重试须沿用原UUID".to_string())?
    }
}

async fn connect(config: &Config, allow_start: bool) -> Result<NamedPipeClient, String> {
    use std::os::windows::process::CommandExt;
    let mut spawned = false;
    for _ in 0..100 {
        match ClientOptions::new().open(&config.pipe) {
            Ok(pipe) => return Ok(pipe),
            Err(e) if e.raw_os_error() == Some(2) => {
                if !allow_start { return Err("Host已退出；可明确选择重新启动".into()); }
                if !spawned {
                    if !config.host.is_file() { return Err(format!("找不到Host：{}", config.host.display())); }
                    std::process::Command::new(&config.host).arg(if config.headless { "--headless" } else { "--data-dir" })
                        .arg(&config.data_directory).creation_flags(0x08000000).spawn().map_err(|e| e.to_string())?;
                    spawned = true;
                }
            }
            Err(e) if e.raw_os_error() == Some(231) => {},
            Err(e) => return Err(format!("无法连接当前用户Host（普通权限运行）：{e}")),
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    Err("Host启动/连接超时，请检查安装与日志".into())
}

async fn frame<R: tokio::io::AsyncRead + Unpin>(reader: &mut BufReader<R>) -> Result<Value, String> {
    let mut data = Vec::new();
    loop {
        let buffer = reader.fill_buf().await.map_err(|e| e.to_string())?;
        if buffer.is_empty() { return Err("Host连接已关闭".into()); }
        let newline = buffer.iter().position(|b| *b == b'\n');
        let take = newline.unwrap_or(buffer.len());
        if data.len() + take > MAX_BYTES { return Err("Host响应超过2 MiB".into()); }
        data.extend_from_slice(&buffer[..take]);
        reader.consume(take + usize::from(newline.is_some()));
        if newline.is_some() { return serde_json::from_slice(&data).map_err(|e| e.to_string()); }
    }
}

async fn run(config: Config, events: Events, mut requests: mpsc::Receiver<Request>, restart: Arc<AtomicBool>, connected: Arc<AtomicBool>) {
    while !requests.is_closed() {
        let pipe = match connect(&config, restart.swap(false, Ordering::AcqRel)).await {
            Ok(p) => p,
            Err(error) => {
                events("pulse:connection", json!({"connected":false,"message":error}));
                while let Ok(request) = requests.try_recv() { let _ = request.response.send(Err(error.clone())); }
                tokio::time::sleep(Duration::from_secs(2)).await;
                continue;
            }
        };
        let (input, mut output) = tokio::io::split(pipe);
        let mut reader = BufReader::new(input);
        let subscribe = uuid::Uuid::new_v4().to_string();
        let manager_session=uuid::Uuid::new_v4().to_string();
        let parameters=if config.manager {json!({"managerSession":manager_session})}else{json!({})};
        let subscription = json!({"id":subscribe,"method":"state.subscribe","params":parameters}).to_string() + "\n";
        if output.write_all(subscription.as_bytes()).await.is_err() { continue; }
        let (received, mut responses) = mpsc::channel(64);
        let reading = tokio::spawn(async move {
            loop {
                let response = frame(&mut reader).await;
                let failed = response.is_err();
                if received.send(response).await.is_err() || failed { break; }
            }
        });
        let mut pending: HashMap<String, oneshot::Sender<Result<Value, String>>> = HashMap::new();
        let failure = loop {
            tokio::select! {
                request = requests.recv() => match request {
                    None => { reading.abort(); return; },
                    Some(request) => {
                        if pending.len() >= 64 { let _ = request.response.send(Err("等待Host响应的请求过多".into())); continue; }
                        if pending.contains_key(&request.id) { let _ = request.response.send(Err("同UUID请求仍在等待响应".into())); continue; }
                        pending.insert(request.id, request.response);
                        if let Err(e) = output.write_all(&request.frame).await { break e.to_string(); }
                    }
                },
                response = responses.recv() => match response {
                    None => break "Host读取任务结束".into(),
                    Some(Err(error)) => break error,
                    Some(Ok(response)) => {
                        if let Some(id) = response.get("id").and_then(Value::as_str) {
                            if id == subscribe {
                                if response["ok"].as_bool() == Some(true) {
                                    connected.store(true, Ordering::Release);
                                    events("pulse:connection", json!({"connected":true}));
                                    events("pulse:snapshot", response["result"].clone());
                                } else { break response.to_string(); }
                            } else if let Some(result) = pending.remove(id) { let _ = result.send(Ok(response)); }
                        } else if response["method"] == "state.changed" { events("pulse:changed", response["params"].clone()); }
                        else if response["method"] == "maintenance.changed" { events("pulse:maintenance", response["params"].clone()); }
                        else if response["method"] == "host.exit_requested" {
                            events("pulse:exit-requested", json!({"requestId":response["params"]["request_id"],"managerSession":manager_session}));
                        }
                        else if response["method"] == "host.exiting" {
                            connected.store(false,Ordering::Release);
                            events("pulse:exiting",json!({}));
                            if config.manager {reading.abort();return;}
                        }
                        else if response["method"] == "host.exit_cancelled" {events("pulse:exit-cancelled",json!({}));}
                    }
                }
            }
        };
        reading.abort(); connected.store(false, Ordering::Release);
        for (_, response) in pending { let _ = response.send(Err(format!("{failure}；操作结果待核对，重试须沿用原UUID"))); }
        events("pulse:connection", json!({"connected":false,"message":failure}));
        tokio::time::sleep(Duration::from_millis(500)).await;
    }
}

fn user_sid() -> Result<String, String> {
    use windows_sys::Win32::{Foundation::{CloseHandle, LocalFree}, Security::{GetTokenInformation, TokenUser, TOKEN_QUERY, TOKEN_USER, Authorization::ConvertSidToStringSidW}, System::Threading::{GetCurrentProcess, OpenProcessToken}};
    unsafe {
        let mut token = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 { return Err(std::io::Error::last_os_error().to_string()); }
        let result = (|| {
            let mut size = 0;
            GetTokenInformation(token, TokenUser, std::ptr::null_mut(), 0, &mut size);
            let mut buffer = vec![0u64; (size as usize).div_ceil(8)];
            if GetTokenInformation(token, TokenUser, buffer.as_mut_ptr().cast(), size, &mut size) == 0 { return Err(std::io::Error::last_os_error().to_string()); }
            let user = &*buffer.as_ptr().cast::<TOKEN_USER>();
            let mut text = std::ptr::null_mut();
            if ConvertSidToStringSidW(user.User.Sid, &mut text) == 0 { return Err(std::io::Error::last_os_error().to_string()); }
            let mut length = 0;
            while *text.add(length) != 0 { length += 1; }
            let result = String::from_utf16(std::slice::from_raw_parts(text, length)).map_err(|e| e.to_string());
            LocalFree(text.cast()); result
        })();
        CloseHandle(token); result
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn framing_handles_multiple_messages_and_exact_integer_strings() {
        let bytes = b"{\"value\":\"9007199254740993\"}\n{\"ok\":true}\n";
        let mut reader = BufReader::new(&bytes[..]);
        assert_eq!(frame(&mut reader).await.unwrap()["value"], "9007199254740993");
        assert_eq!(frame(&mut reader).await.unwrap()["ok"], true);
        assert!(frame(&mut reader).await.is_err());
    }
    #[tokio::test]
    async fn rejects_oversized_frames_before_json_parsing() {
        let data = vec![b'x'; MAX_BYTES + 1];
        assert!(frame(&mut BufReader::new(&data[..])).await.unwrap_err().contains("2 MiB"));
    }
}
