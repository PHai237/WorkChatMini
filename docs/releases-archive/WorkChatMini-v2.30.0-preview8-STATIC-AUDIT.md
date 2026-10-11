# WorkChat Mini v2.30.0-preview8 — Static Audit

**Date:** 2026-10-10. **Input:** Full source v2.30.0-preview7 ZIP. **Windows baseline:** v2.30.0-preview6 (Quick 22/0/0; Stress 22/0/0, from user-supplied reports).

**Scope:** Python structural checks under Linux, XAML/XML parser, static XAML handler-name presence, lexical delimiter scan on modified C#, ZIP integrity. These checks **do not** run `dotnet`, WPF XAML compiler, PowerShell SOURCE-CHECK, UI runtime, Quick, Stress, Gemini, or a real branch restore. Preview7 and preview8 remain **not Windows-validated**.

## Results

- **PASS — C#/XAML files below 1000 lines:** 175 files, maximum 984 (MainWindow.SelfTest.cs)
- **PASS — XAML/csproj XML parse:** 6 files; no XML parser errors
- **PASS — XAML handler-name presence:** 87 unique handler names; missing []
- **PASS — Changed C# delimiter balance (lexer):** 10 changed/new C# files
- **PASS — Version labels synchronized:** 2.30.0-preview8
- **PASS — Windows batch header/CRLF/UTF8-no-BOM:** [('BUILD.bat', True, True, True), ('AUTO-TEST.bat', True, True, True), ('SOURCE-CHECK.bat', True, True, True)]
- **PASS — No generated binary in packaged source:** []
- **PASS — Browser/Discord/provider source untouched:** 32 protected source files; changed []
- **PASS — Changes isolated to Story and package metadata:** []

## Regression assertions included in the source (not executed here)

- 1,000-message search including invisible older messages; match navigation window cap, empty/nonmatching query.
- Corrupt/null memory JSON rejects opening for editing; detached memory proposal clone and durable applied receipt round-trip.
- Retains all previous top-level tests and their assertions. This is **not** a claim that they pass on Windows.

## Manual Windows checks for a later combined validation batch

- Release BUILD with warnings-as-errors; AUTO-TEST Quick and Stress (22 cases each).
- Ctrl+F, search old Chat, Previous/Next, Earlier/Later context, Latest; window scaling and keyboard focus.
- Crash/failure recovery with a disposable Story (do not corrupt personal data); verify `story-memory.json` preservation.
- Proposal Apply/Revision across restart, checked on a disposable Story.
- Stop and restart streaming; monitor provider failures and late chunks.

## Limitations

No .NET SDK/Windows WPF compiler is installed in this environment, so C# type checking, binding validation, resource/event signatures and UI behavior remain unverified. Source unchanged for protected browser/provider areas does not guarantee zero runtime regressions.
