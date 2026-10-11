# WorkChat Mini v2.30.0-preview14 — Conversation Write Ordering & Recovery Barriers

**Based on:** the complete preview13 ZIP, not an older source. **Windows-tested baseline:** preview6 (Quick and Stress each 22 PASS / 0 FAIL / 0 WARN). **Previews7–14 have not received Windows compilation/runtime validation.**

## Changes

1. **Conversation saves are serialized across independent `StoryProjectStore` instances.** Previously the semaphore was an instance field. Two stores could both serialize/write `conversation.json`; the earlier snapshot could finish after a newer one and replace it. A shared per-path coordinator assigns a monotonically increasing request number before background serialization. A completion older than the newest *successfully written* version skips its disk write. Cancellation or I/O failure does not advance the persisted version, so subsequent saves remain possible. The existing deep `StoryMessageSnapshot` capture and atomic temp-file replacement remain in place.
2. **Recovery markers now block new Story transactions.** `HasPendingTransaction` also detects an unfinished `branches/pending-rescues` cleanup record. `BeginCommitAsync` and new-branch initialization now refuse to start on that half-recovered state; Story activation can still run `RecoverIfNeededAsync` and clear the record. This avoids overlapping two rescue/archive cleanups.
3. **Portable backup waits for Story mutations.** Backup export holds the existing per-Story `StoryOperationGate` through traversal, ZIP creation, integrity verification and source re-hashing. Branch switch/commit/rollback cannot run in parallel *in the same process*. Autosaves and external file editors remain outside that gate, so the existing end-to-end source re-hash and final journal checks are retained.
4. **Additional regression coverage:** deterministic out-of-order completion, failed-write recovery and cross-store save roundtrips; refusal to commit while a rescue marker exists; backup waiting for a Story operation lock. Assertions are included in the existing Story memory self-test group. The 22 top-level Quick/Stress test count is unchanged.

## Compatibility, limitations, and verification

- No Story Memory schema change (still v6), no migration, no added package, no new AI request, no changes to shell/browser, Discord call policy, AdBlock, notification policy or provider code.
- Shared locks and sequence numbers are **process-local**. Do not run two copies of WorkChat against the same project. Conversation saves issued from a stale UI after a branch switch must still be prevented by the UI's existing busy controls; the coordinator orders requests but cannot determine which branch a stale caller intended.
- Backups are **not encrypted**. Keep them private and export only when the Story is idle.
- No Windows SDK/WPF compiler is available in the current environment. XML/lexer/source checks are not evidence that generated XAML code, WPF UI, runtime concurrency, or storage operations pass on Windows.
- At the agreed grouped milestone, execute `BUILD.bat` and `AUTO-TEST.bat` on Windows and manually test Story switch, backup export/import, branch rollback, recovery, and Chat during AI streaming using disposable Story data. Do not delete or reset `%LOCALAPPDATA%\WorkChatMini`.
