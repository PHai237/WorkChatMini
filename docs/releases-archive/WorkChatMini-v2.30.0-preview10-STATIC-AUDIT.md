# WorkChat Mini v2.30.0-preview10 — Static Audit

**Source:** preview9 full ZIP. **Windows baseline:** preview6 Quick/Stress 22/0/0. **preview7–10 have not been Windows built or self-tested.**

The checks below verify source structure only. XML parse, Pygments delimiter checks, symbol-name matches and ZIP CRC **cannot** substitute for the C#/WPF compiler or live Windows execution.

## Structural checks

- **PASS** All C#/XAML <1000 lines: 181 files, longest (984, 'MainWindow.SelfTest.cs')
- **PASS** XAML/XML parser: []
- **PASS** XAML event names exist: 87 handlers, missing []
- **PASS** Changed C# punctuation balance: []
- **PASS** BUILD.bat encoding: no BOM, CRLF
- **PASS** AUTO-TEST.bat encoding: no BOM, CRLF
- **PASS** SOURCE-CHECK.bat encoding: no BOM, CRLF
- **PASS** Version metadata: 2.30.0-preview10
- **PASS** No compiled binaries in source: []
- **PASS** Browser/AI provider unchanged: 32 core files, edited: []
- **PASS** Expected importer/rescue helpers: 3 new helpers

## Added regression coverage (source present, not executed here)

- Interrupted commit with an orphan note absent from the checkpoint: completed recovery must rescue the note outside active chapters.
- Valid backup ZIP imported into a new disposable Story ID with matching conversation and chapter count, then test copy deleted.
- Manifest tampering rejected, current Story data preserved; existing checkpoint and branch isolation assertions retained.

## Limitations & Windows verification milestone

- **No .NET SDK/WPF/Windows compilation in this environment.** Current baseline preview6 remains the latest confirmed Windows PASS; previews7–10 must be integration-tested together.
- Run `AUTO-TEST.bat` at the user-selected milestone, target 22 PASS / 0 FAIL / 0 WARN for Quick and Stress.
- Manual check: create a disposable Story with chapters and branches, export its ZIP; import as a new Story; switch between copies; verify original unchanged; run deep checkpoint integrity read-only.
- Simulate interrupted commit in a disposable Story only; verify untracked note is under `branches/recovered` and canonical chapter state rolled back.
- ZIP checksums provide corruption detection, not authentication or encryption. Keep private backups private.
