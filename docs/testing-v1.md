# Fluent Chat V1.0 test matrix

Owner rule: "a V1.0 release that is tested and verified in every scenario."
This file is the honest map of what is verified, how, and what stays open
until a real account links. Statuses: ✅ verified / 🔲 pending / ⛔ blocked
on a real linked account.

Legend for the proof columns: **CI** = GitHub Actions build job,
**VM** = dockur Windows 11 VM run (screenshots), **REAL** = needs Mohamed's
number linked once.

## A. Build and packaging

| # | Scenario | How | Status |
|---|----------|-----|--------|
| A1 | Rust core compiles (check + release) | CI | 🔲 |
| A2 | WinUI shell compiles and publishes | CI | 🔲 |
| A3 | Bundle: core/fluentchatd.exe + VC CRT next to shell | CI artifact | 🔲 |
| A4 | Portable zip runs from an arbitrary folder | VM | 🔲 |
| A5 | Installer: C:\Program Files\Fluent Chat, desktop shortcut option, closes running app | VM | 🔲 |
| A6 | In-app update check + install over existing install (same identity) | VM (two fake releases) | 🔲 |

## B. Core / shell pipe (no account needed)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| B1 | fluentchatd starts, answers `ping` with protocol version | CI script | 🔲 |
| B2 | `getState` returns link state + null me before link | CI script | 🔲 |
| B3 | Bad frames / unknown methods get JSON-RPC errors, core keeps running | CI script | 🔲 |
| B4 | `loadChat` with unknown chat answers cleanly (no hang) | CI script | 🔲 |
| B5 | `shutdown` exits the core within 3 s | CI script | 🔲 |
| B6 | Shell "core missing" page when fluentchatd.exe absent | VM | 🔲 |
| B7 | Core restart mid-session: shell recovers state | VM | 🔲 |
| B8 | Storage: state/cache dirs created with private permissions, reopen works | CI script | 🔲 |

## C. UI (offline/mock path, no account)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| C1 | Mica Alt backdrop active, window + two cards rounded, layered look | VM screenshot | 🔲 |
| C2 | Light theme / dark theme | VM screenshots | 🔲 |
| C3 | Arabic RTL mirrors layout (sidebar right, title bar, bubbles) | VM screenshots | 🔲 |
| C4 | 125% and 100% display scaling, no clipped text or misaligned rows | VM screenshots | 🔲 |
| C5 | Chat list: search filters, chips filter, unread badges, pinning order | VM with demo data | 🔲 |
| C6 | Chat page: bubbles L/R, timestamps, composer send enable/disable | VM with demo data | 🔲 |
| C7 | Animations: page transitions, list item add, send; 60 fps, no jank | VM recording review | 🔲 |
| C8 | Login page: QR renders, steps shown, state transitions (unlinked→connecting) | VM (core emits real QR - no scan) | 🔲 |
| C9 | Crash log written on unhandled exception | VM (forced fault) | 🔲 |

## D. Live account (blocked until Mohamed explicitly links his number)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| D1 | QR scan links his account | REAL | ⛔ |
| D2 | Chat list matches phone (names, order, unread counts) | REAL | ⛔ |
| D3 | History sync completes; older paging via `fetchOlder` | REAL | ⛔ |
| D4 | Send/receive text both directions, receipts (sent/delivered/read) | REAL | ⛔ |
| D5 | Typing indicator and presence both directions | REAL | ⛔ |
| D6 | Media download (`downloadMedia`) and thumbnails | REAL | ⛔ |
| D7 | Mark read/unread syncs to phone | REAL | ⛔ |
| D8 | Reconnect after network drop; queued sends flush | REAL | ⛔ |
| D9 | LoggedOut handling when unlinked from the phone | REAL | ⛔ |

## E. Release

| # | Scenario | How | Status |
|---|----------|-----|--------|
| E1 | Three assets: portable zip, installer, full bundle | CI release job | 🔲 |
| E2 | English changelog, feature list on first release | release notes review | 🔲 |
| E3 | MIT license + attribution (crmne/zapfast, jlucaso1/whatsapp-rust) intact | file check | 🔲 |
| E4 | No "WhatsApp" in the public app name; Meta brand terms respected | review | 🔲 |
| E5 | Drive source backup under My Project Codes | standing rule | 🔲 |
