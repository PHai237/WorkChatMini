# WorkChat Mini v2.30.0-preview11 — Atomic Memory & Crash-Resilient Restore

**Source foundation:** Full v2.30.0-preview10 ZIP. **Status:** source/static review only; Windows WPF BUILD, Quick and Stress have not been run for preview11. Latest confirmed Windows baseline remains **preview6, Quick + Stress 22 PASS / 0 FAIL / 0 WARN**. Preview7–11 are the accumulated unvalidated changes; run integrated testing when the user selects a milestone.

## 1. Transaction-isolated Story Memory

- In `StoryPanel.Memory.cs`, chapter writes and bookkeeping-only updates now operate on a separate JSON-round-tripped `StoryMemoryDocument` graph. Both the chapter list and nested fact/character/event objects are detached from the live panel's memory.
- The active `_memory` reference changes **only after** memory persistence succeeds, and for chapter commits only after durable conversation persistence and checkpoint journal completion. If a nonchapter Memory save fails, previously the live RAM could contain facts that were never written to disk; the new flow keeps the prior state in memory.
- Snapshot creation/parsing is dispatched to the worker pool, so large Story Memory clones avoid synchronous WPF JSON work. No schema changes and no additional Gemini/OpenAI requests.

## 2. Commit pre-finalization checks

- `CompleteCommitAsync` now checks the persisted Memory against its original checkpoint: exactly one new chapter, no missing/replaced existing chapter references, valid unique chapter numbers, and a present nonempty new chapter file; conversation must still parse.
- A missing/incomplete write keeps the commit journal uncommitted, so the existing abort/recovery path can restore the checkpoint rather than falsely declaring success.
- This is a consistency guard, not a guarantee against power loss/disk controller failures. It does not check prose literary quality or prove the AI response was semantically correct.

## 3. Crash-resilient manuscript rescue after restore

- Before changing the live chapter directory, restore writes a durable `branches/pending-rescues/<transaction>.json` cleanup marker separate from `pending-restore.json`.
- After the canonical Memory, conversation and chapters are restored, cleanup moves unsnapshotted/externally edited original files into `branches/recovered/<transaction-id>`; an interruption after retiring the restore journal can resume cleanup on Story activation, without re-rolling back the canonical Story.
- If the prior restore was interrupted after archiving original chapters, a retry preserves files changed in the partial live directory under `branches/recovered/<transaction-id>-replay` before recreating the target chapters.
- Cleanup exceptions retain the original archive and marker and are logged. An unsafe junction/reparse-linked archive cannot be silently traversed/deleted.

## 4. Backup and checkpoint validation integration

- Storage Health marks deferred manuscript rescue as a pending operation. Verified ZIP backup refuses to run if there are deferred rescue markers, including re-checks at publication. ZIP import rejects an archive carrying such unfinished cleanup metadata.
- Checkpoint validation rejects duplicate/invalid chapter numbers, unsafe chapter filename characters, and Windows reserved device names; missing/invalid identifiers fail closed rather than accidentally throwing null-reference exceptions.
- Staged restored chapter files are hashed again after copying from verified content-addressed objects, before any live file is replaced.

## 5. Regression coverage added (not executed on Windows yet)

- Detached Memory transaction copy, including nested chapter objects, cannot change active RAM data.
- A post-restore interrupted rescue marker with an archived unpublished note is replayed while the already committed Story remains unchanged.
- A chapter commit lacking durable new chapter data cannot be falsely finalized and can be rolled back through its journal.
- All these checks are inside existing Story self-test groups; **22 top-level Quick/Stress cases remain**.

## Unchanged/limitations

- No code changes to browser service routing, Discord call guard, WebView2 sleeping, notification policy, AdBlock, AI providers or third-party packages.
- Story Memory v6 and checkpoint compressed snapshot v2 remain readable; markers are additive internal transaction metadata.
- Linux static checks cannot compile WPF or run Windows event handlers. **Preview11 has not passed Windows BUILD or AUTO-TEST.**
- Do not delete `%LOCALAPPDATA%\WorkChatMini` to fix build failures; protect real Story data. Test rollback with disposable Story data first.
