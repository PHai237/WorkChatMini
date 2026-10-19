# WorkChat Core 2.47.0 — Responsive UI Audit

## Changes relative to 2.46.0

- Flyouts: Ad blocker, VPN and Session Health now have scrollable content bounded by a height calculated from the current WPF window height. Width-only **and** height-only resizes update those bounds without causing an unnecessary WebView2 zoom pass.
- Narrow actions: VPN actions and Session Health actions now wrap rather than run outside the window. The Ad blocker state badge is hidden when the window is too narrow.
- Browser: Address-bar history suggestion width no longer forces a 220-DIP minimum; the new-tab landing panel can scroll when the view is short; loading UI has no fixed 250-DIP width.
- Dialogs: Updates and Clear Browsing Data are sized against the actual owner window. Their content scrolls as space shrinks; Update dialog no longer mentions the removed AI Story module.
- No additional WebView2 instances or new work on user profiles; split view/resource saver/audio safety behavior is retained.

## Geometry checks added

A new `Responsive layout at 100–200% DPI` Quick Self-Test exercises 10 physical widths (320–3840px) x 7 heights (240–2160px) x 5 DPI scales (100/125/150/175/200%), a total of **350 geometry combinations**. It asserts that split view and the tiny Apps mode cannot overlap, that visible panels leave some WebView space, and that update/clear dialogs do not exceed owner geometry. It also checks that the real Session Health panel received the height cap. This is a **runtime geometry/unit self-test, not a graphical screenshot test**.

The same 350 geometry combinations were checked independently in this environment using the current formulas (PASS), and all XAML and YAML were parsed (PASS). This environment cannot execute Windows WPF compilation or visually render WebView2. Do not claim every possible physical monitor/DPI has been visually tested. CI must still run compile, Quick/Stress, and manual resize checks.

## Manual Windows visual QA matrix

Test window widths **330, 420, 519, 520, 759, 760, 1024, 1366, 1920 DIP** and heights **220, 320, 540, 768, 1080 DIP**, at 100%, 125%, 150% and 200% display scaling where available. For each, inspect:

- Sidebar rail / Apps fallback / toolbar-collapsed handle
- Ad blocker / VPN / Session Health panels; ability to reach and click Close
- Updates dialog; buttons at bottom reached by scroll
- Clear Browsing Data confirmation dialog
- Web new-tab shortcuts / history suggestions
- Split View transition around 760 DIP; both panes responsive, audio and Discord call uninterrupted
- Switch between monitors with different DPI scales; check popup location and saved window geometry

Do not automatically clear app data or change WebView2 profiles as part of the UI test.

## Known boundaries

The application shell cannot force third-party websites to reflow their own CSS. WebView2 pages may still show site-provided horizontal scrolling or mobile/desktop layouts at narrow widths. Real behavior on 2 monitors and during video calls needs Windows visual testing. The WPF source archive was not compiled in this environment.
