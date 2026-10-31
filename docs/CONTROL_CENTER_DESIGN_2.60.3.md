# WorkChat Mini 2.60.3 — Control Center Design

## Design choice
A richer, Discord-adjacent desktop dashboard, not a flat utility menu. Strongest emphasis on settings hierarchy, organized content surfaces and predictable navigation. Preserve WorkChat's pixel-art application identity and low-memory behavior.

## Implementation
- Native WPF popup, no added WebView, UI package, timer, or animation loop.
- Full width (632 DIP) on regular WorkChat windows (>=720 DIP), compact single-column (292–410 DIP) when narrower.
- Persistent left nav for Overview, New window, Chat alerts, Zoom, Playback, Window, Tools. Subpages retain Back semantics.
- Header uses a single gradient brush; soft dark-violet layers and coherent color tokens are scoped to Settings.
- Distinct hero/overview card, section labels, icon-led rows, helpful descriptions, selected/check and switch states.
- Windows Segoe MDL2 glyphs only inside Settings; original pixel-art icons unchanged.
- Preserve all existing click handlers and preferences; no app/profile-data migration.

## References
- https://discord.com/blog/discord-patch-notes-december-8-2025 — desktop Settings redesign.
- https://discord.com/blog/player-release-q12025 — desktop theming and density.
- https://github.com/lepoco/wpfui/blob/main/docs/documentation/navigation-view.md — Back and navigation.
- https://github.com/microsoft/fluentui/blob/master/docs/architecture/design-tokens.md — semantic tokens.
- https://github.com/pbakaus/impeccable — interface audit and hierarchy.

## Windows validation checklist
1. `PUSH.bat`: Windows preflight, release build and Quick/Stress must pass.
2. With WorkChat at 960 DIP width: Settings shows sidebar + overview, category selection, header, rows and scrolling.
3. At 520 / 330 DIP: navigation sidebar disappears; Overview shows all destinations. Confirm that 330 DIP popup remains reachable and vertically scrollable.
4. At 100/150/200% DPI: text, switches, icons and popup placement must not clip.
5. Chat alerts: Messenger, Discord and Zalo settings remain independent; Back returns to Overview.
6. Zoom, window size/position, resource policy selected values remain visible and update after selection.
7. Confirm Esc/back, outside click dismissal, companion popup, updates and exit, and no regressions to WebView2.

## Known limitation
WPF cannot be executed or visually captured in the Linux authoring environment. The source audit cannot certify layout at real Windows DPI or contrast in Windows High Contrast mode.
