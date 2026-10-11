# WorkChat Mini v2.30.0-preview17 — Interrupted AI Replies & Long-Message Search

**Based on:** complete preview16.1 source. No rollback, new AI call, package dependency, Memory schema change, or chapter/checkpoint format change.

## Changes in source

1. **Safe partial replies after Stop or a provider failure.** Earlier logic preserved the entire streamed response on explicit Stop, but a network/provider exception after receiving text could leave that partial response out of conversation history. Both paths now use the same save routine, using the complete accumulation rather than the bounded WPF tail. Incomplete text is labeled `INCOMPLETE`, with a visible terminal marker. A failed disk save is reported as a save failure rather than claiming persistence.
2. **Incomplete replies do not become canon or creative approvals.** An optional `IsInterruptedResponse` message flag is serialized only when true. Interruption clears proposal metadata and prevents editing/accepting/rejecting suggestions from an incomplete assistant message, and excludes such messages from future writer/model context. Narrative prompts paired with interrupted drafts remain excluded under draft isolation. Successfully persisted assistant replies are not appended again if a later bookkeeping operation fails.
3. **Find the actual text in very long messages.** In full-history Chat search, selecting a hit now opens the ~6,000-character segment containing it, not always the first segment of a 70,000-character reply. The search offset is mapped back to the original Vietnamese text when accent folding removed combining marks; Next/Previous still work and Copy always returns the entire message.
4. **Regression assertions.** Added tests for partial reply durability flags after JSON snapshot/reload, provider interruption vs Stop, no duplicate stop markers, exclusion from creative/canon contexts, preservation of committed chapters, and matches near the tail of very long accented messages.
5. **Build regression safeguards retained.** `Point` references in Story WPF code remain explicitly `System.Windows.Point` from preview16.1; old source-check guards, WebView2/Discord/media/notification policies and provider networking remain unchanged.

## Compatibility & safety

- Existing `conversation.json` files without `IsInterruptedResponse` deserialize with the default `false`. Old data is not migrated or modified merely by reading it. New incomplete text is history only, not a chapter or proposal.
- An incomplete reply is not silently retried or sent to Gemini again. A user can issue a new request separately.
- The saved interrupted text includes a short terminal marker. Copy includes that marker because it represents the recorded status, not deleted content. The raw streamed prefix is preserved unchanged.
- Backup and checkpoint snapshots carry this optional flag with conversation data; Story Memory remains v6.

## Verification and limits

- This package was created in an environment without `dotnet`, the Windows WPF build targets or PowerShell; **Release BUILD, Quick, Stress and manual DPI/streaming acceptance are NOT validated**.
- Last source with confirmed Windows Quick + Stress (22/0/0 each) is **preview14.1**. Preview16 had CS0104 `Point` compile errors, addressed statically in preview16.1 but no Windows BUILD result supplied for it.
- Test preview17 in a fresh extracted folder: run `BUILD.bat`, then `AUTO-TEST.bat`. Test a disposable Story by stopping a long Gemini reply, simulating a provider/network error mid-stream, reopening the story, confirming `INCOMPLETE` and Copy full text, and searching for an accented phrase near the end of a long reply. Never delete `%LOCALAPPDATA%\WorkChatMini` to fix a build failure.
