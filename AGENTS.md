# WorkChat Mini design and code agent guidance

This is a native WPF/.NET 8 Windows application, NOT a React SPA. Use `_Internal/Source/WorkChatTheme.xaml` and `WorkChatControls.xaml` for existing theme/control tokens and `MainWindow.SettingsFlyout.cs` for Settings navigation. Respect user preference: sophisticated dark dashboard surfaces with strong hierarchy and compact controls; avoid decorative filler and unnecessary duplication.

For any UI change, consult `docs/UI_SKILLS_2.60.4.md` and `docs/UI_POLISH_2.60.5.md`, use the Impeccable (operate-mode), Taste Skill (audit-first), Better Interface (accessibility/layout/type) and motion guidance described there. They are reference methods, not requirements to add npm dependencies. Do not use web-only GSAP for the native WPF shell. Use short, one-shot WPF animations only when meaningful, and respect Windows animation preferences.

Preserve existing WebView2 session/profile, pixel-art app icons, browser tabs, Reading Hub data, notifications, memory saver, and update mechanism. Keep every C#/XAML file below 1000 lines; all Windows .bat files CRLF. Only ship source ZIPs that pass static audits. User publishes using PUSH.bat. Windows compilation/UI screenshots are still necessary before claiming a release passed.
