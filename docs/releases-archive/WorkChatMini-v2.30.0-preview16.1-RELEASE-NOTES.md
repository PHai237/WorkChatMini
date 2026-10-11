# WorkChat Mini 2.30.0-preview16.1 — WPF Point Compile Hotfix

**Based on:** complete `WorkChatMini-source-v2.30.0-preview16.zip` (no rollback).  
**Cause:** Windows Release build of preview16 reported `CS0104` for `Point` at `StoryPanel.ConversationPaging.cs:50` and `:67`. The project enables both `UseWPF` and `UseWindowsForms`, so unqualified `Point` is ambiguous between `System.Windows.Point` and `System.Drawing.Point`.

## Changes

- Updated both WPF `TranslatePoint(...)` anchor calls to construct **`new System.Windows.Point(0, 0)`** explicitly. No pagination, scrolling, messages, Story Memory or AI logic was changed.
- Added a StoryPanel-specific `SOURCE-CHECK.ps1` guard that flags future unqualified `new Point(...)` expressions in StoryPanel source.
- Updated version, build banner, metadata, and release notes. All changes are kept in the complete source ZIP. Browser/Discord/AdBlock/provider code is unchanged.

## Validation and acceptance

- Static checks cover the two compiler-reported sites, related unqualified `Point` usages, XML/XAML parse, source length, archive integrity and metadata consistency. **Static checks cannot prove Windows compilation**.
- Preview16 Windows BUILD: **FAILED** with the two `CS0104` errors above. Preview16.1 Windows BUILD, Quick and Stress: **NOT YET RUN** in this environment (no .NET Windows build environment here).
- Last Windows-validated baseline: preview14.1 (Quick 22/0/0, Stress 22/0/0). Preview15–16 are not yet Windows-validated.

**Next step:** Extract preview16.1 into a clean folder, run `BUILD.bat`. If compilation succeeds, run `AUTO-TEST.bat` once; share any new compiler output or Quick/Stress failures. Keep personal Story data under `%LOCALAPPDATA%\WorkChatMini` intact.
