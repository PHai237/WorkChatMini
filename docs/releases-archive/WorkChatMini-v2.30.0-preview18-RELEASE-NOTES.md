# WorkChat Mini v2.30.0-preview18 — Installed App & In-Place Updates

Base: **v2.30.0-preview17**. All previous Story long-message, partial streaming, memory v6, branches, checkpoints, browser services and resource policies are retained.

## Updates / installation

- `INSTALL-APP.bat` does a one-time, per-user install under `%LOCALAPPDATA%\Programs\WorkChatMini` with Start menu shortcut. No uninstall needed for future versions.
- `BUILD.bat` produces a self-contained EXE and `_Releases\WorkChatMini-update-v2.30.0-preview18.zip`, a binary-only update package containing a manifest of EXE hash/version/size.
- `More → Updates…` and tray `Check for updates...`: select a local release ZIP or use an optional trusted HTTPS JSON feed; verify manifest and hash; then explicitly click `Restart & install`.
- The update UI blocks restarting during active Discord/YouTube audio, Web downloads, and Story AI/storage operations.
- External updater script waits for the app to exit, re-verifies bytes, replaces only the installed EXE, keeps `WorkChatMini.previous.exe` and relaunches. `RESTORE-PREVIOUS.bat` provides manual rollback if needed.
- No silent update and no deletion of Story data, WebView2 profiles, logins, user settings or browser tabs.
- The updater rejects wrong versions, invalid checksums, oversized archives, unexpected ZIP entries and unsafe HTTP update links. HTTPS redirects are checked per-hop.
- Added an optional GitHub Actions Windows release workflow with Quick/Stress gate and binary ZIP assets (requires a repository; not configured yet).
- Added `AppUpdateSelfTest` under the existing Environment self-test case for version ordering, ZIP tamper detection and HTTPS validation.

## Co-Creation clarity

- An explicit **Discuss ideas · Chat** action in Story composer resets to non-writing Chat.
- Fixed a prior Chat heuristic that could trigger automatic Memory bookkeeping for a long answer mentioning "chapter", "scene" or "write". Chat is now strictly read-only for canon; manual Keep direction remains separate.
- Composer hint and Role tooltip explain: **Role = how AI collaborates; Action = what AI does**. Companion/Writer do not by themselves commit chapters. Continue Story/Draft Scene are explicit writing actions.

## Security / limitations

- No update publisher feed exists yet; configure a trusted HTTPS endpoint for online release discovery. Binary ZIPs remain selectable manually in the app.
- SHA-256 is an integrity check, *not* a code signature or proof of publisher identity. The updater is intended for personal/trusted distribution before signing is added.
- Windows WPF BUILD and Quick/Stress have **not run** for preview18 in this environment. No runtime success or performance improvement is claimed.
- The historical Windows-validated baseline remains **preview14.1** (22/0/0 Quick and Stress). Preview15, 16/16.1 and 17 were not Windows-validated in the supplied context.

Full installation and feed instructions: `docs/UPDATE-FEED.md`.
