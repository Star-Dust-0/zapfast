//! Fluent Chat headless core.
//!
//! The WinUI shell spawns this process and talks to it with
//! newline-delimited JSON-RPC 2.0 over stdio. This process owns the WhatsApp
//! connection through the ZapFast backend; stdout carries protocol frames
//! only, diagnostics go to stderr. See docs/fluent-chat-core-protocol.md.

use std::collections::HashMap;
use std::io::{BufRead, Write};
use std::sync::mpsc as std_mpsc;
use std::time::Duration;

use serde::Serialize;
use serde_json::{json, Value};

use zapfast::backend::{Backend, Command, Event, LinkStatus, Waker};
use zapfast::model::{AccountId, Chat, ChatId, ChatKind, Content, Delivery, LastMessage, Message};
use zapfast::paths::AppDirs;

const PROTOCOL_VERSION: u32 = 1;

/// One-line chat-list preview, mirroring `model::LastMessage` (not serde).
#[derive(Serialize)]
struct LastDto {
    from_me: bool,
    sender: String,
    sender_name: Option<String>,
    summary: String,
    status: Delivery,
}

/// Wire view of `model::Chat` (not serde), keeping the protocol stable even
/// if the internal model grows fields.
#[derive(Serialize)]
struct ChatDto {
    id: ChatId,
    name: String,
    kind: ChatKind,
    last_activity: i64,
    unread: u32,
    marked_unread: bool,
    archived: bool,
    pinned: bool,
    pinned_at: i64,
    muted_until: Option<i64>,
    last: Option<LastDto>,
    favorite: bool,
    read_only: bool,
    left: bool,
    locked: bool,
    ephemeral_expiration: Option<u32>,
    labels: Vec<String>,
    participants: Vec<String>,
}

fn chat_dto(chat: &Chat) -> ChatDto {
    ChatDto {
        id: chat.id.clone(),
        name: chat.name.clone(),
        kind: chat.kind,
        last_activity: chat.last_activity,
        unread: chat.unread,
        marked_unread: chat.marked_unread,
        archived: chat.archived,
        pinned: chat.pinned,
        pinned_at: chat.pinned_at,
        muted_until: chat.muted_until,
        last: chat.last.as_ref().map(|last| LastDto {
            from_me: last.from_me,
            sender: last.sender.clone(),
            sender_name: last.sender_name.clone(),
            summary: last.summary.clone(),
            status: last.status,
        }),
        favorite: chat.favorite,
        read_only: chat.read_only,
        left: chat.left,
        locked: chat.locked,
        ephemeral_expiration: chat.ephemeral_expiration,
        labels: chat.labels.clone(),
        participants: chat.participants.clone(),
    }
}

/// Wire view of `model::Message`; `content` reuses the model's own serde
/// shape (`#[serde(tag = "kind")]`), so new content kinds pass through.
fn message_json(message: &Message) -> Value {
    json!({
        "id": message.id,
        "chat": message.chat,
        "sender": message.sender,
        "sender_name": message.sender_name,
        "from_me": message.from_me,
        "timestamp": message.timestamp,
        "content": serde_json::to_value(&message.content)
            .unwrap_or_else(|_| json!({"kind": "unsupported", "what": "unserializable content"})),
        "status": message.status,
        "quoted": message.quoted,
        "reactions": message.reactions,
        "edited": message.edited,
        "forwarded": message.forwarded,
        "mentions": message.mentions,
    })
}

fn link_json(status: &LinkStatus) -> Value {
    match status {
        LinkStatus::Starting => json!({"state": "starting"}),
        LinkStatus::Unlinked {
            qr,
            pair_code,
            pairing_phone,
        } => json!({
            "state": "unlinked",
            "qr": qr,
            "pair_code": pair_code,
            "pairing_phone": pairing_phone,
        }),
        LinkStatus::Connecting => json!({"state": "connecting"}),
        LinkStatus::Connected => json!({"state": "connected"}),
        LinkStatus::Disconnected { reason } => {
            json!({"state": "disconnected", "reason": reason})
        }
        LinkStatus::LoggedOut => json!({"state": "logged_out"}),
        LinkStatus::Failed(error) => json!({"state": "failed", "error": error}),
    }
}

struct Daemon {
    backend: Backend,
    out: std::io::Stdout,
    quitting: bool,
    link: LinkStatus,
    me: Option<Value>,
    chats: Vec<Chat>,
    /// `loadChat` requests waiting for their `Event::Messages` answer.
    pending_loads: HashMap<ChatId, Vec<Value>>,
    /// Offline demo data instead of a live account (FLUENTCHAT_DEMO=1).
    demo: bool,
    /// Log every frame crossing the pipe to stderr (FLUENTCHAT_TRACE=1).
    trace: bool,
}

impl Daemon {
    fn write(&mut self, frame: Value) {
        let mut line = frame.to_string();
        line.push('\n');
        if self.trace {
            eprintln!("fluentchatd: -> {}", line.trim_end());
        }
        let mut out = self.out.lock();
        if let Err(error) = out.write_all(line.as_bytes()) {
            // The shell closed the pipe: nothing left to serve.
            eprintln!("fluentchatd: stdout write failed: {error}");
            self.quitting = true;
        }
        if let Err(error) = out.flush() {
            eprintln!("fluentchatd: stdout flush failed: {error}");
            self.quitting = true;
        }
    }

    fn respond(&mut self, id: Value, result: Value) {
        self.write(json!({"jsonrpc": "2.0", "id": id, "result": result}));
    }

    fn respond_error(&mut self, id: Value, code: i64, message: &str) {
        self.write(json!({
            "jsonrpc": "2.0",
            "id": id,
            "error": {"code": code, "message": message},
        }));
    }

    fn notify(&mut self, name: &str, data: Value) {
        self.write(json!({
            "jsonrpc": "2.0",
            "method": "event",
            "params": {"name": name, "data": data},
        }));
    }

    fn handle_frame(&mut self, frame: Value) {
        let method = frame.get("method").and_then(Value::as_str).unwrap_or("");
        let id = frame.get("id").cloned().unwrap_or(Value::Null);
        let params = frame.get("params").cloned().unwrap_or(Value::Null);
        if method.is_empty() || id.is_null() {
            // Notifications from the shell are not part of the protocol yet.
            return;
        }
        let result = self.handle_request(&id, method, &params);
        match result {
            Ok(Some(value)) => self.respond(id, value),
            Ok(None) => { /* deferred: the answering event responds instead */ }
            Err((code, message)) => self.respond_error(id, code, &message),
        }
    }

    /// Returns `Ok(Some(result))` to answer now, `Ok(None)` when a later
    /// backend event carries the answer, `Err((code, message))` on failure.
    fn handle_request(
        &mut self,
        id: &Value,
        method: &str,
        params: &Value,
    ) -> Result<Option<Value>, (i64, String)> {
        let chat_id = || -> Result<ChatId, (i64, String)> {
            params
                .get("chat")
                .and_then(Value::as_str)
                .map(str::to_owned)
                .ok_or_else(|| (-32602, "missing string param `chat`".to_string()))
        };
        match method {
            "ping" => Ok(Some(json!({
                "pong": true,
                "protocol": PROTOCOL_VERSION,
                "core": env!("CARGO_PKG_VERSION"),
            }))),
            "getState" => Ok(Some(json!({
                "protocol": PROTOCOL_VERSION,
                "link": link_json(&self.link),
                "me": self.me.clone().unwrap_or(Value::Null),
            }))),
            "listChats" => Ok(Some(
                json!({"chats": self.chats.iter().map(chat_dto).collect::<Vec<_>>()}),
            )),
            "loadChat" => {
                let chat = chat_id()?;
                if self.demo {
                    let messages = demo_messages(&chat);
                    self.pending_loads.entry(chat.clone()).or_default().push(id.clone());
                    self.on_event(Event::Messages {
                        chat,
                        messages,
                        older: false,
                        complete: true,
                    });
                    return Ok(None);
                }
                self.backend.send(Command::LoadChat {
                    chat: chat.clone(),
                    before: None,
                });
                self.pending_loads.entry(chat).or_default().push(id.clone());
                Ok(None)
            }
            "fetchOlder" => {
                let chat = chat_id()?;
                let explicit = params
                    .get("explicit")
                    .and_then(Value::as_bool)
                    .unwrap_or(true);
                self.backend.send(Command::FetchOlder { chat, explicit });
                Ok(Some(json!({"ok": true})))
            }
            "sendText" => {
                let chat = chat_id()?;
                let text = params
                    .get("text")
                    .and_then(Value::as_str)
                    .ok_or_else(|| (-32602, "missing string param `text`".to_string()))?
                    .to_owned();
                let quoting = params.get("quote").and_then(|quote| {
                    let id = quote.get("id")?.as_str()?.to_owned();
                    let source = quote
                        .get("chat")
                        .and_then(Value::as_str)
                        .map(str::to_owned);
                    Some(zapfast::model::ReplyTarget {
                        id,
                        chat: source,
                    })
                });
                if self.demo {
                    let echo = Message {
                        id: format!("demo-out-{}", text.len()),
                        chat: chat.clone(),
                        sender: "demo@s.whatsapp.net".to_string(),
                        sender_name: None,
                        from_me: true,
                        timestamp: std::time::SystemTime::now()
                            .duration_since(std::time::UNIX_EPOCH)
                            .map(|d| d.as_secs() as i64)
                            .unwrap_or(0),
                        history_order: None,
                        content: Content::Text { text, preview: None },
                        status: Delivery::Sent,
                        delivered_at: None,
                        read_at: None,
                        quoted: None,
                        reactions: Vec::new(),
                        edited: false,
                        mentions: Vec::new(),
                        forwarded: false,
                        thumbnail: None,
                    };
                    self.on_event(Event::MessageUpdated(Box::new(echo)));
                    return Ok(Some(json!({"ok": true})));
                }
                self.backend.send(Command::SendText {
                    chat,
                    text,
                    quoting,
                    mentions: Vec::new(),
                    preview: None,
                });
                Ok(Some(json!({"ok": true})))
            }
            "markRead" => {
                let chat = chat_id()?;
                let receipts = params
                    .get("receipts")
                    .and_then(Value::as_bool)
                    .unwrap_or(true);
                self.backend.send(Command::MarkRead { chat, receipts });
                Ok(Some(json!({"ok": true})))
            }
            "markUnread" => {
                let chat = chat_id()?;
                self.backend.send(Command::MarkUnread(chat));
                Ok(Some(json!({"ok": true})))
            }
            "setComposing" => {
                let chat = chat_id()?;
                let composing = params
                    .get("composing")
                    .and_then(Value::as_bool)
                    .unwrap_or(false);
                self.backend.send(Command::Composing { chat, composing });
                Ok(Some(json!({"ok": true})))
            }
            "downloadMedia" => {
                let chat = chat_id()?;
                let message = params
                    .get("message")
                    .and_then(Value::as_str)
                    .ok_or_else(|| (-32602, "missing string param `message`".to_string()))?
                    .to_owned();
                self.backend.send(Command::Download {
                    manual: true,
                    card: None,
                    chat,
                    message,
                });
                Ok(Some(json!({"ok": true})))
            }
            "shutdown" => {
                self.quitting = true;
                Ok(Some(json!({"ok": true})))
            }
            _ => Err((-32601, format!("unknown method `{method}`"))),
        }
    }

    fn on_event(&mut self, event: Event) {
        match event {
            Event::Link(status) => {
                self.link = status.clone();
                self.notify("link", link_json(&status));
            }
            Event::Me {
                id,
                lid,
                name,
                about,
            } => {
                let me = json!({"id": id, "lid": lid, "name": name, "about": about});
                self.me = Some(me.clone());
                self.notify("me", me);
            }
            Event::Chats(chats) => {
                self.chats = chats;
                let list = self.chats.iter().map(chat_dto).collect::<Vec<_>>();
                self.notify("chats", json!({"chats": list}));
            }
            Event::ChatUpdated(chat) => {
                let dto = chat_dto(&chat);
                match self.chats.iter_mut().find(|known| known.id == chat.id) {
                    Some(known) => *known = *chat,
                    None => self.chats.push(*chat),
                }
                self.notify("chatUpdated", json!({"chat": dto}));
            }
            Event::Messages {
                chat,
                messages,
                older,
                complete,
            } => {
                let payload = json!({
                    "chat": chat.clone(),
                    "messages": messages.iter().map(message_json).collect::<Vec<_>>(),
                    "older": older,
                    "complete": complete,
                });
                if !older {
                    if let Some(waiting) = self.pending_loads.remove(&chat) {
                        for id in waiting {
                            self.respond(id, payload.clone());
                        }
                        return;
                    }
                }
                self.notify("messages", payload);
            }
            Event::HistoryUnavailable { chat, error } => {
                if let Some(waiting) = self.pending_loads.remove(&chat) {
                    for id in waiting {
                        self.respond_error(id, -32000, &error);
                    }
                }
                self.notify("historyUnavailable", json!({"chat": chat, "error": error}));
            }
            Event::MessageUpdated(message) => {
                self.notify("messageUpdated", message_json(&message));
            }
            Event::Incoming { chat, message } => {
                self.notify(
                    "incoming",
                    json!({"chat": chat, "message": message_json(&message)}),
                );
            }
            Event::MessageDeleted { chat, id } => {
                self.notify("messageDeleted", json!({"chat": chat, "id": id}));
            }
            Event::ChatRemoved { chat } => {
                self.chats.retain(|known| known.id != chat);
                self.notify("chatRemoved", json!({"chat": chat}));
            }
            Event::ChatCleared { chat, through } => {
                self.notify("chatCleared", json!({"chat": chat, "through": through}));
            }
            Event::Typing {
                chat,
                sender,
                activity,
            } => {
                let activity = activity.map(|kind| match kind {
                    zapfast::model::ChatActivity::Typing => "typing",
                    zapfast::model::ChatActivity::RecordingAudio => "recording",
                });
                self.notify(
                    "typing",
                    json!({"chat": chat, "sender": sender, "activity": activity}),
                );
            }
            Event::Presence {
                id,
                online,
                last_seen,
            } => {
                self.notify(
                    "presence",
                    json!({"id": id, "online": online, "last_seen": last_seen}),
                );
            }
            Event::Avatar { id, full, path } => {
                self.notify(
                    "avatar",
                    json!({"id": id, "full": full, "path": path}),
                );
            }
            Event::Media {
                chat,
                message,
                result,
                ..
            } => {
                let (path, error) = match result {
                    Ok(path) => (Some(path), None),
                    Err(error) => (None, Some(error)),
                };
                self.notify(
                    "media",
                    json!({"chat": chat, "message": message, "path": path, "error": error}),
                );
            }
            Event::DownloadProgress {
                chat,
                message,
                percent,
            } => {
                self.notify(
                    "downloadProgress",
                    json!({"chat": chat, "message": message, "percent": percent}),
                );
            }
            _ => { /* not bridged in protocol v1 */ }
        }
    }
}

/// Fictional chats for offline UI testing (no account): the same cast the
/// approved concept renders use. Never real names or numbers.
fn demo_chats() -> Vec<Chat> {
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);
    let chat = |id: &str, name: &str, kind: ChatKind, ago: i64, unread: u32, summary: &str| Chat {
        id: id.to_string(),
        name: name.to_string(),
        group_subject_known: kind == ChatKind::Group,
        kind,
        last_activity: now - ago,
        unread,
        marked_unread: false,
        archived: false,
        pinned: false,
        pinned_at: 0,
        muted_until: None,
        last: Some(LastMessage {
            from_me: false,
            sender: "demo".to_string(),
            sender_name: None,
            summary: summary.to_string(),
            full: summary.to_string(),
            labelled: false,
            status: Delivery::Read,
        }),
        favorite: false,
        favorite_position: 0,
        participants: Vec::new(),
        group_description: None,
        read_only: false,
        left: false,
        info_locked: None,
        admin: false,
        group_settings: None,
        locked: false,
        ephemeral_expiration: None,
        labels: Vec::new(),
        notification_sound: None,
        unread_mention: None,
        voice_speed: None,
    };
    vec![
        chat("demo-weekend@g.us", "Weekend Trip", ChatKind::Group, 180, 2, "Khaled: Count me in, guys"),
        chat("demo-sara@s.whatsapp.net", "Sarah Ahmed", ChatKind::Direct, 2100, 1, "Great, I will send it to you this evening"),
        chat("demo-omar@s.whatsapp.net", "Omar Farouk", ChatKind::Direct, 5100, 0, "The photos arrived, thank you so much"),
        chat("demo-mariam@s.whatsapp.net", "Mariam Sami", ChatKind::Direct, 8400, 3, "Is the meeting still on for tomorrow?"),
        chat("demo-mom@s.whatsapp.net", "Mom", ChatKind::Direct, 15600, 0, "Do not forget the doctor appointment Thursday"),
        chat("demo-noor@s.whatsapp.net", "Noor Elhoda", ChatKind::Direct, 19800, 0, "Received, everything is fine"),
        chat("demo-work@g.us", "Work Team", ChatKind::Group, 30000, 0, "Youssef: The report is ready for review"),
    ]
}

/// Fictional conversation for the demo group, matching the render script.
fn demo_messages(chat: &str) -> Vec<Message> {
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);
    let text = |id: &str, sender: &str, sender_name: Option<&str>, from_me: bool, ago: i64, body: &str| Message {
        id: format!("demo-{id}"),
        chat: chat.to_string(),
        sender: sender.to_string(),
        sender_name: sender_name.map(str::to_string),
        from_me,
        timestamp: now - ago,
        history_order: None,
        content: Content::Text {
            text: body.to_string(),
            preview: None,
        },
        status: Delivery::Read,
        delivered_at: Some(now - ago),
        read_at: Some(now - ago + 60),
        quoted: None,
        reactions: Vec::new(),
        edited: false,
        mentions: Vec::new(),
        forwarded: false,
        thumbnail: None,
    };
    vec![
        text("1", "demo-mariam@s.whatsapp.net", Some("Mariam Sami"), false, 1500,
            "Good evening everyone, is everything ready for tomorrow trip?"),
        text("2", "demo-khaled@s.whatsapp.net", Some("Khaled"), false, 1320,
            "Yes, I got the car and the tent ready yesterday."),
        text("3", "me", None, true, 1200,
            "Excellent, and I will bring the food and drinks with me."),
        text("4", "demo-sara@s.whatsapp.net", Some("Sarah Ahmed"), false, 900,
            "Do not forget the camera this time!"),
        text("5", "me", None, true, 300,
            "Beautiful shot, that place is really worth the visit."),
    ]
}

fn main() {
    let trace = std::env::var("FLUENTCHAT_TRACE").is_ok();
    if trace {
        eprintln!("fluentchatd: trace on");
    }
    let account = std::env::var("FLUENTCHAT_ACCOUNT").unwrap_or_else(|_| "1".to_string());
    let account_id = AccountId::parse(&account).unwrap_or_else(AccountId::first);
    let dirs = AppDirs::fluent_chat().account(&account_id);
    if let Err(error) = dirs.ensure() {
        eprintln!("fluentchatd: cannot create data directories: {error}");
        std::process::exit(2);
    }

    let demo = std::env::var("FLUENTCHAT_DEMO").is_ok();
    let mut backend = if demo {
        let (backend, _events) = Backend::detached();
        backend
    } else {
        Backend::spawn(dirs, Waker::default())
    };
    if let Some(startup) = backend.take_startup() {
        let _ = startup.send(());
    }

    let (frames_tx, frames_rx) = std_mpsc::channel::<Value>();
    std::thread::Builder::new()
        .name("fluentchatd-stdin".to_string())
        .spawn(move || {
            let stdin = std::io::stdin();
            for line in stdin.lock().lines() {
                match line {
                    Ok(text) if !text.trim().is_empty() => {
                        if trace {
                            eprintln!("fluentchatd: <- {text}");
                        }
                        match serde_json::from_str::<Value>(&text) {
                            Ok(frame) => {
                                if frames_tx.send(frame).is_err() {
                                    return;
                                }
                            }
                            Err(error) => eprintln!("fluentchatd: ignoring bad frame: {error}"),
                        }
                    }
                    Ok(_) => {}
                    Err(_) => return,
                }
            }
        })
        .expect("unable to start the stdin reader");

    let mut daemon = Daemon {
        backend,
        out: std::io::stdout(),
        quitting: false,
        link: LinkStatus::Starting,
        me: None,
        chats: Vec::new(),
        pending_loads: HashMap::new(),
        demo,
        trace,
    };

    if demo {
        daemon.on_event(Event::Link(LinkStatus::Connected));
        daemon.on_event(Event::Me {
            id: "demo@s.whatsapp.net".to_string(),
            lid: None,
            name: Some("Demo User".to_string()),
            about: Some("Offline demo".to_string()),
        });
        daemon.on_event(Event::Chats(demo_chats()));
    }

    eprintln!("fluentchatd: protocol v{PROTOCOL_VERSION} ready (demo={demo})");
    loop {
        match frames_rx.recv_timeout(Duration::from_millis(100)) {
            Ok(frame) => daemon.handle_frame(frame),
            Err(std_mpsc::RecvTimeoutError::Timeout) => {}
            Err(std_mpsc::RecvTimeoutError::Disconnected) => break, // shell closed stdin
        }
        let events = daemon.backend.poll();
        for event in events {
            daemon.on_event(event);
        }
        if daemon.quitting {
            break;
        }
    }
    daemon.backend.shutdown();
}
