# WorkChat Mini v2.30.0-preview14.1 — Static Audit

**Source:** exact full preview14 ZIP, patched without reverting features. **Windows quick preview14:** 21 PASS / 1 FAIL / 0 WARN. **Last Windows Quick+Stress passing baseline:** preview6 22/0/0. **Preview14.1:** static checked here; Windows BUILD, Quick, Stress not yet executed.

## Audit checks

- **PASS** Source file limit — 189 files; longest (984, 'MainWindow.SelfTest.cs')
- **PASS** WPF XAML and csproj XML parse — []
- **PASS** XAML handler names present — 87 names, missing=[]
- **PASS** Changed C# lexical balance — []
- **PASS** BUILD.bat no BOM and CRLF
- **PASS** AUTO-TEST.bat no BOM and CRLF
- **PASS** SOURCE-CHECK.bat no BOM and CRLF
- **PASS** Version alignment — 2.30.0-preview14.1
- **PASS** Assembly/FileVersion
- **PASS** No generated executables or object folders
- **PASS** Only intended source files changed — ['_Internal/Source/AI/StoryCheckpointCommitSelfTest.cs', '_Internal/Source/AI/StoryCheckpointStore.cs', '_Internal/Source/WorkChatMini.csproj']
- **PASS** Interrupted commit abort reuses ID
- **PASS** Paired restore journal recovery precedes generic recovery
- **PASS** Legacy restore journal reuses existing ID
- **PASS** Regression fixtures cover stable ID, replay, legacy journal
- **PASS** Unchanged browser/provid./shell

## Root cause, scope and limitations

The original test checks for `branches/recovered/<commit-transaction-id>/selftest-unsnapshotted-note.txt`, while preview14 assigned a second independent ID to `StoryPendingRestore`. The rescued file may have existed under the restore ID; the submitted report does not prove it was erased. The hotfix reuses the original ID for new aborts, and the recovery logic now handles overlapping abort/restore journals atomically at the state-machine level (without treating them as unrelated replays). Legacy restore IDs are retained if an older journal already exists. The regression test asserts payload content, journal retirement, repeated recovery and old-format paired-journal replay.

**Audit restrictions:** lexical/XML/structural checks are not C# compilation, WPF build, filesystem crash-injection or runtime tests. The regression fixtures are source additions and have not run on Windows. No .NET SDK/WPF compiler is installed in the container. Do not claim a full 22/0/0 until user reports it.

## Modified source files

- `_Internal/Source/AI/StoryCheckpointCommitSelfTest.cs`
- `_Internal/Source/AI/StoryCheckpointStore.cs`
- `_Internal/Source/WorkChatMini.csproj`
