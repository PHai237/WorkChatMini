# AI Story 2.40.3: UI + Memory quality checklist

## Scope

This patch is deliberately limited to visual usability, source-of-truth safeguards and clear Memory extraction status. It does **not** fine-tune Gemini, rebuild historical chapter memories, migrate Story files, alter API keys or overwrite existing project data.

## Manual Windows checks (required before release)

1. Open Story at narrow widths (e.g., 780px, 1024px) with sidebar expanded and collapsed. Open/close Settings, Characters and Scene Compass while idle and while AI is generating. No control should become unclickable or extend outside the window.
2. Start an empty Story. All seven welcome cards must be reachable by vertical scrolling at short window heights. Select Build Character: More tools must expand and show the selected mode.
3. At ordinary width, check the four primary chips, click More tools twice, and confirm the selected mode highlight and `Send/Run` label follow the selected action.
4. Send a short chat message, edit the last prompt and regenerate. Old AI answer remains visible in history but must be labeled superseded, and a new generation must not refer to it as confirmed fact.
5. With Review before commit enabled, generate a chapter, keep it as draft and confirm Characters/Canon don't change; then commit and check that Memory and Chapters counters update.
6. Simulate Bookkeeper provider failure (e.g., temporarily invalid utility model) after writing a chapter. Confirm the chapter remains available, the UI warns structured extraction is incomplete, and there are no invented characters/canon records.
7. Open a long chapter (>30,000 chars); confirm its full content remains available in manuscript/copy. Bookkeeping should log `partialCoverage=true`. Verify that only facts from available excerpts are eligible for automatic extraction.
8. Run `BUILD.bat --nopause` and `AUTO-TEST.bat --nopause` in Windows GitHub Actions, then verify Release assets/feed before updating the installed app.

## Known limitations / future work

- The 30,000-character bookkeeping excerpt deliberately omits a middle passage; full-chapter chunked extraction with provenance and conflict-safe deduplication is future work.
- A stored chapter whose structured extraction previously failed is **not** reconstructed by this patch. Avoid re-committing a chapter to repair memory because that duplicates manuscript state.
- Story UI remains WPF and native; validate DPI scaling on both monitors. Static XAML parsing cannot establish rendering correctness.
- Bookkeeping is separate from manuscript persistence and may fail due to model/network issues. A chapter being saved is not proof that character/location extraction succeeded.
