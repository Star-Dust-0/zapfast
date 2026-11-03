<h1 align="center">Fluent Chat</h1>
<p align="center"><strong>A native Fluent chat experience for Windows.</strong><br>WinUI 3 shell, Rust core, no browser engine.</p>

Fluent Chat is a Windows-focused fork of [ZapFast](https://github.com/crmne/zapfast), with a WinUI 3 interface and a separate Rust core connected over JSON-RPC.

## V1.0: offline demo

This release uses sample conversations only. It does not link an account or send messages to real people. Demo mode is enabled on every normal launch.

- Light and dark themes, rounded cards and a green accent.
- Mirrored RTL content, chat search, filters, unread counts and pinning.
- Text conversations, offline message echo and a small emoji picker.
- Core-process recovery and an installer that closes the app before upgrading.

## Install

Download the Windows x64 installer or portable ZIP from this repository's **Releases** page. Both include .NET and Windows App Runtime. The installer uses `C:\Program Files\FluentChat`; the portable build runs after extraction.

The builds are unsigned. Windows may show a publisher warning. This is an experimental demo, not an official WhatsApp product.

## Known limits

Real account linking and sync are not available in this demo release. New Chat, attachments, conversation search, More, older-history paging and media playback/download are not wired into the new shell. The emoji picker is intentionally small. The title bar remains LTR.

125% display scaling, sustained animation frame rate and the full in-app update path are not yet verified. See [the test record](docs/testing-v1.md) for evidence and remaining gaps.

## Build

The manual **Build Fluent Chat** workflow builds the Rust daemon, runs the pipe probes, publishes the self-contained WinUI shell and packages the portable ZIP and Inno Setup installer. Shell source is in `shell/FluentChat`; the daemon is `src/bin/fluentchatd.rs`.

## License and credits

MIT. Based on [crmne/zapfast](https://github.com/crmne/zapfast) and the [whatsapp-rust](https://github.com/jlucaso1/whatsapp-rust) project. Original notices are preserved in [LICENSE](LICENSE). Unigram was used only as a visual reference; no GPL code or assets were copied.
