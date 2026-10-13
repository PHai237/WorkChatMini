# WorkChat Mini 2.41.0 — Memory coverage & safe partial repair

## Purpose

Memory is not complete merely because a character or lore entry exists. Version 2.41.0 records a per-chapter bookkeeping result and offers repair for *partially* populated stories without rewriting established facts. This is a source-only change until Windows builds and tests pass.

## New behavior

- Chapter receipts: `Reviewed` (AI JSON processed), `Skipped` (Auto-bookkeeping off or not eligible), `Incomplete` (failed/unusable/unsafe extraction) and implicit `Unknown` for historical chapters. `Reviewed` is **not** a statement of factual completeness.
- Overview warning and writer-context caveat when some chapters have unreviewed Memory. No claim that absent memories prove absent events.
- For empty structured Memory, the original full recovery remains available.
- With partial Memory, **Repair Memory gaps** checks up to 20 valid committed chapter files, requests an AI extraction for each unreviewed chapter, and adds ONLY names of new characters/locations *that literally occur in the committed prose*. Current state, canon, timelines, relationships, revisions, events, abilities and inventory remain unchanged.
- Backfilled location names carry `Unverified` status; backfilled character notes explicitly say attributes were not verified. The application saves one new memory document after a reversible checkpoint, not one file per chapter. Source text is never changed.
- Historical summaries later than the chapter being scanned are withheld. Long chapter samples still have **partial** coverage (opening/middle/end), so this is not a guarantee of exhaustiveness.

## Data compatibility

No new migrations or new files in AppData. The optional `BookkeepingStatus` field lives in each existing StoryChapterMemory JSON object; old memory files deserialize as `Unknown`. All real Story data remains under LocalAppData and is never packaged or pushed.

## Validation needed before release

1. `PUSH.bat` preflight: WPF Release compile with warnings-as-errors on Windows.
2. GitHub Release workflow: Quick + Stress, publish and update feed consistency.
3. Manual Story exercises: commit with Auto-bookkeeping on/off; reopen story; inspect coverage; repair partially populated 2-chapter story; cancel in flight; verify no change to canonical state/chapters; write a chapter after repair.
4. Verify UI on narrow monitors and during provider 503/timeout. Do not equate static checks with a Windows runtime result.

## Limitations

Repair cannot safely infer older/current relationship changes, positions, injuries, promises, or retroactive facts from a sampled historical chapter. No auto rewriting of existing canon. Future work can stage **human-reviewable evidence-backed deltas**, but should not mutate existing facts without explicit author approval.
