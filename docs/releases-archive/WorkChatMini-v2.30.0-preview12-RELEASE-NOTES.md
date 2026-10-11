# WorkChat Mini v2.30.0-preview12 — Transaction Preflight & Backup Integrity

**Base:** full preview11 source. **Windows validation:** NOT YET RUN. The most recent Windows-confirmed source remains preview6 (Quick + Stress both 22/0/0); previews7–12 are cumulative unvalidated source changes. No AI provider, browser, Discord call policy, API key, or Story Memory schema change.

## Changes

1. **Strict checkpoint chapter manifest guard.** A new shared read-only guard requires a one-to-one correspondence between Memory chapters and checkpoint manifest names (case-insensitive), unique positive chapter numbers, safe Windows chapter filenames, valid SHA-256 digest shape and matching counts. Fixes the case where a manifest `[A,A]` previously could pass membership checks for Memory `[A,B]`, subsequently omitting B on rollback.
2. **Durable committed-conversation proof.** Chapter finalization now requires its newly-added chapter reference to match a committed assistant narrative message with the same chapter number in persisted `conversation.json`. Merely parsing a stale conversation is no longer sufficient. Existing chapter filename references must remain unchanged, and the new chapter file must be present and nonempty.
3. **Backup import checks branch-head integrity.** Beyond verifying each file's ZIP checksum, the importer validates the logical consistency of each active/archived branch-head checkpoint: manifest ID, snapshot payload and SHA-256 metadata, chapter names and referenced content-addressed objects. Missing/damaged branch-head objects prevent publication of the imported Story. The source Story and ZIP remain untouched; the incomplete staging folder is removed.
4. **Safe ZIP source traversal.** Backup enumeration now walks one directory level at a time, checks a file/directory before descending, refuses junctions/reparse points, detects case-insensitive filename collisions and enforces the existing 512 MiB/file, 8 GiB/archive and 100,000-file limits before compression. No external linked directory is traversed.
5. **Checkpoint source-link and conversation structure safety.** Capture rejects linked chapter directories/files; restore verification rejects linked checkpoint directories/objects. Snapshot conversation checks use the same parser as normal Story loading, including rejection of null message entries. No automatic repair or deletion is performed.
6. **Regression fixtures.** Added pure guard assertions for duplicate chapters, duplicated checkpoint filenames, path traversal chapter names, stale/uncommitted Chat acknowledgements, chapter reference replacement and missing import branch heads. These are wired into existing Story self-test groups without adding top-level cases (still 22).
7. **Deep Integrity Audit alignment.** Checkpoint deep scan reuses the same one-to-one manifest guard as rollback, avoiding two inconsistent interpretations of a valid checkpoint.

## Compatibility and boundaries

- No Story Memory schema migration, no new dependency or extra AI calls. Snapshot format 1 and compressed format 2 remain readable.
- The import preflight is deliberately stricter: previously exported backups containing broken branch-head checkpoints will be rejected until recovered from a good backup; it never repairs data automatically.
- Static checks cannot prove compiler/runtime correctness. In particular Windows WPF BUILD + Quick/Stress and manual backup/rollback review remain **pending**, grouped at the user's requested test milestone.
- Test with a disposable Story first; never delete `%LOCALAPPDATA%\WorkChatMini` to address compiler errors. Portable ZIP backups are not encrypted.
