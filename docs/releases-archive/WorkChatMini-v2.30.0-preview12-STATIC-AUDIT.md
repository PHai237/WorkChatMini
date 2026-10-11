# WorkChat Mini v2.30.0-preview12 — Static Source Audit

**Baseline:** preview11 source; **latest validated Windows baseline:** preview6 (Quick and Stress each 22 PASS / 0 FAIL / 0 WARN). **No .NET SDK in this environment.** Windows BUILD + Quick/Stress have not been run for previews7–12.

## Structural checks (not runtime/compiler proof)

- **PASS** C#/XAML files < 1000 lines: 185 files; max (984, 'MainWindow.SelfTest.cs')
- **PASS** XML/XAML/csproj well formed: []
- **PASS** XAML event handler method-name references: 87 names; missing=[]
- **PASS** Changed C# lexical punctuation balance: []
- **PASS** BUILD.bat CMD encoding: no BOM, CRLF
- **PASS** AUTO-TEST.bat CMD encoding: no BOM, CRLF
- **PASS** SOURCE-CHECK.bat CMD encoding: no BOM, CRLF
- **PASS** Version metadata labels: 2.30.0-preview12
- **PASS** Numeric assembly revision: 2.30.0.12
- **PASS** No compiled binaries in source ZIP: []
- **PASS** Browser/Discord/provider code unchanged: 32 files, changed=[]
- **PASS** New guard source files present: StoryChapterCommitGuard.cs,StoryCheckpointChapterGuard.cs,StoryPortableImportGuard.cs,StoryTransactionGuardSelfTest.cs
- **PASS** New regression tests wired: existing 22 top-level test groups
- **PASS** Portable import checks branch heads: 
- **PASS** Restore and deep scan share manifest guard: 
- **PASS** Chapter commit checks persisted acknowledgement: 
- **PASS** No extra NuGet dependencies: 

## Changed/new source files

- `_Internal/Source/AI/StoryBackupImport.cs`
- `_Internal/Source/AI/StoryBackupSelfTest.cs`
- `_Internal/Source/AI/StoryBackupService.cs`
- `_Internal/Source/AI/StoryChapterCommitGuard.cs`
- `_Internal/Source/AI/StoryCheckpointChapterGuard.cs`
- `_Internal/Source/AI/StoryCheckpointCommitSelfTest.cs`
- `_Internal/Source/AI/StoryCheckpointIntegrityAudit.cs`
- `_Internal/Source/AI/StoryCheckpointStore.cs`
- `_Internal/Source/AI/StoryPortableImportGuard.cs`
- `_Internal/Source/AI/StoryTransactionGuardSelfTest.cs`
- `_Internal/Source/WorkChatMini.csproj`

## Regression scenarios added to source (not executed on Windows)

- Duplicate checkpoint A/A vs Memory A/B rejected.
- Duplicate chapter numbers and unsafe filenames rejected.
- Stale, parseable conversation without committed chapter acknowledgement rejected.
- Existing chapter reference may not be silently replaced during commit.
- Missing branch-head checkpoint rejects import before publication.

## Known limits and next integration milestone

- C# semantics, generated WPF bindings, native UI behavior, crash/power-loss recovery and app RAM/CPU performance **not verified** by these checks.
- Verify Windows BUILD with warnings-as-errors, then AUTO-TEST Quick + Stress (22/0/0 target).
- Test on a disposable Story: backup round-trip, archive/restore branches, rollback and deliberately damaged ZIP.
- Keep `%LOCALAPPDATA%\WorkChatMini` intact; never delete real data to troubleshoot builds.
