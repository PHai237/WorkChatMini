# WorkChat 2.35.0 source audit

**Scope:** 206 C# files (~33,800 lines), five XAML and .csproj XML, GitHub Actions, updater/feed, chapter text/index cache, Story persistence/checkpoint, Gemini transport, WPF rendering and WebView2 resource policy.

## Confirmed source defect repaired

Previously StoryChapterIndexStore.RefreshAsync enumerated *every* `.md` in a Story's `chapters/` folder and indexed its text. If an aborted commit left a loose manuscript file, it could be retrieved by later generation despite being absent from committed Story Memory. Version 2.35.0 now loads the persistent chapter ledger first and indexes only unique, safely named referenced manuscripts. An unreadable ledger fails closed instead of silently accepting orphan files. Added a regression fixture for uncommitted chapter retrieval; updated synthetic chapter test fixtures to register their committed chapters explicitly.

## Other checks

- XML/XAML and C# project XML parsed; GitHub release workflow YAML parsed.
- 206 C# and 5 XAML files stay below 1,000 lines each; longest C# 976 lines.
- Icon/resource naming and updater ZIP validation preserved.
- Root remains compact; BUILD.bat CRLF restored.
- Story app storage, WorkChatMini.exe name, binary update format and AI stages unchanged.

## Unverified

- Windows WPF compile, 22-case Quick/Stress and end-to-end update/release were not run here (no Windows/.NET SDK).
- Resource savings, Discord-call robustness, prose quality and long-session RAM require live testing; static review alone cannot certify them.
- This is a focused safety patch rather than a claim to have proven every source file bug-free.
