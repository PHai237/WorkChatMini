# WorkChat Mini v2.30.0-preview16 — Long Response Reader & Search Cancellation

**Source baseline:** complete v2.30.0-preview15 ZIP. **Last Windows-validated baseline:** v2.30.0-preview14.1 (Quick and Stress 22 PASS / 0 FAIL / 0 WARN, 2026-10-10). Preview15 has not yet been Windows validated, nor has preview16.

## Changes

1. **Bounded single-message rendering.** The previous 180-message visual row cap did not constrain the length of a single bubble. For responses longer than 6,000 characters, new `StoryLongMessageWindow` and `StoryPanel.LongMessageReader` display a bounded excerpt with Previous/Next controls, a 240-character overlap and a progress counter. Only the displayed prose changes; no persisted message is truncated.
2. **Full-text Copy even when the UI shows an excerpt.** The bubble Copy action selects the full saved message (or full stream-so-far). It never copies only the current WPF excerpt. Full text remains available for AI continuity/retrieval and is unchanged in `conversation.json`.
3. **Bounded live streaming preview.** Incremental provider output is accumulated as complete text in a UI-thread buffer. The WPF TextBlock displays at most the latest 6,000 characters plus a short notice rather than repeatedly concatenating an ever-growing TextBlock string. At successful completion the full provider response is saved, and on Stop the full accumulated partial response is saved, not the truncated tail. Cancelled responses retain the existing `[Stopped]` marker.
4. **Cancellable background Chat search.** Editing a search or switching Story now cancels the old full-history scan as well as invalidating its result ID, reducing wasted CPU during long sessions. Cancellation is normal, not an error in the UI. Search remains local, read-only and accent-insensitive.
5. **Regression tests.** The existing Story long-session self-test now tests a 71,000-character message for complete forward/backward coverage without expanding the WPF excerpt budget, bounded preview from a 72,000-character stream, Copy retaining all characters and cancellation of a superseded search.

## Compatibility

- Same Story Memory v6, conversation schema, backup/checkpoint and branch formats. No database migrations, new NuGet packages or AI calls.
- Browser services, Discord call safety, notification/adblock policy and existing Gemini/OpenAI provider code are unchanged.
- Message length is not capped in storage or AI context; only native WPF display is paged. Long replies need manual Previous/Next navigation within the bubble.
- Search uses a snapshot of message strings as before; token cancellation only saves worker effort.

## Acceptance limits

The authoring environment does **not** have the .NET Windows/WPF SDK. Preview16 has had static source/package checks but **no Windows compile, Quick, Stress or visual acceptance**. Run `AUTO-TEST.bat` at the next planned integration checkpoint, then manually inspect long messages, Copy, Stop in the middle of a long response, navigating long chats, Ctrl+F during Story switches and 100%/125%/150% DPI. In particular, the 180-message row cap and 6,000-character per-bubble cap complement one another but do not guarantee uniform GPU/CPU usage.
