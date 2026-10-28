# WorkChat Mini 2.60.0 — Quiet Workbench

## Product, audience and constraints

WorkChat Mini is a low-overhead Windows WPF/WebView2 workspace for switching between messaging, video, browser tabs and a personal reading library. It is usually used in a compact desktop window, including narrow sizes; the user wants quick switching and very little nonessential UI. This is **not** a new messenger UI: Messenger/Discord/Zalo/etc. retain their own original WebView2 content.

This pass draws on the Impeccable design workflow (`audit`, `distill`, `layout`, `typeset`, `polish`, `adapt`) documented at https://github.com/pbakaus/impeccable and https://impeccable.style/docs/. It is a hand-implemented WPF redesign; Impeccable's web/CLI detector did **not** compile or inspect native WPF at runtime.

## Visual direction

**Quiet Workbench** — understated desktop utility, not a gradient SaaS dashboard.

- Canvas: `#0E1315`; primary surface: `#171D20`; elevated surface: `#20282B`.
- Active/focus accent: `#A5DDC8`; soft state: `#203D38`.
- Text: `#F1F3F0`; secondary text: `#ADB8B6`; separators: `#303B3E`.
- Typography: Segoe UI Variable/Segoe UI for UI; only the small left-rail monogram is Georgia.
- Minimal depth: clear boundaries and interactive states instead of gradients or stacked cards.
- Use consistent 9–11 DIP radii, minimum 32–46 DIP target height, and visible focus/active state.

## Areas redesigned

1. **Navigation rail:** preserves all seven original app/reading icons, their click handlers and tags, adds a subdued WorkChat monogram and mint active marker. Original PNGs remain nearest-neighbor, not replaced with vectors.
2. **Web:** plain dark tab strip; tonal active/inactive tabs; address focus accent; simpler quick links landing page. Blank tab still starts no WebView2.
3. **Reading Hub:** vertical page scroll at the window minimum height; separate heading/action hierarchy; horizontally scrollable source strip and virtual tabs; searchable, wrap-safe filters; prominent Continue Reading; shelf rows become grouped, subtle surfaces. All original `x:Name` controls/events preserved.
4. **Settings:** preserves the existing custom flyout with consistent surfaces, hovered rows and toggle states. Top-level Updates and Exit remain directly reachable.
5. **Resource behavior:** no web frameworks, image generation assets, extra threads/timers, network integrations, third-party WPF dependencies, or extra WebView2 instances introduced.

## Compatibility and tests

- Release scripts and GitHub preflight remain `PUSH.bat` driven. Version synchronized in `VERSION.txt`, `.csproj`, `BUILD.bat` and MangaDex user-agent.
- Prior 2.59.1 Reading Hub data-safety changes remain intact. No migrations or user-data resets in this release.
- Statically verified: XML parses for all 8 XAML files; all event-handler names still resolve in C#; 139 MainWindow and 33 ReadingHub named controls preserved exactly; every C# source file remains under 1,000 lines.
- **Not verified here:** real Windows WPF compilation, runtime XAML resource resolution, exact rendered DPI/contrast, WebView interactions, Quick/Stress tests or real sites. The GitHub Windows preflight and manual visual QA are mandatory before treating the source ZIP as released.

## QA after PUSH.bat

Use your regular `PUSH.bat`. Check Actions status. On Windows inspect at 330×220, 480×360, 960×540 and maximized; test high-DPI screen and keyboard focus. Check each rail icon, Web new tab, address focus, settings, compact view, split view, reading search/filter/bookmark notes/Continue Reading, old library migration and update path. If a build/test fails, do not install the candidate; send the log.
