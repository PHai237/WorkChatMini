# WorkChat Core 2.50.0 — Companion Window implementation notes

## What works

- Drag sidebar icon outside the main window and release; ordinary click remains an ordinary app switch.
- Right-click app icon or Split button, use More menu, or Ctrl+Shift+O.
- Messenger / Facebook / YouTube / Discord / Zalo; existing browser Web tab is unsupported.
- One independent window per service, maximum two total. Clicking an already-open service raises its existing window rather than creating duplicates.
- Existing WebViews inside the main WPF Grid are never removed, relocated or disposed. The new WebView2 shares the existing CoreWebView2Environment and user data directory.
- New window does not navigate until shown, and is disposed on close. Closing WorkChat itself closes all companions before shutdown.
- Discord companion creation is blocked while a call is detected in the main Discord view. Discord audio is not automatically muted in companion because independent voice-call detection is not implemented there.
- Focus inside a companion counts as WorkChat foreground for main YouTube audio. Other companion audio follows focus/background audio settings.
- Extra memory snapshots after opening, once WebView2 is ready, and after closing; timings are logged. No logging of chat bodies, access tokens or full page URLs.

## Why use a new WebView rather than detach the running one?

`Microsoft.Web.WebView2.Wpf.WebView2` derives from HwndHost and owns a native controller with HWND-sensitive lifetime. Reparenting a live view across windows risks losing a running call or video. This staged design creates a shared-profile secondary view instead, knowingly trading extra RAM for safe isolation. **The old page does not transfer current scroll / video position or live call into the companion.**

## Acceptance checklist (must be run on Windows, not claimed by static checks)

1. GitHub preflight Windows `dotnet build ... -p:TreatWarningsAsErrors=true` PASS.
2. Release workflow Quick and Stress PASS and artifact published.
3. Main YouTube playing → drag Messenger icon outside → focus Messenger companion; main playback stays intact and audible.
4. Try opening same companion twice; exactly one window used and focus returns to it.
5. Open two companions, verify third is declined; close one and retry.
6. Close companion and inspect task manager for released WebView2 renderer resources; compare RESOURCE log snapshots before/during/after.
7. Discord active voice call → attempt second Discord window; creation blocked; call uninterrupted. Also test a Discord call inside a companion separately before enabling routine use.
8. Resize WorkChat from 1/8 to full screen; ensure existing Split View, sidebar and compact More menu remain functional.
9. Test 100%, 125%, 150%, 200% Windows display scaling and multi-monitor drag/restore.
10. Verify no changes to `%LOCALAPPDATA%\WorkChatMini\AI`, existing login/profile and update history.

## Limitations

- On Linux this repository can only be statically inspected; WPF build and actual mouse capture/drag-to-desktop behavior must be verified on Windows.
- Releasing a dragged icon beyond the main window is implemented through WPF mouse capture; some third-party window managers or DPI transitions may prevent `PreviewMouseLeftButtonUp` from firing. Right-click and More-menu actions remain available.
- Share login profile ≠ share exact in-page runtime state; new companion loads its service home URL.
- Separate Discord calling, camera, screen sharing and reconnect after Chromium crashes need end-to-end Windows validation.
- More work is possible to measure and lower extra renderer RAM; this version implements guardrails and telemetry, not a proven RAM reduction.
