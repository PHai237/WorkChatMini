# WorkChat Mini 2.36.0 — Stability Audit

## Confirmed defects corrected

1. **Unused update staging:** `AppUpdateWindow` previously kept a fully verified executable in `WorkChatMini-Updates/Pending/<GUID>/` when the Updates dialog closed, the user chose another package, or rechecked the feed without installing. It now calls path-restricted `AppUpdatePackage.DiscardPrepared` for superseded or abandoned staging. The successful updater handoff deliberately retains staging until the updater completes. Manual ZIP files are never deleted.
2. **Stale CPU process samples:** `AnalyzeProcessCpu` returned early on an empty process sample without dropping saved PID baselines. A later PID reuse could bias the next sample. It now resets the baselines after a valid empty snapshot.
3. **Unobserved diagnostic write errors:** WebView failure reporting runs in a fire-and-forget task; source now logs exceptions from telemetry/report writing inside the task.

## Tests added to existing 22-case suites

- Environment self-test: staged update is removed; an EXE outside generated Pending is refused and retained.
- Telemetry process sampling self-test: an empty captured process snapshot clears stale CPU baseline entries.
- Existing top-level Quick/Stress case counts remain **22**; these are additional assertions, not new top-level cases.

## Static verification

- 206 C#, 5 XAML, 6 parsed XML/XAML files; maximum source file 991 lines (`_Internal/Source/MainWindow.SelfTest.cs`).
- 166 XAML event references found with no missing C# method names.
- GitHub Actions workflow YAML parsed; 2 jobs.
- Version 2.36.0 consistent across VERSION.txt, csproj, and BUILD.bat; BUILD.bat CRLF.
- ZIP contains source only; binary release remains GitHub Actions' responsibility.

## Not yet verified

- Windows C# compile, Quick/Stress runs, actual installed updater handoff, long Discord calls, RAM trends, and AI Story writing quality require Windows runtime tests.
- Static audit reviewed the key lifecycles, but cannot prove all application code bug-free.

The archive checksum is emitted separately in the QA result to avoid a circular dependency.
