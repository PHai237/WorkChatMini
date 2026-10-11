# WorkChat Mini v2.30.0-preview14 — Static Audit

**Source:** full preview13. **Last validated Windows release:** preview6; Quick and Stress 22 PASS / 0 FAIL / 0 WARN. **Previews7–14 were NOT compiled or tested on Windows.** No dotnet SDK / WPF compiler is installed here.

## Local checks (do not prove runtime correctness)

- **PASS** All C#/XAML <1000 lines — 189 files, longest (984, 'MainWindow.SelfTest.cs')
- **PASS** All XAML/csproj parse as XML — []
- **PASS** XAML handler names found — 87 names, missing []
- **PASS** Modified C# balanced lexically — []
- **PASS** BUILD.bat no BOM / CRLF
- **PASS** AUTO-TEST.bat no BOM / CRLF
- **PASS** SOURCE-CHECK.bat no BOM / CRLF
- **PASS** Version strings aligned — 2.30.0-preview14
- **PASS** Assembly/FileVersion
- **PASS** No generated binaries
- **PASS** Browser/Discord/AI provider files unchanged — 32 protected, changed=[]
- **PASS** No NuGet dependency changes
- **PASS** Cross-store write ordering is wired
- **PASS** Failed saves do not advance versions
- **PASS** Rescue markers block new commit
- **PASS** Branch initialized only after recovery preflight
- **PASS** Backup holds operation lease
- **PASS** New regression tests connected

## Modified/new source relative to preview13

- `_Internal/Source/AI/StoryBackupSelfTest.cs`
- `_Internal/Source/AI/StoryBackupService.cs`
- `_Internal/Source/AI/StoryBranchStore.cs`
- `_Internal/Source/AI/StoryCheckpointCommitSelfTest.cs`
- `_Internal/Source/AI/StoryCheckpointStore.cs`
- `_Internal/Source/AI/StoryConversationWriteOrder.cs`
- `_Internal/Source/AI/StoryConversationWriteOrderSelfTest.cs`
- `_Internal/Source/AI/StoryProjectStore.cs`
- `_Internal/Source/WorkChatMini.csproj`

## Regression assertions added, pending Windows execution

- Old conversation save completion cannot overwrite a newer successful save; failed write does not mark its version persisted.
- Two independent project-store instances can sequentially save and read the same disposable conversation.
- New chapter commits are rejected while orphan manuscript rescue is pending.
- Backup export waits for the per-Story branch/rollback operation lease.

## Limits and next validation milestone

XML/lexical checks do **not** compile C#/WPF or prove response ordering, crash integrity, true atomic storage, performance, XAML generated method signatures or provider behavior. The sequence coordinator and mutation gate are process-local. Full Windows BUILD (warnings-as-errors), Quick + Stress and disposable Story manual regression are still required; no test was requested at this intermediate preview.
