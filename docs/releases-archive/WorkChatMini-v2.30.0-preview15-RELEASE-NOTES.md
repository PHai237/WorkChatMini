# WorkChat Mini v2.30.0-preview15 — Bounded Long-Story Chat & Search Reliability

**Full-source baseline:** v2.30.0-preview14.1 (user's 2026-10-10 Windows Quick and Stress self-tests both **22 PASS / 0 FAIL / 0 WARN**). The preview14 interrupted-commit rescue hotfix and all prior Story, branch, checkpoint, backup, browser, and AI capabilities are retained.

## Implemented in preview15

1. **Bounded Story history reader:** The old `Earlier messages` control grew the native WPF bubble tree by 60 each click, potentially displaying all 1,000+ saved messages. The reader now maintains a bounded 60→120→180-message window, then slides by 60 as the user continues backward. `Later messages` navigates forward over the complete saved history. The full conversation on disk is untouched; this is visual windowing, not storage truncation or virtual-page deletion.
2. **Reader position and safety:** The paging UI anchors the first/last overlapping message when sliding backward/forward. Delayed scroll callbacks are scoped to the same Story and render revision so a rapid Story switch cannot apply stale navigation to the newly selected Story. `Latest messages` resets to the 60-message tail, and a new user turn does so before creating the streaming bubble. The UI does not rebuild while Gemini is streaming.
3. **Search race protection:** If a user edits a search while a background search is running, the old search no longer clears the busy state/results of a new search. Old failures also cannot overwrite the current Story's search status.
4. **Vietnamese accent-insensitive local search:** Searching `chot huong`, `ca phe`, or `dang tim` can find corresponding Vietnamese messages containing `chốt hướng`, `cà phê`, or `đang tìm`. The fallback runs off the UI thread and is read-only. It never edits Message/Memory/Canon or adds an AI request.
5. **Regression assertions:** An existing Story long-session self-test now walks a simulated 2,000-message chat backward and forward, ensures every message is reachable, verifies the 180-bubble cap, checks new messages do not displace an older reader page, and tests accent-insensitive retrieval.

## Compatibility and acceptance limits

- Keeps Story Memory schema v6, conversation/checkpoint/backup formats and existing files. No migration, new packages, new provider requests, browser changes, notification policy changes, or Discord call guard changes.
- The **180-message limit is a window of visual message rows**, not a limit on the number of messages saved. Nested prose can still be long. Searching still reads the in-memory full history.
- Page-position compensation is best-effort and needs visual testing with variable-height messages. WPF generated-XAML handlers, compiler correctness, actual smoothness, and file recovery require a Windows build/runtime test; static checks cannot prove them.
- On Windows run `AUTO-TEST.bat` and confirm Quick and Stress both 22/0/0, then manually try a disposable Story with >500 messages, `Earlier`/`Later`/`Latest`, Ctrl+F with accented/unaccented Vietnamese, typing while search runs, sending from historical view, and rapidly switching Stories. Avoid using an unvalidated preview with irreplaceable data.
- Monitor stress median tab switch (46 ms on preview14.1 vs 9 ms Quick): one run alone does not establish regression, so compare repeated tests under similar machine load.
