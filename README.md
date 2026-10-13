# WorkChat Mini 2.43.0

## AI Story — read-only evidence auditing (2.43.0)

- **Story Memory → Overview → Audit sources** compares up to 120 stored event, canon, timeline, narrative-promise and causal-link references with up to 20 committed chapter files. It uses no AI calls and never writes Story files. Audit results appear temporarily in the current inspector; select Overview to return.
- Reports distinguish **exact excerpt found**, **chapter referenced with no quote**, **quote not found (could be paraphrase)**, **orphaned references / unreadable chapters**, and **not scanned**. A found quote does *not* prove interpretation, causation, or factual truth; no canon status is modified.
- Bookkeeping prompts now request a short **verbatim evidence snippet** in event/promise/causal-link fields rather than an AI-generated paraphrase, making future citation review more useful. Old memory data stays compatible.
- The Story Readiness self-test includes an offline source-audit regression test. Windows compile and Quick/Stress must still be verified by CI before publishing this source.

## AI Story — honest memory coverage & safe long-story repair (2.42.0)

- **No false completeness:** partial repair stores `NamesOnly`, never `Reviewed`. These chapters remain explicitly incomplete for canonical events, relationship history, and timeline; names-only scans will not re-run automatically and spend API quota again.
- **Longer stories:** name repair processes a maximum of 20 chapters per click, up to 500 indexed chapters. Larger projects are not silently processed in one huge operation. Completely empty projects with up to 20 chapters keep the safer chronological full-recovery path; bigger empty projects can still do a conservative names-only scan.
- **30,000+ character chapters:** a sampling-based bookkeeper cannot claim full extraction. These chapters retain an `Incomplete` receipt, including after full recovery when some original prose was omitted.
- **Protection while extracting:** chapter body SHA-256 is rechecked before Memory is committed. If source prose changed during provider work, Memory save aborts; no chapter text is overwritten. Hashing is kept off the WPF UI thread.
- **Visibility:** Memory Overview now shows counts of processed, names-only, incomplete, skipped, and unknown chapters, plus how many are eligible for the next bounded repair batch. Legacy receipts are normalized without deleting data.
- **Regression checks:** standalone offline tests cover 21- and 45-chapter repair batches, legacy status normalization, no repeated names-only scan, strict full vs partial coverage warnings, safety cap, and chapter content fingerprinting.

**Important:** `Reviewed` denotes a successful extraction from the available source text, **not** proof that every AI statement is factually correct. A names-only pass never updates existing canon or character state. The source bundle has not been Windows-compiled in this environment. Do not publish until GitHub Windows compile + Quick/Stress pass. No GitHub push was performed in this development pass.

## AI Story Memory coverage and partial repair — 2.41.0

- Every newly committed chapter records an explicit bookkeeping result: `Reviewed`, `Skipped`, or `Incomplete`. Existing chapters without that record remain `Unknown`; none of those states certifies perfect AI extraction.
- The Story Memory Overview now shows incomplete/unknown coverage even when some characters or canon facts already exist. It permits a **two-click, provider-powered** repair (1–20 chapters, validated committed files only) without re-creating any chapters.
- The new partial repair is intentionally **name-only**: it can add previously unknown character or place *names* only if those exact names occur as standalone mentions in committed prose. New locations are marked `Unverified`, character details are not invented, and existing inventory, relations, current location, events and canon are never changed.
- Bookkeeping for each chapter is isolated from later chapter summaries. Results are staged in a detached copy and applied once only after all AI requests succeed, following a checkpoint. Cancellation, corrupted input or model failure aborts the entire batch without a partial save.
- The AI's writing context now contains an explicit coverage warning when any committed chapters have unreviewed extraction; absence from structured Memory never proves that something did not occur. Legacy Story data needs no conversion and is not rewritten until the author requests repair.
- Quick/Stress self-tests now check coverage flags, duplicate and unsafe chapter rejection, exact-name evidence matching, preservation of existing facts/state, and no repeat processing for reviewed chapters. These source tests require Windows CI to actually run.

## AI Story author experience — 2.40.2

- Edit/regenerate the last uncommitted prompt, keeping previous AI response archived and checkpointed.
- Explicitly save the latest long Chat answer as a chapter, with optional automatic Memory extraction.
- Discuss/Scene Compass/Continue Story selections stay available during Gemini requests (no second request until the current one finishes).
- Learn the limitations and local Story backup workflow in [AI Story 2.40.2 notes](docs/AI-STORY-EDIT-MEMORY-2.40.2.md).


Windows app combining five communication/media tabs and AI Story.

## Install / update

**If WorkChat is already installed**, do not uninstall or delete your Story data.
After GitHub Actions releases `v2.43.0`, open **More → Updates → Check** in WorkChat and install the newer version. If the old app has no feed, choose `WorkChat-Update.zip` from [Releases](https://github.com/PHai237/WorkChatMini/releases). This ZIP contains **only** `WorkChatMini.exe` and `update.json`; a source ZIP cannot be used in Updates.

**New Windows install:** download `WorkChat-Setup.exe` from Releases. No .NET SDK required.

**Publish new source (developer):** extract `WorkChat.zip` to an empty folder and run `PUSH.bat` (GitHub CLI must be logged in). After one `YES` confirmation, it pushes only to a temporary validation branch first, waits for a REAL Windows WPF Release compile, and **only on PASS** advances `main` and requests the release workflow. If compile fails, it prints the failing job log and leaves `main` untouched. It does **not** build on your laptop. A successful preflight proves compilation, **not** Quick/Stress or a working installer; wait for the full release workflow and Pages job before updating.

## AI Story UI and Memory reliability — 2.40.3

- Main writing toolbar now shows **Discuss**, **Continue Story**, **Scene Compass** and **More tools**; advanced actions are still available with one click. Selected actions are visibly highlighted and auto-revealed when chosen from the welcome screen.
- Responsive Story layout gives the Inspector a bounded share of the screen, scales message bubbles with the actual chat column, keeps Edit/Retry readable, and makes the empty-story welcome scrollable instead of clipping its cards.
- Bookkeeping of chapters longer than 30,000 characters now preserves both the beginning and ending of the prose, with an explicit omitted-middle marker. **This is still partial coverage**: do not assume the middle was read or extracted. The original complete chapter is kept in storage.
- When bookkeeping fails or returns unusable JSON, the chapter can remain committed but the UI explicitly reports incomplete structured-memory extraction rather than silently implying that all characters/events were saved.
- Archived/superseded AI replies are now also excluded by the bounded conversation-context builder itself, preventing them from becoming writing evidence even if a future caller skips the separate draft filter.
- Self-tests include long-chapter excerpt boundaries and exclusion of superseded answers from writing context.

**Release safety:** The source ZIP is not an app update. Run the Windows preflight and release workflows; install through **More → Updates** only after both pass and `latest.json` points to `2.43.0`. No existing Story/credentials/profile files are touched by the source changes.

## AI Story reliability and chat UI — 2.40.2

- AI: after a no-text timeout/network failure on the primary Gemini model, try the existing alternate model rather than spending another entire timeout period on the same model. HTTP 503/429 recovery remains in place.
- Story: `↻ Retry AI` appears only when the **last** user prompt has no completed AI reply or the latest reply was explicitly interrupted. Resends the saved turn, does not duplicate it, and never automatically regenerates a committed chapter. Existing partial text stays marked incomplete/non-canon.
- Chat: bounded WPF Markdown styling for assistant responses (`**bold**`, `*italic*`, inline code and bullet list markers). Raw saved content/copy/Memory are untouched; active streaming stays inexpensive.
- Find: search bar hidden by default; **Alt+F** opens/closes; Escape closes. Inspector Close remains accessible while AI runs, and panel-open timings are written to Logs.
- Version 2.40.2 is **source only** until Windows compile, Quick/Stress and actual installation test pass. Do not install `WorkChat.zip` through Updates or mistake it for an executable release.

## AI Story readiness hardening — 2.40.0

The chapter retrieval planner now rejects duplicate/orphan chapter numbers in every source of ranking boosts, ignores Proposed/retconned canon as *evidence*, and excludes unconfirmed canon from the knowledge graph. A new **offline Story Readiness Gate** runs inside the existing Quick/Stress Story Memory test using a simulated 20-chapter story. It checks recent-chapter provenance, rejected drafts, stale chapter references and context budgets. It does not make extra Gemini calls or modify Story data. See [`docs/STORY-READY.md`](docs/STORY-READY.md) for what still needs real Windows and writing validation.

## Relationship Continuity — 2.38.0

During Continue Story, the private Narrative Focus now highlights up to two relationship changes relevant to the latest committed cast. Only unique committed chapter evidence is eligible; ambiguous nicknames, missing current relationships, stale states, duplicate chapter numbers and uncommitted scenes are excluded. A historical turn is not a compulsory new romantic/dramatic beat. The final continuity repair preserves previously established relationship state and prose whenever possible.

No new API request, user setting or persisted field. The 22 top-level self-test cases remain unchanged, with new in-memory assertions inside the Story memory/retrieval case.

## Story Integrity & Literary Repair — 2.37.0

- Fixed a confirmed chapter-retrieval safety hole: if the disposable search index throws, the **legacy fallback now reloads the committed manuscript ledger and scans only allowlisted chapter files**, never arbitrary orphaned Markdown. If the ledger itself cannot be read, retrieval fails closed and returns no chapter evidence rather than guessing. The fallback has committed/orphan/empty-ledger regression tests in the existing Story retrieval self-test.
- The existing continuity repair model stage now receives *minimal literary repair* guidance: fix grounded fact/knowledge/location contradictions without rewriting unrelated scenes, changing narration, adding twists or turning prose into an explanatory summary. **No additional API call** is added.
- App data, memory JSON, manuscript files, project names, binary updater format and GitHub release workflow remain unchanged. Windows BUILD/Quick/Stress remain to be validated by GitHub Actions.

## Stability audit — 2.36.0

- Closing Updates after a successful Verify now discards the unused staged EXE; choosing another package or starting a new check also discards superseded staging. After Restart & install launches, cleanup is left to the updater process. This never removes a user-selected ZIP or Story data.
- The updater's staging deletion is path-bounded and regression-tested in the existing Environment self-test.
- CPU diagnostics now discard stale WebView PID baselines once all sampled WebView processes have exited, with a regression assertion in the existing Telemetry test.
- Build/test and real Discord calls still require live Windows validation; no benchmarked RAM reduction is claimed.

## Audit hardening in 2.35.0

Story chapter retrieval now searches only chapters referenced by committed Story Memory. An orphaned `.md` left by an interrupted commit cannot become AI evidence. Tests cover the regression. No Story or login data is moved.

## AI Story — Literary Craft & Long-Session Polish

**Continue Story** now uses a bounded private *scene craft brief* during its existing preparation pass. The brief guides scene purpose, POV distance, subtext, emotional movement and an earned ending. The existing draft/editor passes use genre-sensitive line-edit guidance without changing plot decisions, canon or the creator's preferred language. No additional API round-trip is introduced.

The existing editor also compares the start and end of a candidate against up to three unambiguously **committed** recent chapters and flags only exact eleven-word echoes. Rejected drafts, interrupted responses and duplicated chapter numbers are excluded. This is advisory, not an automatic rewrite or a new canonical memory fact.

Story → Settings → Narrative direction → **Chapter craft feedback** now also supports **Stiff dialogue** and **Over-explaining**. Feedback remains per-story and applies to future Continue Story / Director, not retrospectively to saved chapters.

**New in 2.35.0:** Long Story writing-context selection now scans backward only until a bounded set of eligible messages has been collected. It still excludes uncommitted narrative drafts, interrupted outputs and the originating direction for rejected narrative drafts. A regression self-test covers long rejection histories and committed manuscript eligibility.

Literary Draft and Editor guidance now addresses synthetic foreshadowing, overly polished exchanges, repetitive scene openings, and translated-English phrasing in Vietnamese fiction. These are editorial suggestions; the creator's intent, canon and existing API stages remain authoritative.

In the Story reader, extremely long saved messages are no longer briefly copied in full into a WPF TextBlock before the 6k-character reading window is attached. Shared font instances and smaller, single-tile Story avatars reduce avoidable UI allocations. Normal conversation paging/search and full copy remain intact.

The closed-book Story icon remains, without nested gold backgrounds in the Story welcome screen, creation screen or message avatars.

`Scene Compass`, Story Director, Memory v6, manuscript review/commit, checkpoints and backup remain intact. Test on a separate story before trusting long-running manuscripts. App data remains outside the application installation directory.

### GitHub BUILD fix and update UI (2.40.0 release candidate)

- Fixed WPF compilation failure `CS0104 Brushes` in `StoryPanel.xaml.cs` by explicitly selecting `System.Windows.Media.Brushes.Transparent`.
- `PUSH.bat` now runs `SOURCE-CHECK.bat --nopause` before publishing; the static checker also recognizes this regression. **A real Windows BUILD is still required.**
- More -> Updates now checks the official GitHub Pages `latest.json` automatically. The manual `WorkChat-Update.zip` option is under **Advanced** as a fallback. `WorkChat.zip` is source and is **not** an in-app update package.
- Official update flow: **Check GitHub -> Download & verify -> Restart & install**. No automatic restart or install.

## Repository layout

- `.github/` — release workflow
- `_Internal/Source/` — WPF source and AI Story
- `_Internal/Updater/`, `_Internal/Installer/`, `_Internal/Tools/` — packaging and maintenance
- `docs/` — release notes and historical reports
- `BUILD.bat`, `AUTO-TEST.bat`, `SOURCE-CHECK.bat` — CI/developer commands (not required to use the installed app)
- `PUSH.bat` — protected GitHub publishing helper (Windows compile preflight before main)

Keep all user profiles, local API keys, build outputs, and test logs **out** of the GitHub repository.

### Git transport false-negative safeguard

`Ship-GitHub.ps1` verifies the exact remote commit SHA after **both** the temporary-branch push and the main promotion, regardless of the Git exit code. A GitHub `fatal error in commit_refs` rejection can coexist with a successfully created remote ref. The script proceeds only if `git ls-remote` proves the correct 40-character SHA, otherwise it stops safely without force pushing. This does not bypass Windows preflight or publish an uncompiled release.

## Missing Story Memory recovery — 2.40.4

- **Story Memory → Overview → Recover missing Memory** appears only if 1–20 committed chapter files exist **and every structured Memory collection is empty**. If you've already edited canon, characters, events or other Memory, this action remains unavailable rather than overwrite your work.
- Recovery reads the **committed chapter `.md` files** in chapter-number order, one by one, calls the selected provider's utility bookkeeping stage for each, and builds a detached structured-Memory candidate. Chapter N's retrieval context does **not** include later chapter summaries.
- Click the recovery button twice to confirm API usage (possibly up to 20 requests, subject to provider timeout, quota or cancellation). If any extraction fails or a deterministic continuity conflict is found, **nothing is committed**. A checkpoint of the original state is captured before one atomic Memory save. No chapter is appended, replaced, or edited.
- This is **conservative recovery**, not a full automatic rebuild of existing or partially populated Memory. It cannot guarantee AI extraction accuracy; review the recovered facts before trusting them for new chapters.
- Long-chapter bookkeeping now samples beginning, midpoint and ending inside the same 30,000-character budget, marking both omitted passages. It is **still partial coverage**, not full-chapter analysis.
- Quick/Stress sources now include offline recovery eligibility, duplicate filename/number, traversal protection and future-chapter isolation tests. Windows WPF compile and integrated tests must still pass via GitHub Actions before installing.

## GitHub publish discovery guard (2.43.0 source maintenance)

PUSH first runs an offline PowerShell 5.1 regression check of the GitHub Actions run parser. The publisher then queries GitHub Actions REST (`workflow_runs`, `head_sha`, `name`, `id`) instead of assuming each `gh run list` JSON item always has `headSha`. Missing runs are retried; malformed responses or repeated API failures stop before updating `main`. No release/version change is needed for this source-only publish fix.
