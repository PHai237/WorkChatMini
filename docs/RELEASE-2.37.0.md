# WorkChat Mini 2.37.0 — Story Integrity & Literary Repair

## Fix: orphan chapters in fallback retrieval

The indexed retrieval path already respected the committed Story Memory ledger, but its legacy fallback (used after a failed index query) searched every Markdown file in `chapters` without filtering. That could expose orphaned, uncommitted manuscript text as AI reference material after an index error.

`StoryMemoryStore.SearchChapterTextAsync` now reloads the authoritative committed ledger before invoking the fallback. The fallback takes an explicit allowlist of committed filenames, does not read stray files and returns empty results if the list is empty or the ledger cannot be safely read. Only this disposable retrieval path changes; user story files are never removed or rewritten.

Existing `StoryLongRetrievalSelfTest.VerifyAsync` now exercises the fallback directly with a real orphan fixture, a committed chapter and an empty allowlist.

## Literary continuity repair

The existing continuity repair prompt now asks Gemini to preserve the original prose language, POV, subtext, dialogue cadence and all unrelated paragraphs while making only grounded fixes for the flagged issues. Avoids using a continuity correction as permission for a scene-wide rewrite. An assertion in `StoryLiteraryCraftSelfTest` guards the instruction.

## Compatibility

- No change to persisted data schema, Story Memory canonical rules, backup/restore, project data paths or binary updater ZIP structure.
- No new AI model request, API key, window or setting.
- GitHub Actions builds Windows after `PUSH.bat`; no local BUILD required.
- Local checks cover code/XML/YAML/ZIP structure only. **Windows compilation, Quick/Stress and literary improvement have not been validated yet.**
