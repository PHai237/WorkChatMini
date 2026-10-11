# WorkChat Mini — AI Story Companion Blueprint

Version: Concept Blueprint v1

## Core vision
A persistent personal AI co-writer inside WorkChat Mini that can support many genres, remember long stories, research real-world facts when needed, plan before writing, run continuity checks, and perform bookkeeping after each chapter.

## Genre system
Use a multi-select genre checklist per project, not a single fixed genre.

Core project genres are persistent:
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

Support temporary scene/arc tags separately. Example: a Xianxia + Rebirth + Romance story can temporarily use Mystery or Horror for one arc without changing its core identity.

Later, optionally add Story DNA sliders:
- Lighthearted ↔ Dark
- Slow Burn ↔ Fast Romance
- Character-driven ↔ Plot-driven
- Grounded ↔ Power Fantasy
- Low Stakes ↔ High Stakes
- Comedy-light ↔ Comedy-heavy

## Story memory
Do not rely on one huge chat context. Store layered memory:

### Full manuscript
Keep every chapter in full.

### Chapter memory
For each chapter:
- short summary
- detailed summary
- characters present
- locations
- important events
- items gained/lost
- reveals
- relationship changes
- progression changes
- unresolved hooks

### Arc memory
- premise
- starting state
- conflicts
- developments
- ending state
- unresolved consequences

### Canon memory
Store facts with status:
- Confirmed
- Proposed
- Contradicted
- Retconned

### Character memory
Track:
- name and aliases
- age
- appearance
- personality
- speaking style
- goals
- fears
- secrets
- knowledge
- relationships
- location
- condition
- abilities
- inventory
- progression
- history
- last appearance

### Relationship memory
Track trust, affection, suspicion, rivalry, dependency, tension, known secrets, and relationship history.

### Timeline
Track chronology, travel time, ages, wars, school terms, breakthroughs, deaths, reveals.

Rebirth stories can have:
- Original Timeline
- Current Timeline

### Open plot threads
Track unresolved mysteries, promises, foreshadowing, threats, and unfinished conflicts.

### World Bible
Track geography, nations, factions, religions, races, magic, cultivation, classes, skills, items, monsters, economy, politics, technology, and world rules.

### Style memory
Track user preferences such as chapter length, dialogue density, amount of description, humor, romance speed, POV, prose style, disliked clichés, and pacing.

## Retrieval strategy
Never send the entire story to the model every time.

For each request:
1. Understand the request.
2. Detect relevant entities/topics.
3. Retrieve only relevant memory.
4. Build a compact context package.
5. Send that package to the model.

Priority:
Story Canon > Project Lore > User Instructions > Project Memory > Research Notes > Model General Knowledge

External research must never override established story canon unless the user explicitly requests a retcon.

## Research Gate
Search the web when accuracy matters for real-world material:
- real locations
- historical periods
- customs and etiquette
- clothing
- food
- architecture
- professions
- law
- technology
- geography
- historical society

Do not search when the fact is fictional story lore or already defined by canon.

Research notes should store:
- source
- key facts
- relevance
- confidence
- date accessed

## Writing pipeline
The AI should follow:

Recall → Research → Plan → Continuity Check → Draft → Editor Pass → Final Check → Bookkeeping

### Recall
Retrieve relevant story memory.

### Research
Search only when useful.

### Plan
Prepare:
- chapter goal
- motivations
- scene beats
- conflict
- emotional movement
- comedy beats
- romance beats
- foreshadowing
- ending hook

### Continuity check
Verify:
- character knowledge
- injuries
- power levels
- inventory
- relationships
- timeline
- location
- world rules
- open plot threads

### Draft
Write the prose.

### Editor pass
Check:
- awkward wording
- repetition
- pacing
- unnatural dialogue
- exposition dumps
- inconsistent POV
- tone
- character voice

### Final continuity check
Compare the draft against canon again.

### Bookkeeping
Extract and store story changes.

## Bookkeeping
After each chapter, extract:

### New canon
Example:
- a character learned a secret
- a cultivation breakthrough happened
- an artifact was destroyed

### Character changes
- injury
- location
- knowledge
- power
- inventory
- emotional state

### Relationship changes
Qualitative states or optional scores.

### New plot threads
Mysteries, promises, threats, foreshadowing, unresolved conflicts.

### Resolved threads
Mark old hooks as resolved.

### Timeline updates
Add major chronological events.

### Approval flow
Important uncertain changes should be:
- Confirm
- Edit
- Reject

The AI should never silently turn uncertain inference into permanent canon.

## Continuity checker
Catch:
- wrong appearance details
- dead characters returning accidentally
- injuries disappearing
- characters knowing secrets they never learned
- wrong cultivation level
- destroyed items reappearing
- impossible travel time
- timeline mismatch
- romance advancing too quickly
- world-rule contradictions

## AI roles
Use one AI system with shared memory, but allow different working modes:
- Companion
- Writer
- Editor
- Worldmaster
- Character Mode

## Story simulation
Later, allow “what if?” simulation using factions, relationships, timeline, motivations, power systems, politics, and open threads. The user can choose a branch and convert it into canon or an arc.

## WorkChat UI
Keep it minimal.

Main AI view:
- Project selector
- Core genre tags
- Current AI role/profile
- Chat/chapter output
- Input
- Send
- Stop

Optional Story drawer:
- Characters
- Canon
- Timeline
- Lore
- Open Threads
- Research

No giant permanent sidebar.

Genre picker:
- multi-select core genres
- optional temporary tags for the current scene/chapter/arc

## Technical architecture
AI UI
→ AI Orchestrator
  - Memory Retriever
  - Research Gate
  - Planner
  - Continuity Checker
  - Writer
  - Editor
  - Bookkeeper
→ Story Storage
→ Provider Interface
  - Gemini
  - OpenAI
  - Claude
  - Local/Ollama later

Story memory belongs to WorkChat, not to the provider.

## Storage
Recommended first version:
- SQLite for structured story data
- SQLite FTS for retrieval
- Markdown/plain text for chapter files
- JSON only for small settings

Possible structure:

_Internal/
  AI/
    Projects/
      ProjectId/
        project.db
        chapters/
        exports/
        research/

Avoid a heavy vector database until SQLite/FTS proves insufficient.

## Provider security
API keys:
- never hard-code
- never commit to source
- never store as plain text
- use Windows secure storage / DPAPI / Credential Manager

## Performance rules
When AI is idle:
- no active model request
- no polling
- near-zero CPU
- no unnecessary WebView
- no constant indexing
- no background web search

Large tasks run off the WPF UI thread.
Keep source files under 1000 lines.

## Development roadmap

### v2.25 — AI Foundation
- AI module/icon
- native chat UI
- project creation/selector
- provider abstraction
- secure API-key storage
- streaming responses
- cancel generation
- persistent conversations
- basic genre checklist
- model/provider settings
- context budgeting

### v2.26 — Story Memory
- chapter storage
- chapter summaries
- character memory
- canon
- relationships
- timeline
- open threads
- World Bible
- SQLite/FTS retrieval
- bookkeeping extraction
- approval flow

### v2.28 — Research + Writing Pipeline
- Research Gate
- web research integration
- planning step
- continuity pre-check
- editor pass
- final continuity check
- research notes

### v2.29 — Advanced Story Intelligence
- genre-specific memory schemas
- rebirth dual timeline
- cultivation progression
- MMORPG class/level/inventory
- romcom relationship tracking
- Story Simulation
- plot-hole detector
- Story DNA controls

## MVP success criteria
1. A story can exceed 20+ chapters without losing key canon.
2. Early-chapter facts can be retrieved later when relevant.
3. Character knowledge is tracked separately.
4. Web information cannot silently override canon.
5. Research happens only when useful.
6. Bookkeeping updates after each chapter.
7. Provider can change without losing story memory.
8. Story projects remain isolated.
9. AI is almost idle when unused.
10. Existing WorkChat performance remains unaffected.

## Product identity
Working name: **WorkChat Story Companion**

> A persistent personal co-writer that remembers the worlds you create.

It should feel less like asking a chatbot to write something and more like having a long-term author, editor, researcher, and archivist who already knows the series.
