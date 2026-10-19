# WorkChat Mini Core 2.48.0 — Modern UI

## User-facing changes

- Retired the 1/16 window-area preset from the size menu, allowed presets and restore code. Existing saved `WindowSizePreset` and `LastNormalWindowSizePreset` values of `1/16` migrate to `1/8` when settings load. No data reset.
- Unified native WPF shell palette with dark navy surfaces, clearer text/borders, subtle color accents, and rounded 12–14 DIP controls.
- Expanded app rail from 54 to 60 DIP, and service buttons from 34 to 40 DIP. The existing narrow-window Apps menu remains under 520 DIP; Split View stays at >=760 DIP.
- Replaced the six raster pixel-art icon resources with lightweight resolution-independent `DrawingImage` vectors (resource keys retained). No new external packages or WebView2 instances.
- Added short 120 ms hover feedback on navigation buttons and a visible selected/keyboard-focus state. Animation is limited to WPF chrome and never targets WebView2 video.
- Aligned browser address controls, popup/tooltips, action panels and Update dialog palette to the shell. Added modern Segoe UI Variable preference with Segoe UI fallback.
- Windows 11: request native dark caption and rounded system corners through DWM where supported. Failures are cosmetic and ignored. Older Windows retain native frame behavior.
- Split View, Messenger, Discord, YouTube, Zalo, Facebook, Web browser, Resource Saver, app data and update logic remain intact.

## Technical layout

New partial file: `_Internal/Source/MainWindow.Chrome.cs`.
New resource dictionaries: `WorkChatTheme.xaml`, `WorkChatIcons.xaml`, `WorkChatControls.xaml`.
MainWindow.xaml moved icon style into its own dictionary to stay under the 1000-line source guard.
Quick self-test adds `Modern shell tokens and retired size preset`; `SOURCE-CHECK.ps1` adds consistency checks for the retired preset and dictionaries.

## Limitations / release gate

- XAML XML parsing is **not** WPF/BAML compilation. Static validation cannot prove a `StaticResourceExtension`, animation template, or new DrawingImage compiles on the Windows toolchain.
- Geometry permutations exercise code-equivalent rules, **not** actual window rendering screenshots at each resolution/DPI.
- No .NET/WPF SDK is available in this Linux runtime. Windows Compile Preflight, Quick/Stress and manual interactive checks have **not** run for 2.48.0.
- Do not claim memory savings or changed video/call behavior without measurement.

### Recommended Windows checks before release

1. `PUSH.bat` from a clean extracted folder; wait for Windows preflight, then Release Build and Quick/Stress to finish.
2. Verify app launch on Windows 10 (fallback titlebar) and Windows 11 (dark caption/rounding).
3. Check app navigation/menus at widths 330, 480, 760, 1080 DIP and 100%, 125%, 150%, 200% DPI.
4. Confirm saved old 1/16 preset becomes 1/8 and stays that way after restart. All four supported sizes should select and restore correctly.
5. Validate vector icons, keyboard focus and hover; check YouTube video + Messenger Split and Discord call + Resource Saver to rule out regressions.
6. Confirm updates and existing local profiles remain untouched.
