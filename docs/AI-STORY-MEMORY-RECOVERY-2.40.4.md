# WorkChat Mini 2.40.4 — Structured Story Memory recovery

## Problem
A story can contain committed manuscript chapters while Character, Location,
Events and other structured Memory entries remain empty (Auto-bookkeeping was
disabled, a utility call failed, or the model returned invalid JSON). The
chapter texts remain safe and searchable but structured recall is limited.

## New experience
In **AI Story → Memory → Overview**, if there are **1–20 committed chapters**
and *all* structured memory lists are empty, **Recover missing Memory** appears.
The first click explains the operation; the second confirms AI API usage.

The action processes only chapter files listed in committed Memory, in number
order. A background provider stage extracts facts from each chapter, using
previously recovered state and **no future chapter summaries**. Output is
normalized, checked by deterministic continuity rules, and merged into a
**detached in-memory copy**. If any provider response is invalid, a chapter
file is unsafe/missing, cancellation is requested, or a conflict occurs,
**nothing new is saved**. If all chapters succeed, a checkpoint captures the
pre-recovery state, then one atomic structured-memory save is performed.

There is no new chapter, change to existing chapter prose, retcon, automatic
import, deletion, or upload of an entire backup. Provider requests necessarily
send selected chapter excerpts to the configured AI provider; do not run on
sensitive material that cannot be sent to that provider.

## Deliberate limitations
- If any canon/characters/relationship/events/lore/etc. are already stored,
  recovery is unavailable to avoid overwriting user edits or applying earlier
  chapter states over later canon. A separate reviewed merge is required for
  partially populated stories in a future change.
- Recovery requires an API key, will consume usage/quota, and may take time for
  many chapters. Stop cancels work without saving incomplete recovery results.
- The 30,000-character input cap now samples **opening, midpoint and ending**
  of long chapters, with explicit omitted-passage markers. Missing sections
  are not analyzed; this does not guarantee complete extraction or perfectly
  accurate facts. Review Story Memory after completion.
- Corrupt, duplicate, missing, oversized, unsafe-path or linked chapter files
  are not processed. Use Storage Health/backup tools rather than overriding
  these guards.
- All personal Story files remain under `%LOCALAPPDATA%\WorkChatMini`; this
  source upgrade does not move, reset, or upload them.

## Offline regression checks
`StoryMemoryRecoverySelfTest` runs through `StoryReadinessSelfTest` in the
existing Story Quick/Stress acceptance chain. It checks eligibility, duplicate
chapter entries, unsafe paths, existing data protection, and context cutoff at
chapter N. `StoryBookkeepingExcerptSelfTest` verifies the 3-window excerpt
budget and missing-passage markers.

## Acceptance on Windows
1. Run `PUSH.bat` with the GitHub Windows Compile Preflight gate. Do **not**
   promote to `main` or release if the WPF compile fails.
2. Run release Windows Quick/Stress and ensure `v2.40.4` plus feed are ready.
3. In a **throwaway test story** with one committed chapter and empty structured
   Memory, choose Recover, confirm twice and observe Character/Location/etc.
4. Verify the text and count of chapter `.md` files are unchanged. Cancel a
   recovery during AI response and confirm no partial structured-Memory save.
5. Verify a story with existing canon has no recovery button and remains intact.
6. Only then update the installed app through More → Updates. Never delete
   existing user Story data or API credentials.
