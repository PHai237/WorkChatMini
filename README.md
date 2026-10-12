# WorkChat Mini 2.40.0

Windows app combining five communication/media tabs and AI Story.

## Install / update

**If WorkChat is already installed**, do not uninstall or delete your Story data.
After GitHub Actions releases `v2.40.0`, open **More → Updates → Check** in WorkChat and install the newer version. If the old app has no feed, choose `WorkChat-Update.zip` from [Releases](https://github.com/PHai237/WorkChatMini/releases). This ZIP contains **only** `WorkChatMini.exe` and `update.json`; a source ZIP cannot be used in Updates.

**New Windows install:** download `WorkChat-Setup.exe` from Releases. No .NET SDK required.

**Publish new source (developer):** extract `WorkChat.zip` to an empty folder and run `PUSH.bat` (GitHub CLI must be logged in). It pushes versioned source to the existing repo and runs GitHub Actions; it does **not** build on your laptop. Confirm the release and Pages job are green before relying on auto-updates.

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

## Repository layout

- `.github/` — release workflow
- `_Internal/Source/` — WPF source and AI Story
- `_Internal/Updater/`, `_Internal/Installer/`, `_Internal/Tools/` — packaging and maintenance
- `docs/` — release notes and historical reports
- `BUILD.bat`, `AUTO-TEST.bat`, `SOURCE-CHECK.bat` — CI/developer commands (not required to use the installed app)
- `PUSH.bat` — GitHub publishing helper

Keep all user profiles, local API keys, build outputs, and test logs **out** of the GitHub repository.
