# WorkChat Story Companion — Complete Development Roadmap

Current implementation target: **v2.29.4 — Causal Memory + Setup/Payoff Intelligence**

This file is the master plan for the Story Companion. Keep it with the source package so the direction of the feature is never lost when development moves to another chat or version.

---


## Current UI rule

The Story surface must stay chat-first and avoid decorative labels that do not help the user act. Use ordinary terms such as Project, Memory, Genres, Role and AI. Do not rename basic controls into fantasy jargon. Medieval-fantasy identity should come from restrained color, typography, borders, pixel iconography and message styling—not from extra copy.

Project navigation uses a compact persistent list instead of a dropdown:
- 0 projects: show the empty state and **New Story** action;
- 1–2 projects: show compact story rows directly in the sidebar;
- many projects: keep the same rows in a bounded scrolling list;
- the active story is visually highlighted and each story exposes a delete action;
- deletion uses an inline destructive confirmation card in the story list; ordinary Story flows stay modeless.

Composer behavior:
- **Enter** sends;
- **Shift+Enter** inserts a newline;
- no permanent idle/local-storage/shortcut hint;
- status text appears only for active work, cancellation or errors.

### v2.27 visual baseline
The main Story surface follows the approved Figma **Archive** direction: a collapsible ~196 px story sidebar, centered story conversation, AI-left/user-right cards, a lightweight dynamic context strip, five story task suggestions, and a large multi-line composer with secondary tools kept in the bottom action row. The existing pixel `story.png` remains the Story Companion identity and AI avatar. Do not add demo-only theme/state switchers from the Figma prototype.

### v2.27.5 interaction baseline
Characters, Canon, Timeline, Lore, Threads and Memory are reference surfaces, so they open in a **modeless right-side Story Inspector** inside the workspace instead of spawning separate windows. Research follows the same model in v2.28 and stays hidden until a project actually has research notes. AI connection and genres live in **Story Settings**; Role remains the single selector in the composer. New Story is an inline editor in the main workspace. Content/setup dialogs and native destructive MessageBoxes are retired; story deletion uses an inline confirmation card.

Selection must never rely on a barely different hue: active navigation uses a left accent + surface fill, selected genre chips use a strong outline/fill + check mark, and closed dropdown text must remain high-contrast against the warm dark surface.



### v2.28.3 transport + pipeline performance
- Gemini transport prefers IPv4 when IPv4 is available, avoiding broken corporate/VPN IPv6 routes without changing WebView/network behavior elsewhere.
- Private utility stages use non-streaming requests; creative drafting can use buffered streaming, while creator-facing output streams normally.
- Gemini utility stages route to the low-latency Flash-Lite model while creative Draft/Editor stages keep the configured model.
- Draft Scene combines private Plan + Continuity Pre-check into one preparation call, removing one network round trip per draft.
- 429/503 capacity cooldown prevents later stages from repeatedly hammering a model that just reported overload.
- Chapter Markdown retrieval caches unchanged files by last-write-time to avoid rereading every chapter on every prompt.
- Stage logs now include configured/effective model, elapsed time, input/output size, and stream/non-stream mode.

### v2.28.2 Gemini resilience
- Gemini 408/429/5xx, network failures and request timeouts receive bounded automatic retries with exponential backoff + jitter.
- Repeated 429/503 on the selected Gemini model can fall back for that request to `gemini-3.5-flash-lite`; if Lite itself is selected, the secondary fallback is `gemini-3.6-flash`.
- Fallback never rewrites the creator's saved model choice. The selected model remains the preferred model on later requests.
- Raw provider error bodies remain in Logs; the Story bubble gets a concise human-readable failure only after retry/fallback is exhausted.

### v2.28.1.4 latency + compact-window hardening
- Story messages expose a subtle Copy action.
- Story scrollbars use the Archive visual language instead of default Windows chrome.
- The composer/sidebar/message widths adapt earlier on smaller windows.
- The status line exposes the exact model actually used, so model selection is never ambiguous.
- Gemini streaming has a short response-header deadline and falls back to ordinary `generateContent` for the rest of the session when SSE is buffered/stalled by a proxy or managed network.
- Simple/direct Gemini work requests low reasoning effort (Flash-Lite maps low-latency work to minimal where supported); polished streamed writing keeps medium reasoning.

## 1. Product goal

WorkChat Story Companion is a personal long-form fiction co-writer, editor, researcher and archivist inside WorkChat Mini.

The main problem it is designed to solve is not simply “generate good prose”. Ordinary chatbots tend to lose continuity after several chapters, mix up character knowledge, forget inventory/power states, repeat plot points, or immediately write from general model knowledge without checking real-world facts. Story Companion must instead behave like a long-term writing partner that remembers the project itself.

Core workflow:

**Recall → Research if needed → Plan → Continuity Check → Draft → Editor Pass → Final Check → Bookkeeping**

The model provider may change, but the story project and its memory belong to WorkChat.

---

## 2. Genre model

A story is not locked to one genre.

### Core genres
Persistent tags that define the long-term identity of the project.

Examples:
- Medieval Fantasy
- MMORPG / Game System
- Isekai / Xuyên không
- Rebirth / Trọng sinh
- Xianxia / Tu tiên
- Wuxia
- Romcom
- Romance
- School Life
- Slice of Life
- Adventure
- Action
- Mystery
- Political Fantasy
- Drama
- Comedy
- Dark Fantasy
- Urban Fantasy
- Apocalypse
- Sci-Fi
- Supernatural
- Horror

### Temporary scene/arc tags
Short-lived tags that only affect the current chapter, scene or arc.

Example:
- Project core: Xianxia + Rebirth + Romance
- Current arc: Mystery + Horror

Temporary tags must never overwrite the project’s core identity.

### Later: Story DNA
Optional controls such as:
- Lighthearted ↔ Dark
- Slow Burn ↔ Fast Romance
- Character-driven ↔ Plot-driven
- Grounded ↔ Power Fantasy
- Low Stakes ↔ High Stakes
- Comedy-light ↔ Comedy-heavy

---

## 3. Memory architecture

The AI must not rely on one giant chat transcript. Memory is layered.

### 3.1 Full manuscript
Every accepted narrative chapter/scene is stored in full on disk.

Purpose:
- never lose original prose;
- retrieve exact details from old chapters;
- support later continuity and search.

### 3.2 Chapter memory
Each stored chapter has:
- number;
- title;
- factual summary;
- full-text file reference;
- creation time.

### 3.3 Canon memory
Durable story facts.

Status:
- Confirmed — explicit project truth;
- Proposed — uncertain extraction that must not silently become canon;
- later: Contradicted / Retconned.

### 3.4 Character memory
Track per character:
- name and aliases;
- appearance;
- personality;
- goals and fears;
- secrets;
- **character-specific knowledge**;
- current location;
- condition/injuries;
- abilities;
- inventory;
- progression / cultivation / level;
- notes.

Character Knowledge is critical. A character must not act on a secret merely because the global story knows it.

### 3.5 Relationship memory
Track directional relationships:
- A → B state;
- recent changes/history;
- trust/affection/suspicion/rivalry can be added later when useful.

### 3.6 Timeline
Store chronological events and relative dates.

Special schemas later:
- Original Timeline vs Current Timeline for rebirth stories;
- school terms / ages;
- travel time;
- cultivation breakthroughs;
- deaths and reveals.

### 3.7 Open plot threads
Track unresolved:
- promises;
- mysteries;
- foreshadowing;
- threats;
- goals;
- unfinished conflicts.

Resolved threads remain marked instead of silently disappearing.

### 3.8 World / Lore memory
Store world rules and durable project facts such as:
- geography;
- nations;
- factions;
- religions;
- races;
- magic/cultivation;
- classes/skills;
- items;
- monsters;
- economy;
- politics;
- technology.

### 3.9 Style memory
Later, learn or store user preferences:
- chapter length;
- dialogue density;
- description density;
- humor;
- romance speed;
- POV;
- prose preferences;
- disliked clichés;
- pacing.

---

## 4. Retrieval rules

Do not send the entire project to the provider every time.

Before generation:
1. analyze the current prompt;
2. extract relevant terms/entities;
3. always include recent chapter summaries;
4. retrieve matching canon;
5. retrieve matching characters and their knowledge;
6. retrieve relevant relationships, timeline, threads and lore;
7. search old full chapter files for matching excerpts;
8. build a compact Story Memory context;
9. send only relevant memory plus recent chat to the provider.

Priority:

**Confirmed Story Canon > Project Lore > User instruction > Project Memory > Research Notes > Model general knowledge**

Proposed memory is labeled uncertain and must not be treated as settled canon.

---

## 5. Bookkeeping

Bookkeeping runs after substantial narrative output when enabled.

It is a separate AI pass because mixing prose generation and structured extraction in one response risks leaking metadata into the chapter and damages streaming UX.

Bookkeeping extracts only durable information actually established by the scene/chapter:
- chapter summary;
- canon facts;
- character state;
- character knowledge;
- relationships;
- timeline events;
- open threads;
- resolved threads;
- world/lore entries.

Rules:
- explicit facts → Confirmed;
- ambiguous but potentially useful → Proposed;
- no hidden inference;
- no invented inventory, romance, knowledge, deaths, or power changes;
- brainstorming does not automatically become canon.

The user can review Proposed memory in the Memory inspector and approve or discard it.

---

## 6. Research Gate

Planned for the Research phase.

The AI should research the web before drafting only when real-world accuracy materially helps.

Search examples:
- real locations;
- historical periods;
- customs/etiquette;
- architecture;
- clothing;
- food;
- professions;
- law;
- technology;
- travel/geography;
- specialist terminology.

Do not search when:
- project canon already defines the answer;
- the concept is fictional lore;
- the user intentionally wants custom rules;
- research adds no meaningful value.

Research Notes should contain source, key fact, relevance, confidence and access date.

External research must never silently override established project canon.

---

## 7. Writing pipeline

### Step 1 — Recall
Retrieve relevant Story Memory.

### Step 2 — Research Gate
Search only if real-world knowledge is needed.

### Step 3 — Plan
Silently reason about:
- chapter/scene goal;
- motivations;
- beats;
- conflict;
- emotional movement;
- comedy/romance beats;
- foreshadowing;
- ending hook.

### Step 4 — Continuity pre-check
Verify:
- who knows what;
- injuries/condition;
- location;
- power/progression;
- inventory;
- relationships;
- timeline;
- world rules;
- open threads.

### Step 5 — Draft
Write the requested prose.

### Step 6 — Editor pass
Check:
- awkward prose;
- repetition;
- rushed pacing;
- unnatural dialogue;
- exposition dumps;
- POV inconsistency;
- character voice;
- tonal drift.

### Step 7 — Final continuity check
Compare final prose to memory again.

### Step 8 — Bookkeeping
Update persistent memory.

---

## 8. Provider architecture

Story Companion must stay provider-independent.

Current/future provider interface:
- Gemini;
- OpenAI;
- Claude later;
- local/Ollama later.

Story data must not live only in provider chat history.

API keys:
- never hard-code;
- never commit to source;
- never store plaintext in normal settings;
- use Windows DPAPI / secure storage.

---

## 9. UI principles

The Story UI should feel like a focused writing room, not an IDE and not a giant settings panel.

Keep visible:
- project;
- genre tags;
- role/mode;
- provider/model;
- Memory entry point;
- conversation/manuscript;
- composer;
- Stop while generating.

Progressive disclosure:
- Memory details open only when requested;
- research details open only when useful;
- no permanent giant sidebar.

Visual identity:
- pixel book + quill icon;
- restrained medieval-fantasy brass accents;
- underlying product remains genre-neutral enough for romcom, xianxia, sci-fi, etc.

---

## 10. Storage plan

### Current v2.26 implementation
Per project:

```text
project.json
conversation.json
chapters/
  0001-title.md
  0002-title.md
memory/
  story-memory.json
```

Benefits:
- transparent and debuggable;
- no new database dependency;
- easy backup/export;
- full chapters remain readable Markdown;
- structured memory remains versionable JSON.

### Future scaling path
When real projects become large enough to justify it:
- migrate index/search to SQLite + FTS;
- keep Markdown full chapter files if desired;
- preserve a storage abstraction so UI/orchestrator code does not change;
- add embeddings/vector retrieval only if lexical/FTS retrieval demonstrably misses useful context.

Do not add a heavy vector database just because it sounds “AI-like”.

---

## 11. Performance rules

Story Companion must follow WorkChat Mini’s existing philosophy.

When Story is idle:
- no polling;
- no background API request;
- no background web search;
- no continuous indexing;
- near-zero CPU.

Memory retrieval runs only when generating.
Bookkeeping runs only after relevant narrative output and only when enabled.
All disk/network-heavy work stays off the WPF UI thread where appropriate.
Keep source files below 1000 lines whenever possible.

---

## 12. Development roadmap

### v2.25 — AI Foundation — DONE
- native Story surface;
- pixel book/quill icon;
- projects;
- core genres + temporary tags;
- roles;
- Gemini/OpenAI provider abstraction;
- secure API keys;
- streaming;
- cancel;
- persistent conversation;
- Story UI refresh.

### v2.26 — Story Memory — DONE
- structured persistent memory;
- full chapter Markdown storage;
- chapter summaries;
- canon;
- character memory;
- character-specific knowledge;
- relationships;
- timeline;
- open/resolved threads;
- world/lore entries;
- local relevant-memory retrieval;
- old chapter excerpt search;
- automatic bookkeeping for substantial narrative output;
- Proposed vs Confirmed memory;
- Memory inspector;
- auto-bookkeeping toggle;
- self-test for persistence + old detail retrieval.

### v2.27 — Story UI / Figma Archive integration — DONE
- Story Companion redesigned around the approved Figma Archive direction;
- compact story sidebar and project navigation;
- story-specific empty states and suggestion actions;
- AI-left / user-right conversation surface;
- context strip and large multi-line composer;
- real Story Memory controls integrated into the visual design;
- fidelity/compile hotfixes without changing Story Memory behavior.

### v2.28 — Research + Writing Pipeline — CURRENT
- narrative-only pipeline classifier so ordinary discussion does not pay multi-pass latency/cost;
- Recall → Research Gate → Plan → Continuity Pre-check → Draft → Editor Pass → Final Continuity Check → Bookkeeping;
- provider-native web research: OpenAI Responses `web_search`, Gemini Google Search grounding;
- per-project `research/research-cache.json` with query, relevance, confidence, access time and source URLs;
- 14-day exact-query cache to avoid unnecessary repeat searches;
- Research navigation appears only after a project has research notes; source links are visible/clickable in the modeless inspector;
- research/canon precedence enforced in shared prompts; research never auto-promotes to canon;
- hidden draft is not committed; only the final edited/repaired response reaches conversation/bookkeeping;
- final continuity checker can trigger a minimal repair pass for concrete contradictions/knowledge leaks; repaired output is verified once more;
- if final continuity cannot be verified, the polished answer is kept but auto-bookkeeping is skipped so unverified prose cannot silently enter durable Story Memory;
- pipeline stage status stays visible only while work is active.

### v2.28.1 — Story Actions v1 — DONE
- the five visible Story actions are real workflows rather than prompt-prefill shortcuts;
- explicit `StoryActionType` separates workflow routing from Role/persona;
- **Draft Scene** runs the full writing pipeline and may auto-bookkeep final verified prose;
- **Plan Chapter** runs Recall/Research/Plan/Continuity and returns a creator-facing plan without drafting prose or bookkeeping;
- **Check Continuity** calls the continuity reviewer directly and is read-only;
- **Build Character** returns a structured character proposal; nothing enters memory until the creator clicks **Add to Story**;
- **Expand Lore** returns checked lore proposals; **Save as Proposed** stores them as Proposed lore for later review/approval;
- proposal state is persisted with conversation messages so reopen/re-render does not lose whether an item was applied;
- Role no longer forces workflow routing, so `Writer` + `Check Continuity` cannot accidentally invoke Draft/Editor passes;
- normal free chat remains normal chat; the full writing pipeline requires the explicit **Draft Scene** action, preventing accidental multi-pass work from keyword matches.

v2.28.3.2 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.

### v2.29 — Advanced Story Intelligence
- genre-specific schemas;
- rebirth dual timelines;
- cultivation realm/progression model;
- MMORPG class/level/skill/equipment tracking;
- richer romcom relationship state;
- Story DNA;
- plot-hole detector;
- “what if?” Story Simulation;
- branch-to-canon workflow.

### Later — Personalization / optional fine-tuning
Only after enough original user-approved data exists:
- learn user style preferences from edits;
- export draft → edited pairs;
- optional fine-tuning of a compatible model;
- never use fine-tuning as a replacement for project memory or changing canon.

---

## 13. Success criteria

Story Companion is successful when:
1. a 20+, 50+, eventually 100+ chapter project retains important facts;
2. an old early-chapter detail can be retrieved when relevant;
3. character knowledge stays separate from global story knowledge;
4. destroyed/lost items and injuries do not casually reappear/reset;
5. relationships and progression do not jump without story support;
6. open foreshadowing can resurface many chapters later;
7. web research improves factual scenes without overriding canon;
8. switching AI provider does not erase the story’s memory;
9. projects remain isolated from one another;
10. WorkChat remains lightweight when Story is unused.

---

## 14. Non-goals

Do not turn Story Companion into:
- a giant browser inside a browser;
- a permanent dashboard full of metrics;
- a single hard-coded medieval/MMORPG writer;
- a “memory” system that is merely the last 100 chat messages;
- an always-running background agent;
- an AI that silently decides canon against the creator’s intent.

The user remains the creator/director. The AI remembers, researches, reasons and writes in service of that project.


## v2.28.4 — Auto Story / Continue Story

The primary low-effort writing flow is now `Continue Story`.

- The creator may provide only a short direction, or leave the composer empty and press Run.
- Continue Story always uses the writing pipeline and automatically plans the next meaningful beat from Story Memory, recent chapters, open threads, character state, and pacing.
- Per-project autonomy modes: Guided / Balanced (default) / Autonomous.
- Explicit tools (Plan Chapter, Draft Scene, Build Character, Check Continuity, Expand Lore) remain available for creator-controlled stories and do not inherit Continue Story autonomy.
- Auto-bookkeeping runs on the final verified prose. Private plans, research possibilities, and discarded options are not story truth and must not enter durable memory.
- Continue Story retrieval broadens its query with recent chapter summaries, open threads, locations/conditions, and character goals so a short prompt such as `Continue` can still retrieve useful older chapter context.
- Blank Continue Story submissions are allowed; the visible conversation records a simple `Continue the story.` creator message.

v2.28.4 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.


## v2.28.5 — Memory v2 / Event Ledger

Story Memory now keeps both current state and a provenance-aware history of durable changes.

- `StoryMemoryDocument` schema v2 adds a Significant Event Ledger without breaking existing v1 projects; old projects migrate in memory and are written as v2 on the next save. No fake historical events are backfilled from old timeline entries.
- Auto-bookkeeping extracts only meaningful, durable changes from the **final prose**. Private plans, research, discarded options and routine actions are not events.
- Event types cover ability/item/knowledge/relationship/location/condition/world/thread changes plus a Custom fallback. Events carry importance, subject/target, before/after, story-relative time, evidence, related characters/tags and source provenance.
- When generated prose is persisted as a chapter, the app stamps the event with the actual chapter number/title after saving; the model is not trusted to invent chapter provenance.
- Character current state supports two-way deltas: learned/lost abilities, acquired/lost inventory, added/removed goals and gained/forgotten knowledge.
- Confirmed ability/item/knowledge/location/condition events also apply conservative deterministic state effects so a bookkeeping JSON omission cannot leave obvious current-state contradictions.
- Retrieval includes relevant event history, allowing queries such as “when did Magica learn this skill?” to recover both the current ability and the chapter where it happened.
- Continue Story retrieval includes recent Major/Critical events so very short prompts still carry forward important progression.
- Memory Overview shows event count/recent significant events; Timeline includes the event ledger; Character inspector exposes abilities, inventory and progression.
- Existing 22-case self-test count is preserved; the Story Memory case now verifies schema-v2 persistence, event provenance, current-state merge and retrieval.

v2.28.5 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.


## v2.28.6 — Manuscript Commit + Story Arc Memory + Content Profile

Implemented on top of v2.28.5:

- per-project manuscript mode: Auto-commit narrative (default) or Review before commit;
- uncommitted narrative drafts do not mutate durable memory and are excluded from future writing-pipeline context;
- manual **Commit to Story** turns an approved draft into a chapter, then runs bookkeeping/provenance;
- Story Memory schema v3 adds long-running Story Arcs with chapter provenance and Active/Paused/Resolved state;
- Continue Story retrieval includes active arcs so autonomous generation can follow long-running goals instead of only recent scene threads;
- per-project Content Profile guides maturity, gore/violence, horror intensity, non-graphic intimacy boundary, and creator sensitive-theme notes;
- mature/dark subject matter is treated as story tone guidance while provider/platform boundaries remain authoritative.

v2.28.6 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.


## Shell patch v2.28.6.1 — AdBlock auto-page guard

No AI Story schema/workflow changes. Browser shell now suppresses non-user-initiated ad-blocker vendor/update/premium pages, scrubs known nuisance URLs from restored tabs/history, and preserves explicit user navigation.


## v2.29.0 — Advanced Story Intelligence

Implemented after the v2.28.6.1 shell patch:

- Story Memory schema v4;
- explicit creator-approved `Revise Canon` workflow;
- Retconned / Contradicted canon kept as historical revision records rather than current truth;
- arc priority + momentum;
- thread importance + chapter provenance;
- runtime Narrative Focus for autonomous continuation so long-running arcs and neglected threads stay visible without becoming forced plot instructions;
- ordinary bookkeeping is prohibited from silently retconning canon.

See `_Internal/AI/Docs/v2.29.0-ADVANCED-STORY-INTELLIGENCE.md`.

v2.29.0 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.


## v2.29.1 — Long-Story Retrieval Intelligence

Implemented as a lightweight local hybrid retrieval layer with no new external dependency:

- persistent per-project `memory/chapter-index.json` cache built from chapter Markdown;
- index refreshes only new/changed/deleted chapter files via last-write timestamps;
- entity-aware retrieval expansion across character names/aliases, abilities, inventory, events, arcs, threads, canon and revision history;
- significant-event provenance boosts the chapter where an ability/item/knowledge/relationship/world change actually happened;
- explicit `Chapter N` / `Chương N` references strongly pin that chapter;
- Continue Story retrieval uses recent chapters plus durable major events and active narrative state instead of relying on the tiny prompt such as “continue”;
- chapter full text is opened only for top indexed candidates rather than scanning every manuscript file on every generation;
- compact context now budgets sections so retrieved old-chapter evidence is not silently pushed out by large structured memory;
- current canon and revision history remain higher authority than historical chapter prose;
- corrupt/missing retrieval index is fail-open and rebuilt from Markdown, so the index is never story truth.

The existing Story self-test still occupies one of the 22 baseline cases and now verifies event provenance → chapter boost → indexed retrieval → memory context.

v2.29.1 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.

## v2.29.2 — Continuity State Intelligence

Implemented on top of the v2.29.1 retrieval layer:

- Story Memory schema v5 adds provenance-aware relationship history, per-character development arcs, durable location state, and bounded scene-state snapshots;
- relationship current state remains compact while important transitions keep chapter/source provenance;
- Character Arcs track durable growth/regression/transformation separately from plot arcs, so Continue Story can develop people without treating every mood as a new plot thread;
- location/scene graph tracks explicit physical presence, POV, scene location/time, important location objects/connections, and destroyed/inaccessible place state;
- scene merges conservatively update character current location and remove duplicate simultaneous presence from other tracked locations; off-page locations are never inferred;
- retrieval expands relationship, character-arc and spatial queries back to chapter provenance and boosts those chapters in the local chapter index;
- Continue Story Narrative Focus now includes active character arcs and the latest spatial state, reducing personality resets and accidental teleportation during autonomous writing;
- continuity pre-check/final-check explicitly inspect relationship history, earned character development, scene presence and travel feasibility;
- the Memory inspector exposes counts plus relationship history, recent scene state and Character Arcs without adding another popup/window;
- new state structures are fail-safe auxiliary structured memory; chapter Markdown remains the recoverable narrative source.

The existing Story self-test remains one of the 22 baseline cases and now verifies schema-v5 persistence plus relationship/character-arc/location/scene provenance.

v2.29.2 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.



## v2.29.3 — Deterministic Knowledge Graph

Implemented after v2.29.2:

- derived in-memory Story Knowledge Graph connecting characters, abilities, items, knowledge, relationships, locations, significant events, story arcs, character arcs and open threads;
- graph expansion feeds long-story retrieval and provenance chapter boosts without becoming canon itself;
- deterministic structured-state continuity checks classify Info / Risk / Conflict before Story Memory merge;
- hard structured conflicts (for example retired canon being reintroduced, impossible same-time duplicate presence, a dead character appearing without an explicit revival event, invalid self-relationships) suppress unsafe bookkeeping deltas while preserving eligible chapter prose/manuscript metadata;
- current Memory integrity is also summarized into AI context when deterministic risks/conflicts exist;
- deterministic layer is additive: AI continuity checks remain responsible for semantic/narrative contradictions that cannot be proven from structured state alone;
- no new external dependency and Story Memory schema remains v5 because the graph is derived/cache-like rather than durable truth.

v2.29.3 still requires Windows BUILD/runtime verification before claiming compile-clean or regression-clean.


## v2.29.3.1 — Nullability compile patch

Windows compile-check on v2.29.3 exposed 10 CS8600 warnings-as-errors in the new deterministic continuity, chapter index, and knowledge graph code. This patch keeps the v2.29.3 behavior unchanged and makes all affected `TryGetValue` reference outputs explicitly nullable with real null guards. No browser, Discord, notification, AdBlock, memory schema, or Story behavior changes are intended. Windows BUILD remains the final compile verification.


## v2.29.3.2 — Knowledge Graph retrieval priority fix

Quick self-test on v2.29.3.1 produced 21 PASS / 1 FAIL / 0 WARN. The failing case was the Story Memory long-story retrieval assertion introduced with the v2.29.3 knowledge graph integration. This patch prioritizes direct edges attached to original query seed entities before one-hop expansion edges, so high-value relations such as `Character -> HAS_ABILITY -> Ability` cannot be displaced by the bounded graph context. The self-test now reports the exact missing retrieval signal instead of one combined generic failure. Memory schema remains v5; no browser, Discord, notification, AdBlock, or resource-management behavior is changed.


## v2.29.3.3 — Retrieval focus provenance fix

- Retrieval Focus now surfaces top provenance-backed chapter boosts explicitly (for example `Chapter 1`) instead of relying on incidental reason strings.
- This keeps the retrieval-debug context aligned with the actual chapter ranking used by long-story retrieval and fixes the Story Memory self-test regression without weakening assertions.
- No Story Memory schema change.


## v2.29.4 — Causal Memory + Setup/Payoff Intelligence

- Story Memory schema v6 adds a Narrative Promise Ledger for durable reader-facing setups: clues, promises, foreshadowing, mysteries, world-rule setups and Chekhov objects.
- Promise lifecycle: Open → Developing → PaidOff, with terminal Abandoned/Superseded states when the creator/story explicitly changes direction. Terminal promises are never silently reopened by ordinary bookkeeping.
- Promise provenance records introduction, last-touch and payoff chapters without inventing history for older projects.
- Causal Memory stores only explicit durable CAUSES / ENABLES / BLOCKS / MOTIVATES / REVEALS / RESOLVES / ESCALATES relations. Temporal proximity is not treated as causation.
- Continue Story receives bounded setup/payoff attention and recent major causal chains. Staleness is guidance, not a deadline; the writer must not force payoff merely because a setup is old.
- Long-story retrieval expands promise/causal vocabulary in English and Vietnamese, boosts provenance chapters, and exposes promise/causal evidence to the Knowledge Graph.
- Memory Inspector shows active narrative-promise and causal-link counts; Open Threads also exposes the setup/payoff ledger for debugging.
- Existing v5 projects migrate to v6 with empty ledgers; no fake setup/payoff history is backfilled.
- Story self-test remains part of the fixed 22-case baseline and now verifies v6 persistence, provenance, causal/setup retrieval and Narrative Focus.

## v2.29.5-preview2 (2026-10-08) — recoverable checkpoints/rollback
- New StoryCheckpointStore: before-commit Story Memory/conversation snapshots, deduplicated chapter objects by SHA-256.
- Inline chapter rollback and Memory → Story Versions history with a protective snapshot on every restore.
- Restore journal and chapter cache invalidation; verify with Windows Quick/Stress before advancing baseline.
- See `Docs/v2.29.5-preview2-CHECKPOINT-ROLLBACK.md` for limitations and follow-up commit crash journaling.
