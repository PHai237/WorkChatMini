# WorkChat Mini 2.60.5 — Unified workspace polish

This version continues **2.60.4**, not a reset. It aligns the native shell with the violet/indigo Control Center to avoid two competing visual identities.

## Changes

- **Design tokens:** one dark-indigо interaction palette; semantic mint remains for successful/healthy status. Static WPF rail and hero gradients, no WebView/GSAP dependency or moving background.
- **App rail:** preserves original 29-DIP pixel PNG icons and 46-DIP click targets; active service remains highlighted on hover. Split and Settings get 17-DIP Segoe MDL2 symbols rather than large Unicode characters.
- **Browser:** WPF-only New Tab with an indigo hero, grouped shortcuts and full-width wrap behavior. Blank tabs continue to avoid initializing WebView2.
- **Reading Hub:** native header and Continue Reading cards share the new palette; actual saved reading metadata, filters and browser-tab behavior stay unchanged.
- **Settings:** limits popup height relative to the window, switches to single-column navigation when either width or height is limited, hides nonessential header description/footer copy in short windows, and allows inner content to scroll. Maintains sidebar/compact switching, Chat Alert Back, Esc navigation, independent alerts and all commands.
- **QA:** checks new gradient resources, shell structure and short-height bounds in existing self-test/source preflight.

## Design references

Applied the principles documented in `UI_SKILLS_2.60.4.md` (Impeccable, Taste Skill, GSAP Skills *motion guidance*, Better Interface, WPF UI). No third-party source or UI library has been copied into WorkChat.

## What did not change

WebView2 session folder, login handling, tab limit, memory saver, notifications, MangaDex network behavior, reading/bookmark JSON schema, update mechanism and `PUSH.bat` release pipeline. No new dependency or animation loop.

## Windows QA still required

1. Run `PUSH.bat` and confirm GitHub Windows preflight, Quick and Stress tests pass. Local static checks do **not** prove WPF compilation.
2. Open Settings at widths **330, 520 and 960 DIP** and heights **220, 400 and 650 DIP**, particularly the compact one-column version. Check header, Chat Alerts / Back, scroll, Esc, selected/hover states and disabled choices.
3. Open Web → New Tab, click each quick link and confirm no additional WebView is created for an empty tab.
4. Open Reading Hub, test search, filters, Continue, Add/Cancel, virtual tabs; verify existing bookmarks and profiles persist.
5. Check icons, theme, keyboard focus and layout at 100%, 150%, 200% display scale. Observe RAM before and after repeated Settings/Reading Hub opens.

This is a **source candidate**, not a verified Windows screenshot or finished build.
