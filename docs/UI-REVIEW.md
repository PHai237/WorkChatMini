# WorkChat Mini — UI review (Core 2.60.2)

> Source-level audit of Core 2.60.1, followed by the narrowly scoped 2.60.2 polish. NOT a Windows runtime/screenshot approval. All references below are research inputs, not NuGet dependencies.

## References reviewed (October 2026)

| Repository | Applicable lessons | Decision |
| --- | --- | --- |
| [pbakaus/impeccable](https://github.com/pbakaus/impeccable) | Audit information hierarchy, cut ornamental UI copy, finish edge states | Apply principles |
| [nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill) | State clarity, contrast, accessible labeling, WPF-specific UX criteria | Apply principles |
| [lepoco/wpfui](https://github.com/lepoco/wpfui) | Consistent Fluent-style settings rows, dialogs, navigation | Refer to native WPF patterns; do not import |
| [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery) | Settings information architecture and keyboard behaviors | Apply patterns |
| [iNKORE-NET/UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) | Fluent 2 and light/dark/high-contrast style completeness | Reference; no dependency |
| [Kinnara/ModernWpf](https://github.com/Kinnara/ModernWpf) | WPF navigation controls, keyboard behaviors | Reference; no dependency |
| [MahApps/MahApps.Metro](https://github.com/MahApps/MahApps.Metro) | WPF shell and dialog cohesion | Reference; no dependency |
| [materialdesigninxaml/materialdesigninxamltoolkit](https://github.com/materialdesigninxaml/materialdesigninxamltoolkit) | Consistent form/control states | Patterns only; Material visuals not a fit |
| [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) | Icon legibility and naming | DO NOT replace the user's pixel-art icons |
| [shadcn-ui/ui](https://github.com/shadcn-ui/ui) | Component discipline, restrained visual hierarchy | Patterns only; React cannot run natively in WPF |
| [ant-design/ant-design](https://github.com/ant-design/ant-design) | Dense UX without losing the selected state | Patterns only; web-first toolkit not imported |
| [microsoft/accessibility-insights-windows](https://github.com/microsoft/accessibility-insights-windows) | Windows UI Automation inspection and focus-path validation | Recommended for Windows runtime QA; not a runtime dependency |
| [w3c/aria-practices](https://github.com/w3c/aria-practices) | Keyboard and interaction conventions | Conceptual reference; ARIA attributes do not replace WPF UI Automation |

## Existing shell review

- **App rail** (`MainWindow.xaml`, `WorkChatControls.xaml`): Good: clear active marker, original 29-DIP pixel art, 46-DIP hit targets, internal rail scrolling, compact width. Avoid another icon-size reduction or a second toolbar.
- **Settings** (`MainWindow.SettingsFlyout.cs`): Good: one navigable popup with consistent Back for Chat alerts/Zoom/Playback/Window/Tools. Gap: Zoom, size, position, resource selection had no in-page marker; no Escape path through nested Settings. Fixed in 2.60.2.
- **Browser shell** (`BrowserStyles.xaml`, `MainWindow.xaml`): Good: reusable chrome controls, lazy blank tab with no extra WebView2. Gap: marketing-like placeholder copy instead of concise action-oriented instructions. Fixed in 2.60.2.
- **Reading Hub** (`ReadingHubView.xaml`): Good: continue-reading, search, filters, scroll on small sizes. Gap: extra ornamental overline and ambiguous Save page label. Fixed in 2.60.2. The shelf still creates WPF item rows and paginates; it is not a virtualizing panel. True virtualization only merits work if large libraries exhibit measurable lag.
- **Diagnostics/VPN/Health panels** (`MainWindow.xaml`): They are functional overlays, not separate settings subpages; keep their behavior. Some local colors are inconsistent with the semantic palette, but replacing them globally without visual testing risks weakening status semantics.
- **Keyboard and accessibility**: Existing focus styles cover many buttons. Settings navigation now supports Escape and programmatically named action/toggle controls. Native keyboard and screen-reader QA remains required; high-contrast adaptation is not yet implemented across all custom templates.
- **Responsive behavior** (`WorkChatResponsiveLayout.cs`): narrow windows keep the rail, tools are scrollable; WPF popup placement is monitor-relative. Real tests at small sizes and multiple DPI scales remain necessary to confirm no clipping.

## What changed in Core 2.60.2

1. Settings single-choice rows explicitly show `✓` for the active Zoom / resource policy / window size / position; automation name and selection item status added.
2. `Esc` returns to the previous Settings page; from root it dismisses the flyout. Back behavior, popup policy and all toggle state-handling preserved.
3. Zoom is disabled in the Settings root on native Story surfaces rather than silently accepting a no-op.
4. Removed the redundant WorkChat Mini subtitle inside the Settings popup.
5. New tab uses a direct title (`New tab`) and actionable subtitle; Reading Hub removes the ornamental overline and makes `Save current page` explicit.
6. Existing quick self-test extended to check exactly one selected option on Zoom, Size, Position. No runtime dependency, WebView2 or new source/test file added.

## Windows release acceptance (not verified in this environment)

- `PUSH.bat` performs local source preflight and triggers the existing Windows pipeline. Check GitHub Actions final status before installing.
- Test Settings via mouse, Tab, Shift+Tab and Escape. Check nested resource/position pages, Chat Alert Back and toggles, and check current options after reopening.
- Test 330, 520, 760, 960 device-independent-pixel window widths; 100%, 125%, 150% display scaling and on multi-monitor layouts.
- Test browser blank tabs and Reading Hub with both no books and a large library. Verify page reload/return does not spawn extra WebViews.
- Verify app rail icon crispness, tooltips, keyboard focus indication, high-contrast and Windows screen-reader announcements. These are **not** certified from static parsing.

## Rules for future work

Keep the existing `PUSH.bat` path; do not add instructions or random batch files. Do not introduce UI frameworks or WebView2 instances merely to match a screenshot. Do not add redundant labels, menus, badges or animations. Maintain existing names/events/data behavior and code-file line limits. Any future visual overhaul needs an actual Windows screenshot review, not a claim based on source alone.
