# Fluent Chat core protocol (v1)

The WinUI shell (`shell/`) spawns `fluentchatd` as a child process and talks
to it with newline-delimited JSON-RPC 2.0 over stdin/stdout. Stdout carries
protocol frames only; diagnostics go to stderr. One JSON object per line.

The protocol is private to the Fluent Chat app: it connects the app's own
shell and core processes on one machine. It is not an automation interface
and is not covered by any stability promise to third parties.

## Requests (shell -> core)

| Method | Params | Result |
| --- | --- | --- |
| `ping` | - | `{pong, protocol, core}` |
| `getState` | - | `{protocol, link, me}` |
| `listChats` | - | `{chats: [Chat]}` |
| `loadChat` | `{chat}` | `{chat, messages, older:false, complete}` (deferred: answered when the backend emits the page; `historyUnavailable` answers with an error) |
| `fetchOlder` | `{chat, explicit?}` | `{ok}` (page arrives as a `messages` event with `older:true`) |
| `sendText` | `{chat, text, quote?: {id, chat?}}` | `{ok}` (delivery state arrives as `messageUpdated`) |
| `markRead` | `{chat, receipts?}` (default true) | `{ok}` |
| `markUnread` | `{chat}` | `{ok}` |
| `setComposing` | `{chat, composing}` | `{ok}` |
| `downloadMedia` | `{chat, message}` | `{ok}` (file arrives as a `media` event, progress as `downloadProgress`) |
| `shutdown` | - | `{ok}`, then the core exits |

## Events (core -> shell)

`{"jsonrpc":"2.0","method":"event","params":{"name": <name>, "data": ...}}`

- `link`: `{state: starting|unlinked|connecting|connected|disconnected|logged_out|failed, qr?, pair_code?, pairing_phone?, reason?, error?}`. While `unlinked`, `qr` carries the QR string to render.
- `me`: `{id, lid, name, about}`.
- `chats`: full chat list snapshot `{chats: [Chat]}`.
- `chatUpdated`: one changed `{chat}`; upsert by `chat.id` and re-sort (pinned first, then `last_activity` desc).
- `messages`: a page for a chat (unsolicited or an `older` page).
- `messageUpdated`: one changed message (status, reactions, edits).
- `incoming`: live incoming `{chat, message}` for notifications.
- `messageDeleted` `{chat, id}`, `chatRemoved` `{chat}`, `chatCleared` `{chat, through}`.
- `typing`: `{chat, sender, activity: "typing"|"recording"|null}`.
- `presence`: `{id, online, last_seen}`.
- `avatar`: `{id, full, path}`; `path` is a local file the shell may load.
- `media`: a finished `downloadMedia` `{chat, message, path, error}`.
- `downloadProgress`: `{chat, message, percent}`.
- `historyUnavailable`: `{chat, error}`.

## Shapes

`Chat` mirrors the backend model: `{id, name, kind: direct|group|broadcast,
last_activity, unread, marked_unread, archived, pinned, pinned_at,
muted_until, last: {from_me, sender, sender_name, summary, status}|null,
favorite, read_only, left, locked, ephemeral_expiration, labels,
participants}`.

`Message`: `{id, chat, sender, sender_name, from_me, timestamp, content,
status, quoted, reactions, edited, forwarded, mentions}`. `content` reuses
the backend model's serde shape: `{"kind": "text", "text": ...}`,
`{"kind": "image", "caption", "media": {mime, size, width, height, path}}`,
`video`, `audio`, `document`, `sticker`, `location`, `poll`, `contact`,
`revoked`, ... Media `path` is null until downloaded (`downloadMedia`).
