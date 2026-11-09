## WorkChat Mini 2.60.11 — Resource benchmark and YouTube CPU guard

- **Settings → Playback → Resource Benchmark:** record up to 45 minutes of aggregated WebView2/host RAM and per-PID CPU, including suspend/hibernate/trim event timestamps; all opt-in, local-only CSV, no visited URLs or private chat content.
- **RESOURCE-REPORT.bat** summarizes the latest local capture; see [RESOURCE-BENCHMARK.md](RESOURCE-BENCHMARK.md) for a safe apples-to-apples test procedure.
- **YouTube playback monitoring:** batch rapid DOM mutations into 250 ms checks while preserving immediate play/pause events.
- Existing chat notifications, Discord call guard, Vencord, shared login profile, Reading Hub, browser tab sleep thresholds, and PUSH.bat are unchanged. Windows compilation required before installing.

## WorkChat Mini 2.60.10 — Vencord inside the existing Discord slot

- Keep the same **Discord icon**, `discord.com/app`, existing WebView2 and signed-in profile. **No extra Discord app, tab or WebView**.
- Open **Settings → Tools → Discord + Vencord** to install the official Chromium extension on demand, enable/disable it, or repair it from a local `extension-chrome.zip`. Original Discord always remains available.
- Official one-click install downloads Vendicated's **2026-11-04 devbuild** from GitHub and checks a pinned SHA-256 digest. If the upstream rolling build changes, installation stops safely; download the new `extension-chrome.zip` from [the official Vencord release](https://github.com/Vendicated/Vencord/releases/tag/devbuild) and use the local-ZIP option. A manually selected ZIP is **not** automatically publisher-authenticated; only use a trusted official download.
- ZIP extraction rejects directory traversal and oversized archives; manifest validation requires Vencord Web scripts to target Discord. Extension files remain in immutable, hash-named per-user storage so WebView2 can reopen the extension after restart.
- Toggle Vencord off to revert to original Discord **without deleting the Discord login**. Changes are blocked during active Discord calls. The user's decision is persisted; no download starts automatically on launch.
- Discord does not officially support client mods; Vencord is third-party software. WebView2 extension compatibility must be checked on Windows. `PUSH.bat` and Windows CI remain the release gate.

## WorkChat Mini 2.60.9 — Back, Reload and recent YouTube videos

- **Right-click any app icon** on the rail (Messenger, Facebook, YouTube, Discord, Zalo): **Back, Forward, Reload, Home, Copy page link**, plus *Open in another window*. Items are enabled only when they can work.
- **YouTube — Recently watched:** the last 20 videos (title + link) are remembered, even across restarts. Right-click the YouTube icon to reopen one in a single click; *Clear recently watched* wipes the list. Copy page link gives a clean `youtube.com/watch?v=` URL.
- **Keyboard for apps:** `Alt + ← / →` back/forward, `Alt + Home` app home, `F5` / `Ctrl + R` reload (Web tabs keep their own shortcuts). Listed in Settings → Tools → Keyboard shortcuts.

## WorkChat Mini 2.60.8 — Keyboard-first and smarter search

- **Shortcuts:** `Alt + 1–6` jumps straight to Messenger, Facebook, YouTube, Discord, Zalo or Web from any app; `Ctrl + ,` opens or closes Settings.
- **Settings → Tools → Keyboard shortcuts:** a read-only cheat sheet of every shortcut (apps, Settings, Web tabs).
- **Reading Hub search:** accent-insensitive (`dau pha` finds *Đấu Phá Thương Khung*) and debounced, so typing no longer rebuilds the shelf on every keystroke.

## WorkChat Mini 2.60.7 — Performance, Settings polish, safer updates

- **Memory:** while the window is hidden or minimised for 45 s the app now compacts its managed heap and returns working-set pages to Windows (at most every 5 min, never while the window is visible). Background GC thread and tiered-PGO instrumentation are off, `GCConserveMemory=3`.
- **CPU:** the UI-stall heartbeat and the reading-progress capture no longer wake the app while it is hidden or minimised.
- **Settings:** the close (X) and back buttons are now centred and evenly spaced; tooltip shows the Esc shortcut; Esc keeps working after opening or changing pages.
- **Reading:** MangaDex checks honour `Retry-After`, stop on rate limits and report accurate counts; the library keeps a rolling `.last-good.bak`.
- **Updates:** optional ECDSA-signed `latest.json` (see `docs/UPDATE-SIGNING.md`); off until you add the key.
- **Housekeeping:** big XAML/C# files split under the 1000-line limit, GitHub Actions pinned to commit SHAs, `.gitattributes` keeps `.bat` files CRLF, version comes from the assembly.

## WorkChat Mini 2.60.5 — Unified workspace polish

Continue the 2.60.4 dashboard direction across the main rail, Browser New Tab and Reading Hub. Indigo/violet control states and static depth surfaces align the shell with Control Center; Settings now handles very short windows more safely. Keeps all WebView2/profile, data and memory-saver logic intact. Source-only candidate pending Windows CI and visual QA; use `PUSH.bat` as before. See `docs/UI_POLISH_2.60.5.md` for a targeted QA checklist.

## UI skills applied to Settings (Core 2.60.4)

Research-derived refinement using Impeccable, Taste Skill, Better Interface, GSAP Skills (motion principles only), UI Craft, Design Motion Principles and WPF UI references. The Control Center overview now shows the actual active service and count of enabled chat alerts; sidebar destinations are not duplicated in the desktop content pane, but are available in compact mode. Selected controls retain their active appearance on hover, icons and rows are slightly denser, and page changes use an optional one-shot 125 ms native WPF opacity transition. No npm packages, JS animation libraries, extra WebViews or background animation loops. Read `docs/UI_SKILLS_2.60.4.md` for scope and Windows validation. This is source for Windows CI, not a visually certified release. Run `PUSH.bat` as usual.

## Control Center Settings redesign (Core 2.60.4)

The previous 310-DIP row-only Settings flyout has been replaced with a more expressive **Control Center**: a gradient identity header, violet-dark semantic surfaces, persistent sidebar navigation at normal desktop widths, a dashboard overview, grouped settings, native Windows symbol icons, selected-value feedback and a compact mode for narrow WorkChat windows. Chat alerts still offer independent Messenger/Discord/Zalo toggles, and Back/Escape retain predictable navigation. No WebView or third-party UI framework is added; pixel-art application icons remain intact. Existing Settings actions, memory saver and `PUSH.bat` behavior are unchanged. The changes follow lessons from Discord's desktop settings refresh, Fluent design tokens and WPF UI navigation patterns. A source-code design pass is NOT a verified Windows screenshot; test the UI at 330/520/960 DIP and 100/150/200% DPI after CI. See `docs/CONTROL_CENTER_DESIGN_2.60.4.md`.

## Settings state and UX consistency (Core 2.60.2)

Settings now marks the selected zoom, size, position and resource policy. Escape returns to the parent Settings page, or closes Settings from root. Redundant branding was removed from the popup and browser new tab; Reading Hub labels are clearer. Keeps the existing pixel assets, WebViews, update pipeline and PUSH.bat. This is a source candidate pending Windows UI and build tests.

## Settings navigation and icon polish (Core 2.60.1)

The Chat alerts controls now live inside the same navigable Settings flyout as Zoom/Playback/Window and return via the shared Back button. Messenger/Discord/Zalo remain independent. The sidebar artwork is slightly smaller (33 → 29 DIP) while the 46-DIP click targets remain unchanged. Also scales down the gear/split glyphs and Settings row glyphs. No new UI framework, background workers or WebViews. Keep using `PUSH.bat`; Windows preflight is the release gate. See `docs/SETTINGS_POLISH_2.60.1.md`.

## Quiet Workbench visual redesign (Core 2.60.0)

Impeccable-informed WPF design pass: simpler chrome, clearer active rail, redesigned blank browser tab and Reading Hub, coherent semantic colors, and vertically scrollable library at compact window sizes. All original controls and WebView2 logic remain; no additional browser engines, design-tool runtime or user data migration. See `docs/QUIET_WORKBENCH_DESIGN_2.60.0.md`. **Source candidate; Windows CI and visual QA required.** Use the existing `PUSH.bat` workflow.

## Reading/History data-safety fixes (Core 2.59.1)

This patch protects legacy Reading Hub entries from being silently dropped, preserves a pre-migration JSON backup, isolates saved reading progress between different books on the same site, sorts Continue Reading by actual read time, adds one limited restore retry, and protects damaged browser history before new writes. Safety guards and temporary-file self-tests are included. Where a chapter URL cannot be verified as belonging to the original book (notably Hako `/chuong/{id}` and MangaDex `/chapter/{uuid}`), automatic chapter tracking now intentionally stops instead of guessing. See `docs/READING_SAFETY_PATCH_2.59.1.md` for scope, known limitations and Windows validation steps. **Source-only candidate: build and test on Windows before publishing.**

## Reading Hub usability and scroll support (Core 2.59.0)

Based on Core 2.58.0 Figma port. Your reading shelf now has **Edit** (change a book title or source URL in place with an explicit Cancel action), **Favorite** (star/book prioritization and a Favorites filter), paginated library rendering (40 rows per batch), and best-effort preservation of positions in reader layouts that use an inner scrolling pane (e.g. custom novel readers). Title-only edits preserve reading progress; URL edits intentionally reset that book’s page/scroll and MangaDex update baseline. The reading shelf remains local and stores only metadata, not chapter text/images. No new WebView2 process.

See `docs/READING_HUB_TEST_PLAN_2.59.0.md` for user QA. Source validation only: Windows WPF Compile/Quick/Stress and real site scroll behavior require testing on Windows.

## Figma-inspired WorkChat shell and Reading Hub (Core 2.58.0)

This release ports the **approved Workchat UI.zip Figma Make prototype** to the existing **WPF + WebView2** application (not React). It uses real native controls and real saved data; the six original pixel-art PNGs are unchanged. Updated screens: compact sidebar, Settings/Chat alerts, native browser controls and Reading Hub library with Continue Reading and status filters.

See `docs/FIGMA_WPF_PORT_2.58.0.md` for scope, preserved functionality, test plan and explicit Windows validation limitations.

## Reading Hub (Core 2.57.0)

Select the pixel book icon on WorkChat's left rail. Browse MangaDex, Webnovel or Hako/DocLN;
use **+ Add book** to save a title and its HTTP(S) link from **any other source**.
Each book has a lightweight virtual tab, a reading/waiting marker, an editable chapter note,
and a saved last page URL (updated when you navigate chapters on the same site).
For websites using normal document scrolling, WorkChat also stores a best-effort scroll
percentage every 30 seconds and attempts to restore it next time; image-based readers
with custom scrolling might not support this.
The shelf is local at `%LOCALAPPDATA%\WorkChatMini\Reading\library.json` and does not
copy copyrighted chapters or run background crawlers. Reading reuses the existing Web tab
mechanism: **maximum 5 active Web tabs**, unlimited virtual tabs up to **500** saved titles.
`Waiting` remains a manual reading status. On MangaDex, you may now check for changes
using the read-only chapter metadata API: select All/VI/EN, click **Check chapter** on a
saved series or **Check chapters** for a maximum of eight series. The first check is a
baseline; later changed chapter IDs show a possible new chapter/translation until you
mark it seen. There is no background network polling. Webnovel, Hako/DocLN and custom
links remain bookmark-only until their update mechanisms are separately validated. If all
five browser slots are taken, close an unneeded Web tab first. The six original pixel
art app icons, user WebView profile, chat alerts and Split View are unchanged.

# WorkChat Mini Core 2.54.0 — Settings UI rebuilt

**Settings gear:** A single dark, rounded popup anchored beside the bottom rail gear. All nine top-level commands use one 39-DIP button template. Categories open a page INSIDE that same popup, with Back navigation; no native WPF fly-out submenu can show a white panel. Updates and Exit stay at the root.

**Chat alerts:** Dedicated dark popup with three independent, custom switch controls and the original Messenger/Discord/Zalo pixel-art icons. Settings persist independently. Clicking one switch does not close the popup. WebView2 may show notifications only when WorkChat stays running and when the service/Windows allow them.

**Preserved:** The six original pixel icons, left rail, Split View, companion windows, background-audio toggle, Always on top, zoom, updates, browser profiles and app data. On browser tabs, unsupported Pop out is visibly disabled and resource policy explains that it is managed automatically.

**Release gate:** This is a source candidate. Static/XAML/YAML verification does not prove Windows WPF compilation or actual visual rendering. Run the Windows Preflight followed by Quick/Stress before release; test the popup at 330/520/960 DIP and at 100/150/200% DPI.

## Previous: 2.51.0 — Pixel Rail & Minimal Shell

**UI refresh based on feedback:** Restored all six original 128×128 pixel-art PNG icons (no substitute vector drawings), kept a single left-hand app rail at every window width (48 DIP on narrow windows, 60 DIP normally), pinned **Split** directly above the bottom **Settings gear**, and removed the duplicate 54-DIP top toolbar plus its 18-DIP handle row. Background audio and Always on top stay available as settings-only switches. The former toolbar title and audio/switch controls are hidden compatibility hosts, not visible chrome. Settings are grouped as Pop out, New window, Zoom, Playback, Window and Tools; no long rows of status text. Nothing changes the saved browser profiles, call protection, WebView2 lifetime or Story data.

## Earlier changes: Companion Windows

**New:** drag a service icon outside the WorkChat window and release to open a *separate companion window*, or right-click an app icon and select **Open ... in another window**. Also available at **More → New companion window**, **Ctrl+Shift+O** (current pinned service), and right-click **Split → Open right app in another window**. The existing Split View remains available.

Companion windows use a fresh WebView2 **only on demand**, with the same authenticated WebView2 profile/environment. The active original WebView is *never reparented*, so opening a companion does not relocate/close an existing video or voice controller. At most two companion windows at once, with WebView disposal on close. A companion starts at its service home rather than copying a live playback/scroll position, and it consumes additional RAM while open. Separate Discord windows are blocked while WorkChat detects an active Discord call. Browser/Web tabs are not supported in this batch.

**Audio:** switching focus to a WorkChat companion keeps the main visible YouTube playing with sound while background audio is off; standalone companion windows respect active-window/background-audio mute rules (Discord is protected). Memory snapshots and creation timings are logged without message contents.

**Validation:** source ZIP is a development artifact. Windows WPF Compile + Quick/Stress + call/video verification are required before a public release. No GitHub push or installed user data change occurs just by extracting it.

---

**2.49.0 · Split View polish and stability:** Improved compact/expanded window transitions (audio and memory state follow the actually visible panes), saved the user's split ratio across temporary collapse, added **Swap left / right** to Split's context menu, and made a second Split click cancel a pending open. Opening a background/secondary WebView no longer covers the primary with a loading screen. Switching apps applies pane visibility in one pass to avoid flicker. The toolbar title now truncates gracefully on narrow windows. No extra WebView2 instance, user data deletion, or third-party UI package. Windows build and real playback/call checks still required.

**2.47.0 responsive layout update:** WPF flyout panels have bounded, scrollable content on short windows; narrow action groups wrap rather than clipping; update/clear-data dialogs size against the owner; browser history suggestions no longer use a 220-DIP minimum width; the blank Web tab scrolls at short heights. A Quick Self-Test checks 350 geometry combinations (10 widths × 7 heights × 5 DPI scales). It does not simulate real browser CSS or replace Windows visual validation.

# Previous Core versions

**2.48.0 Modern UI:** 1/16 window size retired and migrated to 1/8, original pixel-art app icons, consistent dark theme, rounded controls/dialogs and Windows-managed dark window chrome where supported.

**2.46.0 Split View:** Two existing pinned WebViews can sit side by side with a draggable divider, controlled by Split / Ctrl+Shift+2 or the context menu. The secondary pane remains foreground for suspend, memory targets and audio; no duplicate WebViews or profile changes. The window automatically returns to single-pane under 760px. Separate-window detachment is intentionally deferred pending Windows WebView2 safety testing.

**2.45.0 UI & performance update:** A narrow vertical app rail replaces the desktop icon strip. Under 520px the rail originally collapsed to an Apps menu (replaced by always-visible compact rail in 2.51.0); no extra WebViews are created. The active Messenger highlight is fixed after retiring Story, height-only window resizes no longer trigger zoom updates, and passive hover wake is disabled during elevated/critical memory pressure (click still wakes the site). Includes a new Core navigation rail self-test.


This source retires the built-in AI Story feature and keeps WorkChat as a lightweight multi-site desktop app (Messenger, Facebook, Discord, YouTube, Zalo, Web).

**User Story data is NOT removed.** Existing `%LOCALAPPDATA%\WorkChatMini\AI` remains on disk, untouched by the application and updater. Back it up before changing computers or removing the app.

The Story button, Story popup, StoryPanel WPF object, and AI Story source modules have been removed from the application. Legacy Story settings are retained only for backward compatibility.

Build and release scripts still need a successful Windows WPF build, Quick/Stress tests, and installer verification before deployment.

## Build
- `SOURCE-CHECK.bat` for static validation
- `BUILD.bat` for Release WPF compilation (Windows/.NET SDK)
- `AUTO-TEST.bat` for app tests
- `PUSH.bat` for protected GitHub preflight and release
