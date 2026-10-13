# AI Story 2.43.0 — Chapter source audit (read-only)

## Intent and scope

The existing memory schema has ChapterNumber, Source and Evidence fields. Source and Evidence can be provider-generated. Merely having `Confirmed` in a Memory entry does NOT establish that the underlying committed chapter supports it. This release adds an **on-demand read-only audit** that checks the link to the committed manuscript, without silently rewriting or making canon decisions.

- Navigate to **Story Memory → Overview → Audit sources**. The audit is shown in the existing inspector pane; select Overview again to return.
- Examines up to 120 items across confirmed events, causal links, narrative promises, timeline and confirmed canon. Proposed canon/events are excluded rather than promoted by the audit.
- References must point to a chapter in the unique, safe committed chapter ledger. Never scan orphan files or a path from model output. Reads at most 20 chapter files and respects the existing 2 MiB/chapter limit.
- Displays an **exact excerpt** result only when an evidence string is literally present in the chapter body (case-insensitive). A match shows only that the text occurs, NOT that the claim, interpretation or causal relation is accurate.
- A non-matching paraphrase is marked **Quote not found** (not contradicted). A claim with a chapter source but no quotation is **Source only**. Ambiguous references or unreadable chapters are shown as missing/unavailable.
- This report is volatile UI text; it is not stored as canon, is never fed to Gemini as confirmed data, has no API cost and changes no manuscript/memory JSON.
- For newly written chapters, the Bookkeeper asks the model to put an exact short source excerpt in event/promise/causal-link evidence fields. Historical evidence fields remain unchanged.

## Regression and limitations

`StoryEvidenceAuditSelfTest.Verify()` is wired through `StoryReadinessSelfTest.Verify()`. It checks evidence literal matching, false-positive paraphrases, missing chapters, proposed exclusion, chapter-number extraction, memory non-mutation and bounded scan counts. Self-test is written but is NOT executed until the real Windows Quick/Stress suite is run.

A textual match does not resolve contradictions, track semantic entailment, or verify conclusions. There is deliberately no automatic accept/reject/delete button. Cross-chapter events may need several citations; this audit uses the explicit source/introduced chapter only. This iteration does not implement automated recovery of historical event timelines.
