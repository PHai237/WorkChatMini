# Discord + Vencord Web integration — 2.60.10

**Location:** WorkChat Mini → Settings → Tools → **Discord + Vencord**. Works in the existing Discord button, not a new app slot.

## Activation

1. Open WorkChat Settings → Tools → Discord + Vencord.
2. Select **Install official Vencord Web** for the 2026-11-04 pinned release. It downloads from `github.com/Vendicated/Vencord` and checks SHA-256 before extraction. If the upstream rolling release changes, it **refuses mismatched downloads**.
3. Alternative: download `extension-chrome.zip` from <https://github.com/Vendicated/Vencord/releases/tag/devbuild>, choose **Install from Vencord ZIP**, and explicitly acknowledge the session-access permission. Only use a trusted download; a locally selected ZIP cannot be authenticated by a pinned digest.
4. Return to the **same Discord icon** and verify Vencord's Settings category inside Discord. If you were already on Discord, the page reloads after installation.
5. **Disable Vencord** from that same Settings page to return to original Discord. Your browser login remains intact. This does not remove installed extension files, and can be reversed.

## Design and limits

- No background polling, browser tab or new WebView2. A WebView2 profile already enables extensions. They persist in that shared profile; Vencord's validated content scripts only match `discord.com`. Permissions elsewhere are still governed by Vencord's manifest, and extension API compatibility depends on WebView2.
- WebView2 keeps unpacked extension files in their original folder. The installer stores immutable SHA-named folders under user AppData rather than editing the installed folder.
- Version and source are saved under `settings.json` as `VencordWebEnabled` / `VencordWebPackageSha256`; existing settings/login are preserved.
- The first installed package requires an explicit button press. Nothing is downloaded in the background or automatically on startup. Reopening Discord reattaches the locally cached extension when necessary.
- The official devbuild URL is mutable, and a mismatched ZIP will **not** be installed. Use trusted local ZIP to update. Do not use random mirrors.
- A WebView2 API or manifest failure **must not block loading original Discord**.
- Discord's Terms of Service prohibit client modifications. Use at your own risk.

## Windows QA

- `PUSH.bat`: source guard, build, Quick and Stress tests. New Quick self-test is offline.
- Check Vencord appears inside the old Discord icon, with no extra WebView2.
- Confirm enabled state after WorkChat restart; disable, reload and check original Discord.
- Check logins, calls and notifications still work; try toggling during a Discord call (should refuse).
- Check Messenger, Zalo and other hosts do not have Vencord UI injected.
- Check malformed/non-Vencord ZIP, too-large ZIP and pinned hash mismatch fail safely.
- Compare RAM/CPU before and after; Vencord's own plugin overhead is outside WorkChat's control.

**Windows build/runtime not verified here.**
