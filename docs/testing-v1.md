# Fluent Chat V1.0 test matrix

Owner rule: "a V1.0 release that is tested and verified in every scenario."
This file is the honest map of what is verified, how, and what stays open
until an explicitly authorized test account links. No account login is authorized now. Statuses: ✅ verified / 🔲 pending / ⛔ blocked
on a real linked account.

Legend for the proof columns: **CI** = GitHub Actions build job,
**VM** = dockur Windows 11 VM run (screenshots), **REAL** = needs a separately approved test account. Mohamed's number must not be used.

## A. Build and packaging

| # | Scenario | How | Status |
|---|----------|-----|--------|
| A1 | Rust core compiles (check + release) | CI | ✅ |
| A2 | WinUI shell compiles and publishes | CI | ✅ |
| A3 | Bundle: core/fluentchatd.exe + VC CRT next to shell | CI artifact | ✅ |
| A4 | Portable zip runs from an arbitrary folder | VM | ✅ |
| A5 | Installer: C:\Program Files\FluentChat, desktop shortcut option, closes running app | VM | ✅ |
| A6 | In-app update check + install over existing install (same identity) | VM (two fake releases) | 🔲 |

## B. Core / shell pipe (no account needed)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| B1 | fluentchatd starts, answers `ping` with protocol version | CI script | ✅ |
| B2 | `getState` returns link state + null me before link | CI script | ✅ |
| B3 | Bad frames / unknown methods get JSON-RPC errors, core keeps running | CI script | ✅ |
| B4 | `loadChat` with unknown chat answers cleanly (no hang) | CI script | ✅ |
| B5 | `shutdown` exits the core within 3 s | CI script | ✅ |
| B6 | Shell "core missing" page when fluentchatd.exe absent | VM | ✅ |
| B7 | Core restart mid-session: shell recovers state | VM | ✅ |
| B8 | Storage: state/cache dirs created with private permissions, reopen works | CI script | 🔲 |

## C. UI (offline/mock path, no account)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| C1 | Mica Alt backdrop active, window + two cards rounded, layered look | VM screenshot | 🔲 |
| C2 | Light theme / dark theme | VM screenshots | ✅ |
| C3 | Arabic RTL mirrors layout (sidebar right, title bar, bubbles) | VM screenshots | 🔲 |
| C4 | 125% and 100% display scaling, no clipped text or misaligned rows | VM screenshots | 🔲 |
| C5 | Chat list: search filters, chips filter, unread badges, pinning order | VM with demo data | 🔲 |
| C6 | Chat page: bubbles L/R, timestamps, composer send enable/disable | VM with demo data | ✅ |
| C7 | Animations: page transitions, list item add, send; 60 fps, no jank | VM recording review | 🔲 |
| C8 | Login page: QR renders, steps shown, state transitions (unlinked→connecting) | VM (core emits real QR - no scan) | 🔲 |
| C9 | Crash log written on unhandled exception | VM (forced fault) | ✅ |

## D. Live account (blocked until a separate test account is explicitly authorized)

| # | Scenario | How | Status |
|---|----------|-----|--------|
| D1 | QR scan links an approved test account | REAL | ⛔ |
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
| E3 | MIT license + attribution (crmne/zapfast, jlucaso1/whatsapp-rust) intact | file check | ✅ |
| E4 | No "WhatsApp" in the public app name; Meta brand terms respected | review | ✅ |
| E5 | Drive source backup under My Project Codes | standing rule | 🔲 |

## Verification evidence (2026-11-03)

- Windows build + packaging, Rust daemon and .NET pipe probes:
  https://github.com/Star-Dust-0/zapfast/actions/runs/41669606355
- Clean Windows 11 launch, light/dark, relaunch, RTL sidebar and installed launch:
  https://github.com/Star-Dust-0/zapfast/actions/runs/41677634475
- Search and filters (All=7, Sarah=1, Unread=3, Groups=2, Favorites=0),
  conversation load and offline send echo in light/dark/RTL/installed:
  https://github.com/Star-Dust-0/zapfast/actions/runs/41680307901
- Crash-log handling was observed during earlier missing-PRI launch faults:
  https://github.com/Star-Dust-0/zapfast/actions/runs/41644759425

Partial results remain pending in the table when the whole scenario is not
verified. Installer fresh install and shortcuts passed; closing a running app
for upgrade remains untested. Rounded two-card layout passed, but actual Mica
translucency is not established by the VM screenshots. Sidebar and message
bubbles mirror in RTL; title bar remains LTR and Arabic shaping is untested.
Conversation load, timestamps, demo send echo and composer clearing passed.
Empty send and unread badge defects were fixed in 1bbb282 and verified in
run 41686435185: empty send disabled, nonempty send enabled, badges show 2/1/3.
The same run verified install-over-running-app (exit 0, app closed), upgraded
relaunch, missing-core page, and the folder ACL (SYSTEM, Administrators,
current user only).

A five-second idle sample after sending used 0 additional milliseconds of
UI CPU, with a 169,279,488-byte working set. This is a single VM sample, not
proof of 60 fps, animation smoothness, or performance under real-account load.

Restart recovery was implemented in 81f6896 and verified by forcibly killing
the daemon: a new PID appeared, the seven-chat list returned, and another
offline message was sent successfully. Evidence:
https://github.com/Star-Dust-0/zapfast/actions/runs/41691406863

Still unverified: 125% scaling, storage reopen with real persisted messages,
pinning order, real QR display, updater end-to-end, release assets, source
backup, and all live-account scenarios. No release has been published.

## Known implementation gaps (not merely untested)

- New Chat, attachment, emoji, conversation search and More buttons have no
  action handlers in this shell.
- Pinning is reflected in sorting but has no user action in the shell.
- Older-history paging and media display/download are not wired into the shell.
- The title bar does not mirror with the RTL content.
- The test runner currently logs FAIL markers but does not fail its workflow
  on every behavioral failure. Read the markers and inspect screenshots;
  a green VM workflow alone is not proof that every assertion passed.

This is a verified basic-text-chat demo, not an all-scenarios-verified V1.0.
