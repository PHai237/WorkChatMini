# WorkChat Mini Core 2.51.0 — Pixel Rail & Minimal Shell

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
