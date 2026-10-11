# WorkChat Mini v2.30.0-preview13 — Static Audit

**Source base:** preview12. **Latest Windows validated baseline:** preview6 Quick & Stress 22 PASS / 0 FAIL / 0 WARN. **Preview7–13 Windows BUILD/Quick/Stress not performed.** No .NET SDK/WPF available in this Linux environment.

## Structural and source inspections (not compiler/runtime proof)

- **PASS** C#/XAML files under 1000 lines — 187 files, longest (984, 'MainWindow.SelfTest.cs')
- **PASS** XAML/XML/csproj parse — []
- **PASS** XAML event method-name references — 87 names, missing []
- **PASS** Changed C# punctuation lexical balance — []
- **PASS** BUILD.bat no BOM, CRLF
- **PASS** AUTO-TEST.bat no BOM, CRLF
- **PASS** SOURCE-CHECK.bat no BOM, CRLF
- **PASS** Version metadata aligned — 2.30.0-preview13
- **PASS** Assembly/FileVersion 2.30.0.13
- **PASS** No generated build binaries — []
- **PASS** Browser/Discord/AI provider sources unchanged — 32 files; modified=[]
- **PASS** Six branch mutation/recovery methods acquire gate
- **PASS** Five checkpoint public mutation/recovery methods acquire gate
- **PASS** Branch inner restore avoids reentrant gate
- **PASS** Abort marker persisted after restore journal
- **PASS** Completed journal rechecks durable state
- **PASS** Unknown restore journal rejected
- **PASS** Regression fixtures present and wired
- **PASS** No added NuGet dependencies — [('Microsoft.Web.WebView2', '1.0.4258.31'), ('System.Security.Cryptography.ProtectedData', '8.0.0')]

## Source changes relative to preview12

- `_Internal/Source/AI/StoryBranchSelfTest.cs`
- `_Internal/Source/AI/StoryBranchStore.cs`
- `_Internal/Source/AI/StoryCheckpointCommitSelfTest.cs`
- `_Internal/Source/AI/StoryCheckpointStore.cs`
- `_Internal/Source/AI/StoryOperationGate.cs`
- `_Internal/Source/AI/StoryOperationGateSelfTest.cs`
- `_Internal/Source/WorkChatMini.csproj`

## New automated assertions included in source (NOT YET EXECUTED)

- Two store instances cannot enter the same Story operation gate simultaneously.
- Other Stories stay independent; cancelled pending gate entries do not acquire leases.
- Two concurrent branch renames never create partially written branch metadata.
- A completed aborted-commit cleanup marker does not replay an old rollback.
- Contradictory committed/aborting markers and mismatched restore journals are refused.

## Known gaps / test milestone

- XML and lexer checks cannot prove WPF compilation, generated event signatures, cross-thread behavior or crash/power-loss durability.
- Gate is in-process only, not an inter-process mutex. Do not open two WorkChat processes on the same Story data.
- Run Windows BUILD (warnings-as-errors), Quick + Stress, and manual disposable-Story branch/commit/restore tests at the agreed consolidated milestone; no request to test each preview.
- Never delete `%LOCALAPPDATA%\WorkChatMini` to fix code issues.
