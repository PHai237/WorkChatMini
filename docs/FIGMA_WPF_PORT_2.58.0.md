# WorkChat 2.58.0 — Figma Make to native WPF

## Provenance
- Reference: `Workchat UI.zip`, Figma Make React/Vite prototype provided by the user.
- Baseline: WorkChat Core 2.57.0 Reading Progress & MangaDex Updates source archive.
- The Figma mock services/novel text and hard-coded book data are **not** shipped in production.

## UI adapted
- Figma palette: `#0D1016`, `#141820`, `#1A1F29`, `#202631`, `#292F3B`, `#7598FF`, `#E9EBEF`, `#9EA7B5`.
- Narrow left rail 58 DIP / compact 50 DIP; original service icons kept as original bitmap resources with nearest-neighbor scaling.
- Settings flyout 310 DIP, dark nested-page navigation, visually consistent 39-DIP action rows, icons, bottom-anchored placement, direct Updates and Exit.
- Three independent Chat alerts toggles (Messenger, Discord, Zalo) retain persisted preferences and existing permission security policy.
- Browser command buttons re-skin only, no added permanent top toolbar.
- Reading Hub: compact dark header, existing source shortcuts, virtual tab strip, real Continue Reading from most-recent book, real persisted scroll progress, search, All/Reading/Waiting filter, compact list with all real reading actions.

## Preserved
- Real Messenger/Discord/YouTube/Facebook/Zalo sites via WebView2, Split View and companion windows.
- Five-tab Web limit and reading virtual tabs without extra browser renderer.
- Existing local `Reading/library.json` schema and data; URL validation; Hako/DocLN source aliases.
- MangaDex metadata chapter polling limit, language setting and Mark Seen action.
- AppData browser profile, auth cookies, notification settings, update path and old pixel PNGs.

## Windows acceptance checklist
1. Run `PUSH.bat` with temporary Windows preflight; verify Release `dotnet build` and Quick/Stress pass.
2. Check rail icon layout and selection in 1280x800, 960x640 and 720x540 windows (including 125-200% DPI).
3. Open gear, test every Settings page and direct Updates/Exit; test submenu back and 3 independent chat switches.
4. Open Reading Hub with no books, add books from MangaDex, Webnovel, DocLN and custom URL; search, filter, tab/book status, note save, delete confirmation.
5. Read one book and switch to YouTube, return to Continue Reading; verify scroll restore is best-effort, no wrong-book URL.
6. Check MangaDex updates, language and Mark Seen; return to Messenger, split and companion pop-out; ensure profiles/calls remain unaffected.

## Scope of testing here
This archive was validated structurally and inspected for icon hashes, paths, handlers, resource references and ZIP integrity; a Windows WPF compiler and GUI renderer were unavailable, so no actual WPF rendering, performance numbers, or CI success are claimed.
