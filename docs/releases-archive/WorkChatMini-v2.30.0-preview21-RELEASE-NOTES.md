# WorkChat Mini v2.30.0-preview21 — Clean Install & Update Workflow

Built on **v2.30.0-preview20**, which passed Windows Quick **22/0/0** and Stress **22/0/0** (reports dated 2026-10-11). No Story canon, Gemini provider, Story Director, Discord call or browser/AdBlock policy changed.

## What changed

1. `AUTO-TEST.bat` **no longer builds**. It runs Quick + Stress against the currently installed app by default. `AUTO-TEST.bat --built` runs the EXE previously produced by `BUILD.bat` (for developers and CI). Missing reports, wrong mode and non-22/0/0 results now block test success.
2. New installed `TEST-APP.bat` can launch isolated Quick + Stress without SDK, source folder or a build. Reports and self-test logs remain under `%LOCALAPPDATA%\WorkChatMini-SelfTest`. A tiny PowerShell runner is shared with the developer test script.
3. BUILD output and update/install ZIPs moved to `_Internal\Build` and `_Internal\Releases`. No new EXE, updater folder or release ZIP should be created in the source root.
4. Removed version-specific release notes from the source root; historical notes are under `docs/releases-archive`, current notes under `_Internal/RELEASE-NOTES.md`.
5. New Inno Setup EXE installer script: on the configured GitHub Actions Windows release pipeline, produce `WorkChatMini-Setup-v2.30.0-preview21.exe`. It installs per-user to `%LOCALAPPDATA%\Programs\WorkChatMini`, preserves `%LOCALAPPDATA%\WorkChatMini`, includes update/recovery/self-test helper and Start Menu shortcut. No source ZIP extraction or SDK for end users.
6. GitHub release pipeline now requires the existing 22/0/0 Windows tests on the built EXE and adds Setup EXE (plus older compatibility ZIPs) to the release and feed landing page.
7. Updated first-install helper to detect development layout and legacy binary installer layout.

## Important limits

- **No binary Windows Setup EXE is included in the source ZIP.** A configured GitHub repository/Windows release runner must generate it. This workspace has no Windows .NET/WPF build environment.
- **Online updates are not activated just by downloading this source ZIP.** Configure repository + GitHub Pages and produce a successful release once. Thereafter the app can check More → Updates and install binary updates without uninstalling.
- SHA-256 provides integrity, not signed publisher identity. Code signing remains a future requirement for public distribution.
- Existing installed app can continue running; preview21 is not Windows validated yet.
- For Story usage, preview20 has passed technical selftests. Actual Narrative Quality and Story Director behavior still need manual playtesting. Test first with a separate Story; use Companion + Chat and Review before commit.

## How to proceed

See `docs/START-HERE.md` in the source ZIP. For developers, `BUILD.bat` is a distinct action. For end users, download *WorkChatMini-Setup-v*.exe* from a trusted GitHub Release after the release pipeline succeeds, then use More → Updates for subsequent versions.
