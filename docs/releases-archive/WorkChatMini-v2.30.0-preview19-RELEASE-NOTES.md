# WorkChat Mini v2.30.0-preview19 — Story Director + Narrative Quality 2.0

Base: **v2.30.0-preview18**. Existing in-place updater, browser protections,
Story Memory v6, branches, backup, long-chat paging and incomplete-stream safeguards are unchanged.

## Story Director (opt-in, per Story)

- Story Settings → Story Director → **Review** (default **Off** for all existing Stories).
- Selecting **Continue Story** and pressing Send first makes a *single* utility-model
  call to propose three plausible next-chapter directions A/B/C. Nothing is written
  to conversation, chapter files, Creative Ideas or canon during this preview.
- Choose A/B/C, edit the text, then **Write with this direction** to invoke the
  existing Continue Story pipeline. **Write freely** bypasses the proposal,
  **Discard** leaves the current composer instruction intact.
- Preview is held in temporary UI state, not written to disk. Changing Story
  discards it. A changed composer direction must be previewed again before approval.
- If the preview API errors, **Write freely** remains available. The Story
  remains unchanged by the failed preview.
- The approved direction is sent only with this generation and stored in the
  assistant message's SourceInstruction for reproducibility. It is labeled as
  editorial direction, **not a chapter event or confirmed canon**. Unchosen
  plans are not sent to the writer. Existing explicit Chat stays discussion-only.
- No extra API calls when the per-Story setting is Off. Review costs one
  additional utility-model request before the normal Continue Story pipeline.

## Narrative Quality 2.0 / steering

- Story Settings → Narrative Focus: Balanced, Character depth, Grounded plot,
  Slower pacing. Optional 480-character avoid/prefer note, saved per Story.
- These guide only Continue Story; they are not canon and do not retcon memory.
- Private preparation now emphasizes believable causes, character choices,
  aftermath, existing arcs and optional quiet chapters instead of arbitrary
  new factions/powers/mysteries and mandatory cliffhangers.
- Continue Story Draft temperature adjusted from 0.75 to 0.65; Editor from
  0.75 to 0.35. Draft Scene keeps 0.75. These are prompt/stage controls,
  **not a guaranteed improvement in prose quality**.
- Maintains the existing continuity checker and review-before-commit.
- Self-test assertions extended under the existing Story memory case for
  defaults, persistence, opt-in boundaries, prompt limits and research routing.

## Verification / limitations

- Static audit, source structure, XAML names/event checks and ZIP integrity run
  in the packaging environment. **Windows WPF compile and 22 Quick/Stress tests
  have NOT been run for preview19** (nor preview18).
- Existing tested Windows baseline remains **preview14.1**.
- Story Director quality requires actual Gemini/other provider playtesting;
  network responses, UI appearance and narrative quality cannot be proven by
  static checks. Do not replace your stable install until Windows validation.
