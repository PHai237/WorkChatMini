## 2.32.0 — Literary Craft 2.0

- Add optional, bounded scene craft intent to existing Continue Story preparation/draft/editor (no extra AI call or canon changes).
- Introduce genre-sensitive prose guidance focused on POV, subtext, emotional cause and earned chapter endings.
- Add committed-only cross-chapter opening/ending echo watch and two feedback choices: Stiff dialogue, Over-explaining.
- Keep installer/updater feed, GitHub Actions and stable download filenames unchanged.
- Windows BUILD and Quick/Stress must pass on GitHub before release.

## v2.30.0-preview20 — GitHub Windows release delivery (Windows validation pending)

- GitHub Actions builds and tests Windows release, packages both first-install and update ZIPs, then publishes Release and stable GitHub Pages update feed.
- Release EXE embeds the publisher feed address; Updates checks online when opened, verifies then explicitly installs on restart.
- First-time users install without BUILD/.NET SDK. Local data never belongs in the install ZIP. See `docs/UPDATE-FEED.md`.
- Publishing needs one-time GitHub repository + Pages configuration; this source zip is not a compiled EXE.

## v2.30.0-preview19 — Story Director / Narrative Quality 2.0 (Windows validation pending)

- Optional per-Story Review gate for Continue Story: three next-chapter options, editable approval and Write freely bypass; no preview canon/commit.
- Per-Story Narrative Focus and 480-character Creator Steering; no extra API requests when Story Director is Off.
- Cause/choice/consequence, aftermath and pacing guidelines, toned-down Continue Story draft/editor temperature; preserves manuscript modes, Memory v6 and older stories.

## v2.30.0-preview17 (2026-10-10)

- Interrupted response durability for Stop and provider errors, opt-in incomplete conversation status, future model context isolation.
- Search-hit-aware long-message reader with Vietnamese diacritic-aware original offsets.
- Regression assertions and release notes. Static checks only; Windows validation pending.

## v2.30.0-preview16 — bounded single-response reader and cancellable Chat search (Windows validation pending)

- Adds native WPF chunk navigation for single messages over 6,000 characters, while keeping full persisted text and full Copy.
- Bounds streaming UI preview without truncating Stop/saved text, cancels superseded local searches and expands long-session selftest assertions.
- Built on preview15; last Windows-validated source baseline remains preview14.1. No Windows build or runtime tests yet.

## 2.30.0-preview15 — bounded Story history & Vietnamese Chat search (Windows validation pending)
- Keeps visual Story reader at ≤180 message bubbles while permitting full-history navigation.
- Guards delayed WPF scroll and stale background search results across Story/query changes.
- Adds read-only accent-insensitive local search and 2,000-message paging regression assertions.

## 2.30.0-preview14.1 — interrupted commit recovery hotfix (not Windows validated)
- Reuse the commit transaction ID for its abort/rescue journal.
- Treat overlapping commit and restore journals as one recovery operation, including legacy journal IDs.
- Add repeated recovery and legacy-paired-journal regression fixtures.

## v2.30.0-preview14 — Conversation write order / recovery barriers (2026-10-10)

- Shared per-Story conversation write queue preserves newest successful autosave across independent stores.
- Pending manuscript rescues now block new commit/branch creation until recovery finishes.
- ZIP backup acquires the Story mutation gate for its entire snapshot/checksum workflow.
- Additional deterministic regression fixtures, no new top-level test case; Windows compile/test pending.

- **v2.30.0-preview13:** per-Story backend mutation serialization, resumable one-time aborted-commit cleanup, verified completed-journal recovery, concurrent-branch regression tests; Windows validation pending.
- **v2.30.0-preview12:** strict chapter/checkpoint mapping, commit conversation proof, branch-head import validation, safe backup enumeration; Windows validation pending.
## v2.30.0-preview10 — Safe checkpoint recovery and verified backup import (2026-10-10)

- Import a v9+ verified portable Story ZIP as a **separate new Story**; original Story, branch heads and ZIP are never overwritten. Read/check hashes before extracting; stage files outside Projects and atomically publish a uniquely identified copy after validation.
- Reject corrupt ZIPs, unsafe Windows archive paths, incomplete metadata, pending journal files, and unfinished branch switches. Use bounded streaming extraction, not unbounded RAM buffers.
- Preserve orphan, unknown and modified chapter files during commit rollback/branch restore into `branches/recovered/<transaction-id>`, outside active manuscript and memory. Do not follow directory junctions, do not silently delete unprotected prose, and retain the old archive if rescue inspection fails.
- Show recovery holdings in Storage Health, enhance existing Story backup/commit regression self-tests (still 22 top-level test cases).
- No changes to Gemini/OpenAI provider, WebView2, Discord guard, memory schema v6 or checkpoint format. Source audit only; Windows BUILD/Quick/Stress deferred to user-requested integration milestone.

## v2.30.0-preview9 — Portable Backup & Live Manuscript Integrity (2026-10-10)

- Verified portable ZIP backup of each Story (project, conversations, chapters, memory, branches and checkpoints), without browser data/API keys. Manifest includes SHA-256; verify archive and recheck source before publishing. No automatic import or cleanup.
- Storage Health read-only live manuscript diagnostics: missing/empty/unlisted files, duplicate/invalid chapter references.
- Fail-closed chapter file creation with CreateNew to prevent overwriting old or orphan prose; cleanup of temporary branch/checkpoint journal files on error.
- Added disposable ZIP integrity/tamper and live consistency regression assertions inside the existing 22-case self-test suite. No Windows BUILD/Quick/Stress yet; Windows-tested baseline preview6.

## v2.30.0-preview8 — Conversation Search & Memory Safety (2026-10-10)

- Full-history local search with 60-bubble bounded navigation; Ctrl+F and result prev/next; cancel stale searches on Story switches.
- Streaming generation isolation; fail-closed corrupt Memory loads; staged proposals with durable applied receipts.
- Source/static check only; Windows BUILD + Quick/Stress deferred by user request. preview6 latest Windows-validated baseline.

---

# WorkChat Mini — source release history

- **v2.30.0-preview11**: isolated bookkeeping/commit Memory, durable post-restore rescue journal, chapter finalization checks, checkpoint/backup preflight validation. Static only; Windows testing pending.

## v2.30.0-preview7 — Creative Ideas Organizer & Conversation Integrity (2026-10-10)

- Based on Windows-validated preview6 (Quick/Stress 22/0/0).
- Creator-selected categories, text/status/category filters in native Story Inspector; optional branch-local JSON field.
- Stable conversation value snapshots before background writes; lock prompt initiation before first async save; restore unsaved input on initial save failure.
- Extended Story foundation regression assertions; static-only preview7 pending Windows BUILD/Quick/Stress and manual UI checks.

---

## v2.30.0-preview6 — Story Long-session Reliability (2026-10-10)

- Based on Windows-validated preview5 (Quick/Stress 22/0/0).
- Queue rapid Story switches with latest selection winning, block edits during loading, fail closed on unreadable Story.
- Latest messages button and scroll-follow improvements; 1.000-message paging and switch-queue regression tests.
- Preview6 remains source-only until Windows BUILD/Quick/Stress.

---

# WorkChat Mini — source release history

# WorkChat Mini v2.30.0-preview4 — Co-Creation 3.0 / Creative Decisions

**Base:** Full source `v2.30.0-preview3`.
**Windows baseline:** `v2.30.0-preview3`, **Quick 22 PASS / 0 FAIL / 0 WARN; Stress 22 PASS / 0 FAIL / 0 WARN** (user's logs 2026-10-10 08:14 +07:00). Quick startup 1129ms / median switch 11ms; Stress startup 938ms / median switch 12ms; neither reported leftover WebView processes.
**preview4 validation:** static audit only. No .NET SDK/Windows WPF environment is available here: Windows BUILD/Quick/Stress must be rerun.

## Included changes

1. Explicit **Pass / Undo pass** per Gemini Chat option. This is creator intent only; unchosen alternatives, quotes, code blocks and narrative drafts are never automatically declined. Unaccepted ideas remain uncommitted.
2. Model context carries a bounded section of recently declined suggestions to discourage repeated pitches. It is clearly marked as non-canon and never treated as chapter events. The context remains capped at 2300 characters; no additional Gemini calls.
3. **Replace existing direction** selector during Keep idea. The author chooses an existing accepted idea; the old direction is archived in the SAME atomic conversation save. No naive semantic-conflict heuristic attempts to infer what to replace.
4. **Revision history and restore** for creative directions, with previous wording/time retained when changed. The prior wording is excluded from active writer context, but remains recoverable. Branch checkpoints snapshot the history naturally.
5. **Safer UI persistence**: one write-in-progress gate, multi-message state rollback on disk save failure, and a stable conversation array for save even if navigation changes. Send / branch / checkpoint operations are gated during this small window.
6. Extended **Co-Creation self-test assertions** inside the existing Story foundation test for explicit rejection, undo, invalid rejection, approval override, replace/archive, edit/restore, JSON persistence and long-session context budget.
7. No changes to Shell/WebView, Discord call/media guard, adblock, notification policy, provider network, Story Memory v6, checkpoint storage format or chapter canon.

## Stored data compatibility

`StoryMessage` gains optional JSON properties `CreativeDirectionRevisions` and `DeclinedCreativeSuggestions` (empty on legacy conversations). They are serialized inside `conversation.json` so existing branch/checkpoint files can carry them without migration. Previously-saved stories continue to load. Do not edit or delete personal story files to upgrade.

## On Windows

1. Extract full ZIP to a clean directory, preserving `%LOCALAPPDATA%\WorkChatMini`.
2. Run **AUTO-TEST.bat** once; expected both Quick and Stress = 22/0/0. If compile fails, send the first compiler error and surrounding lines. Static checks are not a compiler.
3. Smoke-test with a disposable Story: Gemini returns three ideas; **Pass** one, verify it appears in Creative Ideas and persists after restart; **Undo pass**; **Keep idea** for another; then choose **Replace** an existing direction; edit, view Revision history, restore. Switch to a separate branch and confirm its prior decisions are isolated.
4. If you use real Gemini prompts, check that declined ideas are not immediately re-suggested. Prompt guidance cannot absolutely guarantee the model never repeats them.

## Known limitations

- Automatic semantic conflict resolution between Vietnamese creative directions is intentionally **not** claimed: the creator explicitly chooses replacements.
- The negative guidance is bounded and consists of a few recent declines; it does not represent permanent cross-story blocking.
- The new WPF UI has not been visually inspected at Windows 100/125/150% DPI. Built-in technical self-tests are not a substitute for manual UI review.
- Source-only XML/static checks in the packaging environment do not confirm a successful C#/WPF compilation.


---

# WorkChat Mini — source release history

Previous release notes are retained here as an archive. They are documentation, not executable source.



---

## RELEASE-NOTES-v2.29.5-preview10.md

# WorkChat Mini — v2.29.5-preview10

**Based on:** v2.29.5-preview9. **Latest Windows-validated baseline:** v2.29.5-preview7.1 (22/0/0 Quick and Stress). This preview has NOT undergone Windows BUILD / Quick / Stress yet.

## What changed

1. **Conservative character alias resolution** (`StoryAliasResolver.cs`): resolve a whole-phrase, unambiguous nickname to the canonical character name before building the retrieval plan. Shared nicknames are not silently assigned. Canon/memory is untouched; this is a read-only derived query aid.
2. **Query-first term priority** (`StoryRetrievalPlanner.cs`, `StoryRetrievalPlan.cs`): preserve original query terms and resolved canonical names ahead of graph and ontology expansions in the 40-term chapter search budget.
3. **Corpus-aware ranking** (`StoryChapterIndexStore.cs`): compute document frequencies from the CURRENT branch's disposable chapter index and give distinctive direct-query terms more weight. No vector DB or new stored index format.
4. **Evidence-oriented chapter excerpts** (`StoryChapterExcerptSelector.cs`, `StoryMemoryStore.cs`): choose a bounded 1,800-character window with the most matching direct terms, then canonical entity terms, then weaker expansions. Do not blindly return the earliest matching sentence.
5. **Strict causal provenance bridge** (`StoryRetrievalPlanner.cs`): a matched causal link may boost the chapter of a referenced event only when its endpoint equals the event ID or full summary. Do not infer causation from chronology, name overlap or arc co-occurrence.
6. **Additional Story self-test fixtures** (`StoryLongRetrievalSelfTest.cs`): cover unique/shared aliases, partial-word collisions, late evidence-window choice, rarity ranking and explicit cross-chapter causal references. Still 22 top-level self-test cases.

## Intentionally unchanged

- Story Memory schema v6, saved chapter and branch/checkpoint formats, provider/API routing, UI, Browser and Discord Call Guard.
- No project data migration, no automatic delete or cleanup.
- No new dependencies or network calls; retrieval remains deterministic and local.

## Validation

Source-only release. ZIP/XML/XAML/batch/static checks are performed here. .NET SDK and Windows WPF runtime are unavailable in this environment, so neither compile nor Windows self-test is claimed. Per user preference, defer Windows BUILD and Quick/Stress to the next meaningful integration milestone.

## Known limitations

- Exact lexical aliases must already be present in saved Story Character memory; no model guesses new aliases.
- Sampling still caps each chapter index at 1,800 distinct terms; a very rare term absent from that sample may remain unreachable except via structured provenance or fallback.
- Character-count prompt budget is approximate, not tokenizer-exact.


---

## RELEASE-NOTES-v2.29.5-preview11.md

# WorkChat Mini — v2.29.5-preview11

**Based on:** v2.29.5-preview10.  **Last Windows-tested baseline:** v2.29.5-preview7.1 (Quick + Stress: 22 PASS / 0 FAIL / 0 WARN).  **Preview11 is source-only; no Windows BUILD, Quick or Stress has been run.**

## Changes

1. **Complete-chapter term filter / index schema v3:** A fixed 4 KiB Bloom-style signature is derived from ALL normalized chapter tokens, even when a clue is outside the bounded 1,800-term sample. The signature stores no prose and is *disposable derived index data*, not Memory/canon. Search still prioritizes direct query/entity terms, and the existing candidate cap limits full chapter reads.
2. **Candidate verification:** A probabilistic match alone does not count as evidence; the chapter text must match query terms unless structured event/chapter provenance explicitly boosts it or the user requested Continue Story with recency context. False positives are expected from the approximate signature and must never become proof of a story event.
3. **Memory context authority:** Current Confirmed/Proposed canon (excluding Retconned/Contradicted) and approved revision history are assembled before recent/historical chapters. Current canonical facts are ordered by last update when a short prompt has no lexical match. Retrieved chapter excerpts are explicitly labeled historical provenance and cannot override approved retcons.
4. **Bounded index construction:** The visible/sample term list is taken from three limited text windows instead of allocating a list of every distinct token in a long chapter. All terms are still fed into the constant-size signature. This reduces temporary allocations at index-build time, not measured end-to-end latency.
5. **Backwards-compatible rebuild:** Index schema v1/v2 is automatically re-derived as v3 on the next retrieval; `chapter-index.json` is disposable. Story Memory schema v6, branch IDs, checkpoints, saved chapter text, UI and provider are unchanged. Branch/restore invalidation remains intact.
6. **Additional Story regression assertions:** Rebuild an old index, find a unique clue hidden outside all sampled windows, check that a saturated filter cannot alone admit evidence, restore empty-prompt Continue Story recent-chapter fallback, preserve provenance exceptions, and ensure current canon is above historical chapter evidence. These are folded into the existing long-retrieval Story test, keeping 22 top-level tests.

## Constraints and honest caveats

- A Bloom signature can report false positives, but not false negatives for tokens processed by the same tokenizer. Actual text verification guards normal results.
- On first use after upgrading v1/v2 indexes, all existing chapter indexes must be rebuilt once, so the first retrieval may take longer; subsequent requests reuse index metadata unless chapter timestamps change. The index grows by about 5.5 KiB encoded per chapter, in addition to the existing sampled terms.
- The index is local and keyword-based; this is **not** semantic embedding retrieval. Vietnamese orthography variants and paraphrase-only retrieval remain limited.
- No auto-clean/delete of story data, no browser/Discord/AdBlock edits, no new dependencies. **No compile/runtime claims until Windows BUILD/Quick/Stress.**

## Next validation milestone

When ready to integrate, run `BUILD.bat`, then one Quick and Stress via `AUTO-TEST.bat`. A new `v2.29.5-preview11` version string distinguishes the run from the validated `preview7.1` baseline. Check particularly `Story memory persistence and retrieval`, performance of first reindex on an existing long story, and branch-switch/rollback data isolation.


---

## RELEASE-NOTES-v2.29.5-preview13.md

# WorkChat Mini — v2.29.5-preview13

**Based on:** v2.29.5-preview12.  
**Latest native Windows-tested baseline:** v2.29.5-preview7.1, Quick and Stress 22 PASS / 0 FAIL / 0 WARN.  
**preview13 has NOT been Windows-built or Quick/Stress validated.**

## Changes

1. Added `StoryEventContextMatcher.cs`: deterministic, read-only Event Ledger query matching combining a conservatively recognized action (`ItemLost`, `ItemAcquired`, `AbilityLost`, `AbilityLearned`, `Death`, `Injury`, `Discovery`), a uniquely resolved character, a concrete query clue, and optional exact chapter reference. It only surfaces **Confirmed** events with chapter provenance. Unrecognized actions use the prior retrieval flow.
2. Restricts resolved actors to event structured `Subject`, `Target`, or `RelatedCharacters`. An ambiguous nickname cannot be assigned to an arbitrary character; a different object, action or explicitly requested chapter will not match. "Mất tích" does not imply `ItemLost`.
3. The retrieval planner promotes these precise confirmed event boosts before wider graph/summary signals. It does not indiscriminately boost every old event about the same character when the question specifies an action. Missing/ambiguous contextual matches do not fall back to unsafe broad Event Ledger boosts.
4. Story writer context uses the same event filter for action-specific prompts. Only matching confirmed events can appear in that Event Ledger section; bookkeeping and other consumers retain the prior general-purpose context.
5. Added event-context regression assertions to the existing Story long-retrieval self-test: actor isolation, unique vs shared aliases, wrong chapter/object/action exclusion, rejected unconfirmed event, missing-item vs disappearance distinction, event provenance and writer context filtering.
6. Synced version metadata and build batch headers to preview13. No new dependency and no Memory schema changes.

## Boundaries / follow-ups

- A recognized action with missing evidence returns no confirmed event rather than inventing a match. This matcher is **not** full natural-language understanding, and synonyms outside its conservative list are not handled.
- Non-event chapter sections may still include historical context; current canon continues to take precedence over old prose.
- No UI, browser, Discord, notification, AdBlock, checkpoints, branch storage or providers were modified.
- All assertions are source-level until a Windows .NET build runs. User prefers grouped integration tests, not one test run after every preview.


---

## RELEASE-NOTES-v2.29.5-preview15.1.md

# WorkChat Mini v2.29.5-preview15.1 — Legacy chapter-index test hotfix

**Source baseline:** `v2.29.5-preview15` (Windows BUILD succeeded; Quick reported 21 PASS / 1 FAIL / 0 WARN). The last fully Windows validated Quick+Stress baseline remains `v2.29.5-preview7.1`.

## Root cause

`StoryLongRetrievalSelfTest` expected a late-chapter clue in `StoryChapterIndexEntry.Terms`. This was correct for the earlier sampled-term index assumption, but since index **schema v3**, the `Terms` list is capped at 1,800 sampled terms; complete chapter keyword coverage lives in the separate `TermSignature` Bloom filter. The Windows Quick test correctly found the clue in the returned actual chapter excerpt *before* reaching the stale assertion, but failed because the clue was not also in sampled `Terms`.

## Changes

- Corrected the migration regression test to verify `SchemaVersion = 3` and validate a correctly sized, deserializable full-chapter `TermSignature`.
- Explicitly checks tokens from chapter start, middle and end plus the late secret. Still checks actual verified text retrieval, index persistence, and bounded sampled terms.
- Left the production retrieval pipeline, legacy-index upgrade code, Memory schema v6, chapters, branch data, and browser untouched. No production behavior changed.
- Version labels now identify `v2.29.5-preview15.1`, assembly/file `2.29.5.18`.

## Validation and next step

Static checks only in this environment: source diff, ZIP CRC, XAML/XML, line counts, batch BOM/CRLF. **Windows Quick/Stress were not run here.** Run `AUTO-TEST.bat` once to validate the complete accumulated preview8–15 features; if Quick fails again, share the exact new assertion from the report.

Note: `latest(1).log` uploaded with the report was from a normal app session (`selftest=False`), not the failing self-test; it also recorded low overall system memory during browser initialization, unrelated to this index-test assertion.


---

## RELEASE-NOTES-v2.29.5-preview16.md

# WorkChat Mini v2.29.5-preview16 — knowledge provenance & alias safety

**Based on:** v2.29.5-preview15.1 (Windows Quick + Stress 22/0/0, 2026-10-09)

## Changes

1. `StoryKnowledgeProvenance.cs` adds read-only, bounded chapter provenance for **current** character knowledge only. An event qualifies only when it is `KnowledgeGained`, `Confirmed`, attributed to a uniquely identified subject and tied to an existing committed chapter. The target must still exist in the character's current `Knowledge` list. Forgotten knowledge, orphan chapters, proposed events and ambiguous aliases do not become present knowledge.
2. `StoryKnowledgeBoundary.cs` adds up to two concise knowledge-source citations per relevant character to the author-level POV context. Citations are historical learning evidence, not an assertion that the underlying belief is currently true after a retcon.
3. `StoryEventLedger.cs` fixes a pre-existing unsafe `FirstOrDefault` character resolution: an event referring to a **shared alias** no longer grants items, abilities or knowledge to the first matching character. A canonical unambiguous subject continues to update as before. Event history still records the incoming event; the ambiguous event does not mutate current character state.
4. Regression assertions were added inside `StoryLongRetrievalSelfTest.VerifyAsync` (no additional top-level self-test case): shared-alias merge, canonical-subject merge, confirmed knowledge-source attribution, proposed/ambiguous/orphan event rejection, and removed knowledge staying removed.

## Scope / compatibility

- No change to Story Memory schema v6, project files or branch storage.
- No new package dependencies, API calls, UI controls or database.
- No browser, Discord, AdBlock or notification changes.
- All provenance is derived read-only from the **active branch memory**.

## Validation status

Static/package validation only in the authoring environment, with no Windows WPF compiler. **Preview16 is NOT Windows build/Quick/Stress validated**. The most recently tested baseline is v2.29.5-preview15.1. Keep that ZIP available for rollback.

If the next Windows build reports an error, provide compiler output and/or Quick/Stress reports; no need to run repeated full stress cycles for a simple compile fix.


---

## RELEASE-NOTES-v2.29.5-preview17.md

# WorkChat Mini v2.29.5-preview17 — Character identity & epistemic continuity

**Source baseline:** `v2.29.5-preview16` (static audit only).  
**Most recent Windows-validated baseline:** `v2.29.5-preview15.1` (Quick + Stress: 22 PASS / 0 FAIL / 0 WARN).

## Changes

1. **Fix an unguarded character patch merge:** `StoryMemoryMerger.MergeCharacter` previously used `FirstOrDefault` over canonical names or aliases. When two characters shared an alias, a Bookkeeping `CharacterPatch` could wrongly grant Knowledge, Inventory, Abilities, or other state to the first matching character. It now rejects ambiguous identities without changing either character.
2. **Unified deterministic identity resolution:** new `StoryCharacterIdentity.cs` resolves a unique canonical name first, then a unique alias. Duplicate canonical records or shared aliases are ambiguous. The same rule is used by Event Ledger state changes, Knowledge provenance, and deterministic continuity lookup. A truly new, non-colliding canonical character may still be created.
3. **Conservative epistemic guard:** new `StoryEpistemicUpdateGuard.cs` flags ambiguous character patches and exact matches where a new character Knowledge entry reproduces another character's private Secret without a matching **Confirmed `KnowledgeGained` event** for that recipient. These are **Risk**, not **Conflict**: insufficient structured evidence does not prove ignorance, and the guard does not silently erase a legitimate reveal.
4. **Bookkeeping extraction instructions:** require unique canonical recipient names and explicit `KnowledgeGained` provenance for durable on-page discoveries. `relatedCharacters`, POV, and shared nicknames are not proof of learning.
5. **Normalize before deterministic validation:** explicit null arrays in bookkeeping JSON are normalized consistently before checks, matching the shape already expected by MemoryMerger.
6. **Extended regression assertions:** shared-alias `CharacterPatch` cannot change another character, canonical updates and new characters still work, ambiguous updates surface a Risk, Proposed learning cannot clear a secret-transfer warning, Confirmed learning can clear that specific warning. These are included in an **existing** Story self-test case; top-level test count remains 22.

## Safety and compatibility

- No schema migration. Story Memory remains **v6**.
- No branch/rollback/checkpoint format changes and no new dependencies/API calls.
- No browser, Discord, WebView2, AdBlock, notification, or Story UI modifications.
- No automatic retcon, merge, secret removal, or guesswork when identity is ambiguous.

## Validation / testing

Source/package static checks only. There is **no Windows WPF/.NET SDK in the authoring container**, so this build has **not** passed Windows BUILD, Quick or Stress. Do not replace the `preview15.1` validated baseline until Windows tests pass. As agreed, no new AUTO-TEST request for this incremental feature pass.


---

## RELEASE-NOTES-v2.29.5-preview18.md

# WorkChat Mini v2.29.5-preview18 — Historical Knowledge Timeline

**Source:** v2.29.5-preview17. **Last Windows validated baseline:** preview15.1 — Quick/Stress 22 PASS / 0 FAIL / 0 WARN. Preview17 and preview18 are not validated Windows builds.

## Changes

1. **Read-only Knowledge Timeline** (`StoryKnowledgeTimeline.cs`). For explicit flashbacks or rewrites of an existing chapter (for example, `viết lại chương 5, POV: Magica`), derive what a character demonstrably learned before that chapter from Confirmed `KnowledgeGained` events with committed chapter provenance. Learning occurring within the target chapter is flagged as not necessarily available at its beginning. Future discoveries are counted but their *contents* are withheld from the historical POV.
2. **Identity/provenance guards**: only uniquely attributed character events are considered, ignoring ambiguous nicknames, Proposed events, duplicate event IDs, and chapters absent from committed memory.
3. **Historical writer context**: for explicit past-chapter requests, no current character knowledge, inventory, relationships, location, active arcs, future event ledger, or future chapter excerpts are presented as past-state evidence. Previous committed chapters and historical events remain available. Current Canon and revision history stay clearly marked *author-only retcon constraints*.
4. **Normal Continue Story unchanged**: ordinary forward writing, existing Story Genres/Temporary Tags, the writer pipeline and provider routing are untouched. No new API calls or dependencies.
5. **Self-test extension**: adds historical POV/provenance checks to the existing Story memory test (22 top-level self-test cases retained). Covers prior, same-chapter and future learning; current-state leakage; Proposed, ambiguous and orphan provenance; and ensures normal Continue Story still uses current memory.
6. **Version alignment**: Build, Auto-Test, project version and informational version consistently indicate preview18.

## Known limits / why this is conservative

- A historical learned-event is **not proof a character retained the fact forever**. Older project memories may lack chapter-level forgetting provenance. Missing learning events mean **not verified**, never proof the character was ignorant.
- The local timeline is chapter-granular, not scene/paragraph-granular. Even a Confirmed event in the target chapter cannot be assumed at its opening.
- Automatic chronological inference from unstructured prose, arbitrary in-world dates and time-travel sequences is intentionally not attempted.
- The scope switch activates only on explicit flashback/rewrite requests referencing a chapter already committed; plain `tiếp tục` remains unaffected.

## Verification status

Only static source/package inspection can be performed here (Windows WPF/.NET SDK unavailable). This **does not constitute a compile or runtime PASS**. Do not replace the validated preview15.1 baseline until a Windows BUILD and Quick/Stress test pass at the next planned integration checkpoint.


---

## RELEASE-NOTES-v2.29.5-preview19.md

# WorkChat Mini v2.29.5-preview19 — Character Development Continuity

**Source base:** v2.29.5-preview18 (Knowledge Timeline).  
**Last Windows-validated baseline:** v2.29.5-preview15.1 — Quick/Stress 22 PASS, 0 FAIL, 0 WARN.  
**This source:** static-checked only; Windows BUILD, Quick and Stress have **not yet run**.

## Feature changes

- **Committed Character Turning Point History:** Added `CharacterDevelopmentHistory` as an optional, backward-compatible field in existing Story Memory schema v6. Saves the stage/direction before and after a character arc change, with canonical character, arc title, description, source and committed chapter number. No migration is required for existing stories.
- **Provenance guard:** Only records a new turning point when `LastChange` is new, a committed chapter exists in the active branch and the character identity is not ambiguous. The description originates from bookkeeping of the final committed prose, not from research or drafts; the code does not independently prove the natural-language claim is present verbatim in the prose.
- **Bounded history:** Retains at most 240 character-development changes per active branch, avoiding unbounded memory or checkpoint growth. It is included in the per-branch memory snapshots, so rollback and branch switching inherit existing isolation.
- **Continue Story:** The Narrative Focus places up to 3 recent committed turning points and 2 relationship transitions early in the guidance. Growth remains cumulative; a justified relapse is allowed, rather than treating all regression as an error.
- **Reader retrieval:** An additional ranked `CHARACTER DEVELOPMENT HISTORY` section lets targeted questions find previous changes without confusing them with the current character-arc stage.
- **Deterministic Risk guard:** Changes of an existing arc's stage/direction without a new explicit `LastChange`, or an unsupported reopening of a resolved arc, produce a `Risk` diagnostic (not a hard block). Shared aliases never mutate the first matching person's arc.
- **Resolved Arc stability:** Default `Active` in a patch cannot silently reopen an already `Resolved` arc without a new change description.
- **Bookkeeping guidance:** Stable title reuse, prose-grounded turning points, and no needless rewriting of `Personality` from a passing mood.
- **Regression assertions:** Verified histories, duplicate writes, ambiguous nicknames, no-evidence risks, earned reversal, resolved status, JSON roundtrip, old v6 data, and Narrative Focus insertion within the existing Story self-test; total **22 top-level tests**, not increased.

## Compatibility and scope

- No new packages, extra AI request stages, WPF controls, or browser changes.
- Memory schema version remains **6**, with one optional additive list. Historical data is **not fabricated** for old projects.
- Only source in `_Internal/Source/AI` and version/build labels changed.
- All C# and XAML remain under 1,000 lines per file.
- ZIP is source-only, not a prebuilt executable.

## Test plan (at next integration milestone)

1. Build with `BUILD.bat` using Windows/.NET SDK. Compiler warnings are errors.
2. Quick then Stress (target 22/0/0 each), focusing on the existing Story memory/retrieval case.
3. Practical test: Continue Story across two committed character turning points, then switch branches/rollback to ensure no turning point leaks.


---

## RELEASE-NOTES-v2.29.5-preview2.1.md

# WorkChat Mini v2.29.5-preview2.1 — Compile hotfix

## Compiler errors fixed

- `StoryPanel.Checkpoints.cs:24` — `HorizontalAlignment.Left` => `System.Windows.HorizontalAlignment.Left`.
- `StoryPanel.Checkpoints.cs:67` — same qualification.
- `StoryPanel.Checkpoints.cs:87` — `Orientation.Horizontal` => `System.Windows.Controls.Orientation.Horizontal`.

The project enables both WPF and Windows Forms, so WPF enum names are explicitly qualified.

## Scope

- No change to checkpoint, rollback, recovery, memory, browser, Discord, AdBlock or notification behavior.
- Version metadata and build/test batch banners bumped to identify the hotfix.
- No additional dependencies, tests, or UI added.

## Validation needed

The archive was statically checked only. .NET/WPF Windows compile, publish, Quick and Stress still need to be run on the user's Windows machine.

Run `AUTO-TEST.bat`; it builds and runs Quick and Stress, saving reports in `_Internal\Reports`. Expected report build text: `2.29.5-preview2.1-checkpoint-ui-compile-hotfix`.

Do not treat v2.29.5-preview2.1 as a validated baseline until both tests pass with `22 PASS / 0 FAIL / 0 WARN`.


---

## RELEASE-NOTES-v2.29.5-preview20.md

# WorkChat Mini v2.29.5-preview20 — Character Decisions + Relationship Identity

**Based on:** v2.29.5-preview19.
**Last validated baseline:** v2.29.5-preview15.1 (Windows Quick/Stress: 22 PASS, 0 FAIL, 0 WARN).
**Preview20 validation:** Source inspection only. Windows BUILD/Quick/Stress have **not been run**.

## Purpose

Keep Continue Story's creative freedom while grounding character choices in **already committed character turning points and relationship changes**. Avoid turning short-lived emotions into permanent personality resets, or assigning a shared nickname to the wrong character.

## Changes

1. **Character Decisions focus:** `StoryCharacterDecisionFocus.cs` joins up to three relevant characters' committed turning points, current active character-arc stage/direction and recent committed relationship shifts into a small (≤1,700 characters) Narrative Focus section. It emphasizes agency, earned contradictions and plausible reversals; it **does not prescribe the next plot beat** or infer causation from temporal proximity.
2. **Directional identity-safe relationships:** `StoryRelationshipLedger.cs` resolves unique character aliases to canonical names before writing changes, rejects ambiguous aliases and self-pairs, and updates a single uniquely matched legacy nickname edge instead of adding a duplicate. For new cast not yet registered, the original supplied names remain usable.
3. **Do not silently pick among duplicate legacy edges:** Where multiple existing rows represent the same canonical from→to pair, automatic merge skips the ambiguous update rather than selecting the first.
4. **Identity-aware diagnostics:** Existing contradictory relationship states recorded once under a name and once under its unique alias can be identified as a conflict. Ambiguous or self-identity patches, and a relationship-state reversal without a `change` explanation, receive conservative diagnostics. An explained reversal is **not blocked**. The existing self-relationship sanitizer now handles a character's two names as the same person.
5. **Bookkeeping instruction:** Uses unique canonical names for both participants, treats direction as significant and asks for an actual on-page cause for durable relationship changes. No extra AI calls or schema changes.
6. **Regression coverage:** `StoryCharacterDecisionSelfTest.cs` runs inside the existing Story memory/retrieval top-level case. Tests unique and shared aliases, legacy edge normalization and conflict recognition, deduplication, self-relationships, missing-evidence Risk vs explained changes, joined Continue Story context and exclusion of uncommitted drafts. Total 22 top-level cases remains unchanged.

## Safety and limitations

- Story Memory schema remains v6. No automatic rewriting/migrating all old memory, and no destructive merge of contradictory historical entries.
- Context is derived from the active branch's Story Memory; only chapter-numbered records linked to an existing committed chapter are included. A bookkeeping description is not independently verified against the entire saved prose here.
- No new packages or UI, no change to Core Genres/Temporary Tags, provider routing, WebView, Discord call guard, AdBlock, notifications or browser resource saving.
- The current source is **not a validated Windows build**. `BUILD.bat` then Quick/Stress should be run at the next integration milestone, not necessarily immediately after this preview.


---

## RELEASE-NOTES-v2.29.5-preview21.md

# WorkChat Mini v2.29.5-preview21 — Character Motivation & Decision Provenance

**Base:** `v2.29.5-preview20` (source ZIP).  
**Last Windows-validated baseline:** `v2.29.5-preview15.1`, Quick + Stress both 22/0/0.  
**This release:** Static checks only. No .NET SDK / Windows WPF compiler is available in this environment. Do not treat preview21 as BUILD/Quick/Stress PASS.

## What changed

1. **Durable decision events without a new memory schema.** `StoryEventLedger` recognizes `DecisionMade`, `SacrificeMade`, `GoalAbandoned`, `PromiseKept`, and `PromiseBroken` in addition to existing `PromiseMade` and `GoalChanged`. Events keep the existing confirmed/proposed status, subject, target, summary, evidence and committed chapter provenance. A normal non-pivotal choice must not become a durable event.
2. **Motivation/decision evidence in Continue Story.** `StoryCharacterMotivationFocus` adds a bounded (maximum 1,450 characters) **CHARACTER CHOICES & CONSEQUENCES** section derived exclusively from the active story's committed, Confirmed events with explicit evidence and uniquely identifiable actors. It can show current goals as *current state*, not as retroactively inferred motives.
3. **No invented causality.** It only shows a cause of a decision through an explicit `MOTIVATES` link between unique Confirmed events, or an explicit consequence through a `CAUSES` link. Both endpoints and the link itself must have committed chapter provenance; duplicate event IDs/summaries are not accepted as unequivocal endpoints. The same event's chronological proximity is never sufficient.
4. **Bookkeeping instruction updated.** New event types are extracted from final narrative only. Abandoning a tracked goal requires both `GoalAbandoned` in the historical Event Ledger and matching `characters[].removedGoals` for current state. Reader-facing `CharacterPromise` entries remain in the existing Narrative Promise system and are not automatically resolved by passage of time. No additional model/API stage.
5. **Non-destructive continuity warnings.** `StoryCharacterMotivationGuard` flags a confirmed decision with no on-page evidence or a goal-abandonment event that fails to remove an existing goal. These are **Risk**, not a forced block, destructive patch or automatic retcon.
6. **Regression coverage.** New self-test class is invoked inside the existing Story memory/retrieval case (total remains 22). Covers merge and type normalization, goals removed, explicit causal/MOTIVATES provenance, shared aliases, Proposed/uncommitted records, missing chapters, forged links, duplicate event IDs and non-blocking state-consistency warnings.

## Scope and safety

- No new Story Memory schema or file format; Memory stays **v6**.
- Branch/rollback/checkpoint storage, browser, media/Discord, adblock, notifications, API providers, genres and temporary tags are unchanged.
- Decision context is historical evidence, **not an instruction forcing the next plot beat**; current canon and retcons remain authoritative.
- No migration, deletion of user data, dependency or new API calls.
- Still needs a Windows BUILD and Quick/Stress at the **next integration test checkpoint**, not necessarily immediately.

## Test/validation caveat

Source/ZIP/XML/XAML/batch integrity checks are static only. The new C# tests are present but have **not run** yet. If Windows compile or self-test fails, repair against this preview21 source and re-run only the necessary stage before declaring it validated.


---

## RELEASE-NOTES-v2.29.5-preview22.1.md

# WorkChat Mini v2.29.5-preview22.1 — Compile Hotfix

**Based on:** Full source `v2.29.5-preview22`.

## Fixed

- `StoryMemoryRetriever.cs` (historical context): corrected nullable `StorySceneStateMemory.ChapterNumber` handling before `HashSet<int>.Contains`.
- Before: `committed.Contains(x.ChapterNumber)` passed `int?` to a method that requires `int` (`CS1503`).
- After: `x.ChapterNumber is int sceneChapter` and `committed.Contains(sceneChapter)`, preserving only positive, earlier, committed chapter scenes for historical retrieval. Null chapter numbers are ignored.
- Version metadata, `BUILD.bat`, `AUTO-TEST.bat`, and `VERSION.txt` updated to `preview22.1` so logs identify the exact build.

## Unchanged

No changes to Bookkeeping, browser, Gemini requests, Narrative Focus, Story Memory v6, Branches, checkpoint formats, user data or existing self-test assertions.

## Validation status

Static source, XAML/XML and ZIP integrity checks completed. **A real Windows `dotnet build`/publish and Quick/Stress were NOT run in this environment.** Please run `BUILD.bat` first. If build succeeds, run `AUTO-TEST.bat` for integration; do not assume PASS until reports confirm it.


---

## RELEASE-NOTES-v2.29.5-preview3.md

# WorkChat Mini v2.29.5-preview3 — Transactional chapter commit recovery

Base: v2.29.5-preview2.1, which Windows Quick and Stress both passed (22 PASS / 0 FAIL / 0 WARN on 2026-10-09).

## Changes

- Added a distinct `branches/pending-commit.json` transaction journal, written **after** the pre-commit checkpoint but **before** chapter, canon, or conversation mutation.
- A chapter is durably committed only after chapter prose, Story Memory v6, and the message's `ManuscriptCommitted` metadata (plus alternate sibling flags) are persisted together.
- Journal has Prepared/Committed states. An interrupted Prepared commit is **automatically rolled back** to its immutable pre-commit checkpoint before the story can be loaded; a Committed marker is cleaned without rolling back the finished chapter.
- Interrupted partial commits are **never captured as a valid alternate branch** during recovery.
- The existing restore journal handles file replacement; it is safe to retry after an interrupted restoration.
- In-process errors and cancellation during an active transaction reuse the same restoration path. Cached chapter text and index are invalidated after restore.
- Expanded the existing **Story memory persistence and retrieval** self-test with three deterministic cases: interrupted Prepared recovery, successful committed write, and leftover committed marker recovery. The suite remains 22 cases.
- No changes to Browser, Discord, AdBlock, notification behavior, icons, dependencies, Story schema v6, or user data migrations.

## Limitations

- This stage hardens commit/rollback, not yet a full named multi-branch tree, merge engine, or UI timeline.
- Crash simulation verifies on-disk recovery states; it does **not** kill the Windows process in the middle of file writes.
- Checkpoint snapshot files still contain a complete structured-memory and conversation JSON; chapter prose objects remain deduplicated by SHA-256. No garbage collection/deletion is introduced.
- Past chapters without checkpoints are not retroactively rewindable.
- An in-flight transaction without its committed marker is deliberately rolled back, even if its files were fully written immediately before the crash. Safety takes priority over keeping the last chapter.
- The authoring environment does not provide the Windows WPF .NET SDK. Source was statically checked, **not Windows compiled or runtime-validated**.

## Suggested validation cadence

No need to run Quick + Stress after every small feature. At the next integration milestone, build once with `BUILD.bat`, and run Quick + Stress only after the compiler passes. If compilation fails, send its errors; do not spend time repeatedly running self-test.

## Follow-up

- Named versions / branch navigation without duplicating whole project folders.
- Recovery UX for failed journal validation and optional integrity diagnostics.
- Larger story memory performance and branch retention only after measured needs.


---

## RELEASE-NOTES-v2.29.5-preview4.md

# WorkChat Mini v2.29.5-preview4 — Named Story Branches

Base: v2.29.5-preview3 (transactional chapter commit recovery). Last **Windows-validated** baseline remains **v2.29.5-preview2.1** with Quick and Stress both 22 PASS / 0 FAIL / 0 WARN.

## Implemented

- Named **Story Branches** in Story Memory → **Story Branches / Versions**:
  - Create a new branch from the current committed story state, leaving the existing story as **Main** on first use.
  - Rename a branch; switch between named branches with inline confirmation.
  - Switching away captures the live branch's latest chapters, structured memory v6, and conversation before restoring the target.
  - Branches point to immutable, content-addressed checkpoints: no full project directory clones.
  - At most 20 branches per story; branch names are unique per story.
- **Crash-resumable switching**:
  - A durable `branches/branch-index.json` stores branch heads, active branch, and any pending switch.
  - A pending switch is written before live chapter files are changed.
  - Restore uses the checkpoint journal and the source branch's saved head, not a possibly half-written live state.
  - On story load, checkpoint recovery runs first; then pending branch switch recovery completes before memory/conversation are loaded.
  - Bad branch metadata or missing/corrupt checkpoint blocks switching instead of silently resetting story data.
- **Memory isolation:** active chapter files, narrative promises, causal links, canon and conversation are replaced together. Chapter-text/index caches are invalidated after restoration. Generation sees only the active materialized branch.
- **UI:** Story branch controls share the existing modeless Memory inspector. Branch-navigation housekeeping checkpoints are hidden from the ordinary Restore Points list.
- Expanded the existing *Story memory persistence and retrieval* self-test to cover Main ↔ B switching, no cross-branch canon/conversation/Promise/Causal/retrieval leaks, rename persistence, and interrupted switch recovery. **22 total self-test cases unchanged.**
- Version metadata and BUILD header updated to preview4.

## Deliberate limits

- Not a Git-style merge engine. No merge, deleting branches, or garbage collection of old checkpoint objects yet. The current milestone prioritizes safe switching and retained history.
- A branch head is refreshed when switching **away** from the live branch; commits on the active branch remain directly in the live story until then.
- Checkpoint snapshots still include complete memory and conversation JSON, while chapter files use SHA-256 deduplication. Long-story size/performance work remains later.
- A crash is simulated with on-disk journal states; this is not a Windows process-kill test.
- Browser, Discord, notifications, AdBlock, icons, package dependencies, and Memory schema v6 are unchanged.

## Validation

- Static checks completed: XAML/project XML parsed, source files under 1,000 lines, batch files retain CRLF without BOM, archive integrity verified.
- **Not Windows compiled or runtime-tested.** The current environment has no Windows WPF .NET SDK. Do not mark preview3/preview4 as validated until the next planned integration BUILD + Quick/Stress.
- As requested, no AUTO-TEST is requested for each incremental build. When ready for the next testing milestone, run `BUILD.bat` once, then Quick + Stress if the build passes.


---

## RELEASE-NOTES-v2.29.5-preview9.md

# WorkChat Mini — v2.29.5-preview9

**Source base:** v2.29.5-preview8 (deep-checkpoint-integrity)

## Scope: long-story retrieval and context performance

1. **Bounded chapter-text cache** (`StoryChapterTextCache.cs`): 32 cached files max, ~8 MiB UTF-16 text max; LRU eviction and explicit invalidation on restore/branch change. Previously, every chapter ever read could remain referenced for the lifetime of the app.
2. **Full-chapter term sampling** (`StoryChapterIndexStore.cs`): bounded 1,800 distinct index terms sampled from opening, middle, and end instead of only the first 1,800. Index schema moved from 1 to **2** (disposable cache only, **NOT Story Memory v6**). Existing chapter index upgrades lazily during refresh; no project-data migration.
3. **Query-specific retrieval** (`StoryRetrievalPlan.cs`, `StoryRetrievalPlanner.cs`): unrelated recent chapters no longer appear as positive candidates merely for being recent. Continue Story retains an explicit recency preference.
4. **Dynamic total-context budget** (`StoryContextBudget.cs`, `StoryPanel.Memory.cs`, `StoryMemoryRetriever.cs`): a 24,000-character ceiling on composed Memory + deterministic warnings + Narrative Focus + provenance focus + Knowledge Graph. Memory gets up to 22,000 characters dynamically depending on the other sections, preserving space for current safety/continuity signals.
5. **Regression self-test** (`StoryLongRetrievalSelfTest.cs`): tests a late-chapter clue beyond 2,500 unique terms, upgrading a v1 index, irrelevant-hit suppression, chapter cache invalidation across content changes, cache eviction, and context budget. Called from existing **Story memory persistence and retrieval** test; still 22 self-test cases.

## Deliberately unchanged

- Story Memory schema stays v6; chapter/manuscript/branch/checkpoint format unchanged.
- No cleanup or deletion of story chapters, checkpoints, or archived branches.
- Browser, Discord call guard, WebView, notification handling, AdBlock and network providers unchanged.
- No new package dependencies, no vector DB, and no new UI controls.

## Validation status

- Source ZIP structure, C#/XAML line limits, XML parsing and batch format checked statically.
- **No Windows WPF compile/runtime tests in this environment.** Latest validated baseline stays **v2.29.5-preview7.1** with Quick and Stress both 22 PASS / 0 FAIL / 0 WARN.
- Follow-up Windows build + quick/stress should be performed at the next integration milestone as previously agreed.

## Limitations / next stage

- Term sampling is lexical and approximate; text not represented among the 1,800 index terms can still be missed without a provenance boost. This change improves long-chapter coverage but is not a semantic/vector retrieval system.
- Character-count context bounds are not tokenizer-exact token budgets.
- Legacy full-text fallback remains intentionally available if the disposable index fails.


---

## RELEASE-NOTES-v2.29.6-preview2.md

# WorkChat Mini v2.29.6-preview2 — Narrative Follow-through & Prose Craft

**Starting point:** v2.29.6-preview1 (user Windows Quick and Stress both 22 PASS / 0 FAIL / 0 WARN).  
**Status:** Source-only candidate. Not yet Windows BUILD, Quick, Stress or live Gemini-validated.

## Changes

### 1. Recent verified consequences in Continue Story

- New `StoryRecentConsequenceFocus.cs` offers at most two recent significant outcomes from the last three uniquely identified committed chapters.
- Selects only Confirmed events with evidence, unique event IDs and a recognized consequence type.
- Rejects Proposed, undated/uncommitted, evidence-less, repeated-ID and shared-alias events.
- Only offers past events as *optional follow-through guidance*: it never decides what scene must happen next, writes new canon, or infers causation.
- Integrates in `StoryNarrativeFocus` with a per-section quota and retains the global 2,380-character narrative context ceiling.

### 2. Less forced chapter endings

- Preparation prompt no longer requires an `ending/hook`; it requests a natural ending state/aftermath and accepts a hook only when earned.
- The Continue Story preparation, draft and editor guidance explicitly allows aftermath, silence, conversation and quieter scenes.
- Editorial polish is instructed not to introduce new backstory, injuries, reveals or relationship changes merely to create a twist.

### 3. Stronger local repetition signal

- Extends the existing editor diagnostic to detect a long sentence repeated three times within a single paragraph.
- The diagnostic is advisory, bounded to the existing 60K-character draft sample and never edits or rejects prose automatically.
- No additional Gemini stage/API request, schema migration, dependency or settings UI.

### 4. Expanded regression checks (still 22 top-level self-tests)

- Confirmed committed event inclusion, rejection of Proposed/uncommitted/no-evidence/duplicate ID/ambiguous nickname entries.
- Narrative Focus character budget and existing recent-chapter summary integrity.
- Detection of repeated long sentences without treating a short quiet passage as an error.
- No regression to a mandatory cliffhanger rule.

## Not changed

- Browser, notification policy, Discord/media guards, AdBlock and WebView resource policy.
- Story Memory schema v6; branches/checkpoints/rollback; canon/character/Event Ledger stored data.
- Continue Story autonomy, genres/tags, provider/model routing, research API gating or number of AI writing stages.

## Validation protocol

Run `AUTO-TEST.bat` on Windows once this integration candidate is ready for testing and send both Quick and Stress reports. Expected: 22 PASS / 0 FAIL / 0 WARN each. Static source inspection alone is **not** Windows BUILD success or proof of better literary output.

---

# WorkChat Mini v2.30.0-preview3 — Co-Creation Integrity & Conversation Safety

**Based on:** full-source `v2.30.0-preview2.1` (StringReader hotfix; not Windows-validated).  
**Windows-validated baseline remains:** `v2.30.0-preview1`, Quick + Stress 22 PASS / 0 FAIL / 0 WARN (user-supplied logs).  
**preview3 verification status:** static audit only. The packaging environment has no .NET SDK / WPF runtime, so Windows BUILD and 22-case Quick/Stress have NOT run.

## Changes

- Corrected the **numbered-option index-shift bug**. `Chốt: phương án 2` now matches label `2.` / `2)` in the *immediately prior assistant Chat reply*, not index 2 in a filtered list. Skipped short options cannot cause the wrong direction to be stored. Missing or conflicting duplicate labels fail closed. Bullet-only lists retain positional matching.
- Recognize both backtick and tilde fenced code as non-proposals; retain list numbers in UI. No new AI calls, no auto-commit and no canon updates.
- When editing a Gemini Chat response's creative direction, start with an empty editor rather than copying/truncating the entire assistant reply. The author must intentionally approve an edited direction. Keep/Edit/Archive work inside the existing native WPF panel.
- **Fail-closed corrupted conversation load**: `StoryProjectStore.LoadMessagesAsync()` throws an explicit error instead of treating unreadable `conversation.json` as an empty conversation. Story UI blocks actions and preserves the original file, avoiding a potentially destructive autosave. Previously-rendered Creative Ideas controls are cleared on failure.
- Expanded assertions inside the existing Story foundation/Co-Creation self-test: skipped option 1, option 3, conflicting duplicate labels, positional bullet fallback, code-fence exclusion, unresolved modified reference, and malformed conversation JSON parser refusal. The negative test uses a pure parser to avoid intentional ERROR log entries. Self-test fixture remains disposable; no user-story data is modified by test.

## Compatibility & non-goals

- Story Memory schema stays **v6**. No migration, changes to stored `StoryMessage` fields, branch snapshots, checkpoint object format, or provider behavior.
- Do not claim semantic conflict detection, AI prose quality improvements, or full Windows UI audit: those were not implemented/tested here.
- Shell, Discord call guard, media, notifications, AdBlock, service icons and tab limits remain untouched.
- All C#/XAML files are required to remain under 1,000 lines. Batch files remain UTF-8 no BOM with CRLF.

## Test on the user's Windows machine

1. Unzip to a separate folder; preserve `%LOCALAPPDATA%\WorkChatMini` and personal stories.
2. Run `BUILD.bat` (or `AUTO-TEST.bat` once, which performs build plus Quick/Stress). Target: Quick 22/0/0 and Stress 22/0/0. Never call the release validated without those results.
3. Smoke-test `Chốt: phương án 2` when 1 is too short; `Chốt: phương án 1` must **not** capture option 2. Test review/edit/archive/restore; open a 100+ message Story with Earlier messages; switch branches; inspect 100/125/150% DPI.
4. For corrupted-story recovery, first make an independent backup and use a disposable test Story. On load error, do not edit the unreadable JSON; inspect Logs and use an intact branch checkpoint/backup.

If Windows BUILD fails, send exact compiler output. Fix preview3 source, not a fallback ZIP.

---

# WorkChat Mini v2.30.0-preview4 — Co-Creation 3.0 / Creative Decisions

**Base:** Full source `v2.30.0-preview3`.
**Windows baseline:** `v2.30.0-preview3`, **Quick 22 PASS / 0 FAIL / 0 WARN; Stress 22 PASS / 0 FAIL / 0 WARN** (user's logs 2026-10-10 08:14 +07:00). Quick startup 1129ms / median switch 11ms; Stress startup 938ms / median switch 12ms; neither reported leftover WebView processes.
**preview4 validation:** static audit only. No .NET SDK/Windows WPF environment is available here: Windows BUILD/Quick/Stress must be rerun.

## Included changes

1. Explicit **Pass / Undo pass** per Gemini Chat option. This is creator intent only; unchosen alternatives, quotes, code blocks and narrative drafts are never automatically declined. Unaccepted ideas remain uncommitted.
2. Model context carries a bounded section of recently declined suggestions to discourage repeated pitches. It is clearly marked as non-canon and never treated as chapter events. The context remains capped at 2300 characters; no additional Gemini calls.
3. **Replace existing direction** selector during Keep idea. The author chooses an existing accepted idea; the old direction is archived in the SAME atomic conversation save. No naive semantic-conflict heuristic attempts to infer what to replace.
4. **Revision history and restore** for creative directions, with previous wording/time retained when changed. The prior wording is excluded from active writer context, but remains recoverable. Branch checkpoints snapshot the history naturally.
5. **Safer UI persistence**: one write-in-progress gate, multi-message state rollback on disk save failure, and a stable conversation array for save even if navigation changes. Send / branch / checkpoint operations are gated during this small window.
6. Extended **Co-Creation self-test assertions** inside the existing Story foundation test for explicit rejection, undo, invalid rejection, approval override, replace/archive, edit/restore, JSON persistence and long-session context budget.
7. No changes to Shell/WebView, Discord call/media guard, adblock, notification policy, provider network, Story Memory v6, checkpoint storage format or chapter canon.

## Stored data compatibility

`StoryMessage` gains optional JSON properties `CreativeDirectionRevisions` and `DeclinedCreativeSuggestions` (empty on legacy conversations). They are serialized inside `conversation.json` so existing branch/checkpoint files can carry them without migration. Previously-saved stories continue to load. Do not edit or delete personal story files to upgrade.

## On Windows

1. Extract full ZIP to a clean directory, preserving `%LOCALAPPDATA%\WorkChatMini`.
2. Run **AUTO-TEST.bat** once; expected both Quick and Stress = 22/0/0. If compile fails, send the first compiler error and surrounding lines. Static checks are not a compiler.
3. Smoke-test with a disposable Story: Gemini returns three ideas; **Pass** one, verify it appears in Creative Ideas and persists after restart; **Undo pass**; **Keep idea** for another; then choose **Replace** an existing direction; edit, view Revision history, restore. Switch to a separate branch and confirm its prior decisions are isolated.
4. If you use real Gemini prompts, check that declined ideas are not immediately re-suggested. Prompt guidance cannot absolutely guarantee the model never repeats them.

## Known limitations

- Automatic semantic conflict resolution between Vietnamese creative directions is intentionally **not** claimed: the creator explicitly chooses replacements.
- The negative guidance is bounded and consists of a few recent declines; it does not represent permanent cross-story blocking.
- The new WPF UI has not been visually inspected at Windows 100/125/150% DPI. Built-in technical self-tests are not a substitute for manual UI review.
- Source-only XML/static checks in the packaging environment do not confirm a successful C#/WPF compilation.

## 2.30.0-preview22 (2026-10-11)
- Story Scene Compass: latest committed chapters, unresolved threads, promises and character goals in a lightweight inspector, with optional editable Continue Story cue and strict no-canon behavior.
- Fixed local + GitHub binary update ZIP validation independent of entry order and added archived-EXE SHA-256 release check.
- Source/static verification completed; Windows release/Quick/Stress and actual storytelling playtest remain pending.
