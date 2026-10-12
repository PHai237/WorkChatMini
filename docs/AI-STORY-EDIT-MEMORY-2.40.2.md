# AI Story 2.40.2 — author editing and chapter memory

## What changed

- **Edit & Regenerate**: edit the most recent creator turn, provided no chapter/proposal/creative direction from its AI answer has been committed. The old AI reply remains visible as an archived earlier version and is excluded from future AI context. Each edit creates a local checkpoint **before** changing the prompt. This is not editing arbitrary historical turns; older chapters should be revised with branches/checkpoint restores.
- **Save reply as Chapter…**: when a long AI reply was accidentally written in Chat rather than Continue Story, a two-click author action can promote only the latest eligible full reply into the chapter store. If Auto-bookkeeping is ON, it attempts character/lore/location/event extraction via a utility model. Only explicitly approved content becomes canon. AI extraction failures may still permit a chapter-only commit: check Memory and Logs.
- **Discuss / Scene Compass / Continue Story chips** remain clickable during an AI request so you can inspect context or select the next action; Run/Send, composer input, and memory mutation remain disabled until the current request completes.
- Correctly treats previously superseded AI replies as history, not AI generation context, and still allows Retry AI when an edited prompt's new response fails.

## Where your story lives

- `%LOCALAPPDATA%\WorkChatMini\AI\Projects\<story-id>\` contains project.json, conversation.json, memory and chapter files. No remote synchronization is implied.
- Story Memory is currently scoped to the active story. It automatically participates in context retrieval when an eligible chapter is committed and bookkeeping succeeds; a Chat discussion alone does not add confirmed memory.
- Open Story Memory > Overview to check Auto-bookkeeping ON. In Settings, choose Review before commit if you want manual approval for Continue Story, or Auto-commit narrative if you want generated narrative saved automatically after success.
- Story Memory > storage health offers **Create verified Story backup (ZIP)**. Backups are written in `%LOCALAPPDATA%\WorkChatMini\AI\Backups\` and can be imported as a separate story.

## Safety and known limits

- Editing a committed chapter, an applied character/lore proposal, or a saved creative direction is deliberately blocked. This avoids quietly rewriting established canon. Branch/rollback is the correct workflow.
- AI may extract *zero* characters even if a chapter was saved. Review the Memory counts and `[STORY-MEMORY]` log entries. No automatic claim is made that a chapter has been semantically indexed just because it exists in conversation.
- The app does not train a model, use an embedding/vector database, or automatically share memory between different stories. Its existing story-scoped retriever combines structured Memory and chapter search.
- Windows WPF compilation and Quick/Stress must pass in GitHub Actions; source-only lint on Linux is not a compiled release.
