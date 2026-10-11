# WorkChat Mini v2.30.0-preview14.1 — Interrupted Commit Rescue Hotfix

**Source:** Full `v2.30.0-preview14` + narrowly scoped recovery fixes. **Trigger:** Windows Quick self-test on 2026-10-10 reported 21 PASS / 1 FAIL / 0 WARN, within `Story memory persistence and retrieval`. Build and self-test completed sufficiently to report a result, but the test detected the wrong recovery path.

## Root cause

`StoryCheckpointCommitSelfTest` expects unsnapshotted manuscript notes in `branches/recovered/<pending commit TransactionId>/`. In preview14, `AbortCommitCoreAsync` created a **new random TransactionId** for the restore journal. `StoryRestoreRescue` rescues under the restore journal's ID, so the test's exact directory did not match the rescue directory. The error message saying that a file was discarded **does not by itself prove the contents were deleted**; it could have been preserved under the second ID. See the specific test's expected path and abort journal creation.

## Fixes

1. **Keep the same transaction identity through an interrupted commit and its abort.** The newly-created `StoryPendingRestore.TransactionId` is now `pending.TransactionId`, keeping rescue paths predictable for users, tests and post-crash manual recovery. No snapshot/Memory schema changes.
2. **Recover a pair of journals in the correct order.** When `pending-restore.json` and `pending-commit.json` both exist, the recovery path first processes them as a single abort, validates the relationship, and retires the commit marker only after restore. This closes the crash window between creating a restore journal and setting the `Aborting` flag. A contradictory completed-commit + pending restore combination is rejected rather than silently modifying data.
3. **Legacy in-flight compatibility.** Existing preview14 restore journals with an independently generated transaction ID remain valid if both checkpoint IDs match the pending commit. Those preexisting restore IDs are retained as the rescue destination; only *new* abort journals reuse the original ID.
4. **Regression tests:** in addition to the original unsnapshotted-note assertion, verify no pending markers remain and recovery replay preserves the note. Add a legacy two-journal crash fixture and validate exact-once recovery, correct rescue directory, retained Memory and conversation, and subsequent activation.

## Constraints and validation status

- **No changes** to browser, Discord call guard, AdBlock, Gemini/OpenAI providers, personal data storage format, backup ZIP format or Story Memory v6. No new dependencies or AI requests.
- Existing real recovered notes stored under the old independent transaction ID are **not relocated or deleted**. Continue to inspect `branches/recovered/` or Storage Health if needed.
- This package is **static-audited only** here: no Windows .NET SDK/WPF compiler or running Windows filesystem is available. Windows `AUTO-TEST.bat` must confirm Quick and Stress. Do not reset/delete `%LOCALAPPDATA%\WorkChatMini` to resolve a test failure.
- Preview6 is the latest baseline with both Quick/Stress 22/0/0. Preview14 was compiled and Quick-tested on Windows (Quick 21/1/0); no preview14 Stress report was supplied.
