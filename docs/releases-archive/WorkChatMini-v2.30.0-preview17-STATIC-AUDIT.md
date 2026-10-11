# WorkChat Mini v2.30.0-preview17 — Static Audit

Date: 2026-10-10. Based on the full preview16.1 ZIP.

## Checks completed in this environment

- **193 C#/XAML source files**, largest **984 lines** (`_Internal/Source/MainWindow.SelfTest.cs`); all below the required 1,000 lines.
- **5 XAML + 1 project XML** parsed successfully as XML. This does not confirm WPF XAML semantic compilation.
- Checked **88 distinct XAML event-handler names** against C# source names; no missing names detected. Signatures were **not** compiled.
- Verified version labels in `VERSION.txt`, project file, BUILD and AUTO-TEST scripts; batch encoding has CRLF and no UTF-8 BOM.
- No generated EXE, DLL, PDB or `bin/obj` content under source.
- Checked unqualified `new Point(...)` and ambiguous `Orientation` patterns in StoryPanel source; the preview16 WPF namespace fix remains present.
- Compared source changes against preview16.1: limited to long-session Story AI/UI/data model/tests and release metadata; browser/Discord/AdBlock/provider network files unchanged.
- Independently checked the long accented search fixture and bounded segment math in Python. This is not a run of the application's C# self-test.

## Unverified

- `dotnet` and PowerShell are not available in this environment. **Cannot execute Release BUILD, `SOURCE-CHECK.bat`, WPF runtime, Quick/Stress or Gemini streaming tests here.**
- Self-test assertions were added in source; their execution and outcomes remain **pending Windows validation**.
- Last externally reported Quick/Stress 22/0/0 baseline: preview14.1. Preview16 had a CS0104 compile failure; preview16.1 contained the hotfix but has not been confirmed by a subsequent Windows log in this conversation.

## Packaging

- Full source (no personal AppData), **293 files**, uncompressed file bytes 3,588,479.
- Do not replace the working installation or run destructive restore operations with this preview until Windows tests pass.
