# AI Story 2.42.0 — Receipt semantics and long-story repair

This changes only source code. It does not train a model or modify any personal Story stored under `%LOCALAPPDATA%`.

## Why this change

2.41.0's name-only repair mistakenly wrote a chapter receipt of `Reviewed` after inspecting only character and location *names*. That suppressed coverage caveats, making a writer treat events/relationships absent from Memory as absent from the story. 2.42.0 uses a distinct `NamesOnly` receipt and keeps that caveat visible.

## Receipt semantics

| Receipt | Meaning | Eligible for name-only scan? |
|---|---|---|
| `Reviewed` | Non-excerpted prose was submitted to the normal/full extraction; output processed. Not a guarantee of accuracy. | No |
| `NamesOnly` | Names-only scan completed; events, relations, timeline, and other structured facts still unverified | No (avoids duplicate API costs) |
| `Incomplete` | Extraction failed, content was omitted, or unsafe structured deltas were removed | Yes, if source set is valid |
| `Skipped` | Auto-bookkeeping was off or intentionally bypassed | Yes |
| `Unknown` | Legacy or invalid receipt; no reliable extraction status | Yes |

A story with 21–500 committed chapters now supports name-only repair even when structured Memory starts empty. The app processes at most 20 chapters per invocation; it never silently initiates an unbounded job. Up to 20 chapters with completely empty structured Memory still use the separate, full chronological recovery workflow. The 500-chapter safeguard is an *operation cap*, not a limit on writing or reading the story.

## Integrity safeguards

- Repair uses committed chapter files only, validates filenames and the chapter set, and never modifies manuscript text.
- A complete copy of Memory is staged. A checkpoint is written before the final Memory file replacement.
- The original chapter text is re-read and SHA-256 compared before commit, guarding against mid-extraction changes (in addition to the existing active-project guard).
- `NamesOnly` additions are confined to independently present character and location **names**; it never writes current location, relationship, inventory, abilities, secrets, timeline or events.
- Long chapters are still sampled for Gemini bookkeeping. The receipt is `Incomplete`, since omitted portions cannot be treated as evidence.
- Provider calls may incur quota. Cancellation or failure leaves the prior Memory document unchanged.

## Windows test checklist (only after preflight passes)

1. Load a duplicate/test Story with already committed chapters and some character memory; check **Memory → Overview** coverage counts.
2. Run Repair names; inspect that **NamesOnly** counts increase and that no existing relationships/inventory or events are rewritten.
3. Reopen the same Story; confirm name-only chapters do not re-enter the pending batch, while AI writing context still warns about incomplete full coverage.
4. Test 21+ and 45+ chapter test data (without using personal Story originals) and verify no single operation processes more than 20 chapters.
5. Test a chapter longer than 30,000 characters: valid extraction still marks it `Incomplete`, never fully reviewed.
6. Disable network or cancel a repair midway; confirm the preexisting Memory and chapter prose remain intact.
7. Run Windows Release compile and Quick/Stress self-tests, then publish a versioned release only if all pass.

## Limitations

This does **not** reconstruct complete events for a Story with partially populated Memory. Name-only repair is a safe stopgap. Recovering full historical state into a non-empty canon will require evidence-backed suggestions and author review in a later version; auto-merging old events into current state remains unsafe.
