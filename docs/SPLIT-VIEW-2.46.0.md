# WorkChat Mini Core 2.46.0 — Split View

## Intended user flow

1. Open YouTube in the normal WorkChat window.
2. Click the `◫` Split control (or use `Ctrl+Shift+2`), and Messenger appears on the right using its existing WebView2 instance.
3. Use both at the same time; drag the center divider to resize.
4. Right-click Split to choose Discord, Zalo, YouTube, Facebook, or Messenger as the companion pane.
5. Click Split again to return to a single pane. The right pane is temporarily hidden if window width falls below 760 DIP, and restores when widened.
6. If the toolbar is collapsed, use the Split button beside the toolbar reveal handle.

## Design guarantees

- The right pane uses the same already-owned WebView2 control and the same app profile; no duplicate browsers or second logins.
- This feature is session-only and does not preload the companion site at app startup.
- The Resource Saver treats both visible panes as foreground, so it will not suspend the second pane as an inactive background tab.
- Audio policy and user-away YouTube resume account for both visible panes. Existing Discord call guard remains intact.
- No user data, profile, cookies, token, updater or installer changes.
- Web remains the primary pane only: the companion selector supports pinned services, not a second browser tab.

## Deliberately not in 2.46

Dragging a running WebView2 control into a separate HWND/window is NOT implemented in this release. Reparenting or duplicating native WebView2 HWNDs can interfere with input, focus, video playback and ongoing calls, and requires Windows interactive regression tests. The split view provides the requested YouTube + messaging workflow without that risk.

## Windows acceptance tests (must run before publishing)

- `BUILD.bat --nopause` on Windows, compiler warnings as errors.
- `AUTO-TEST.bat --built --nopause` Quick and Stress; new `Split layout` self-test is included.
- Manual: YouTube video keeps playing while Messenger receives/sends a message in the right pane.
- Manual: switch left to Discord and back with the companion pane open; resize splitter; shrink width below 760 then expand.
- Manual: background audio off, then on; Discord voice call protection, minimize/restore and click both surfaces.
- Manual: collapse toolbar, use Split handle button; verify no active-tab freeze, added sign-in, or WebView2 process duplication.

## Verification limits

Static parsing, names/handlers, ZIP consistency can be checked off Windows, but WPF XAML compilation, WebView2/HWND airspace handling, actual app behavior, performance and GitHub CI outcomes have NOT been verified here.
