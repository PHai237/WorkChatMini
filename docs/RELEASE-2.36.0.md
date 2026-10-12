# WorkChat Mini 2.36.0 — Stability Audit

- Updates window discards verified but uninstalled EXE staging when closed, replaced or rechecked. The external updater retains ownership after Install is launched.
- Cleanup refuses paths outside WorkChat-generated Pending/<guid>/WorkChatMini.exe and never deletes user ZIPs.
- WebView CPU diagnostics clear stale PID samples when no browser processes remain; background diagnostic-report exceptions are caught and logged.
- Embedded Quick/Stress cases exercise safe cleanup and CPU baseline reset.
- Existing Story Memory/checkpoints/canon, Discord call policy, binary update format, and GitHub Actions flow unchanged.
- Windows build, Quick/Stress, and end-to-end update not verified in this environment.
