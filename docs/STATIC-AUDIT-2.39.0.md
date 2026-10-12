# WorkChat Mini 2.39.0 — Static Audit

Audit basis: unpacked local `WorkChat.zip` v2.38.0. Local source was patched to v2.39.0. This audit is **static inspection only**; no Windows build, no WPF render, no Gemini network, and no Quick/Stress execution was available in this Linux environment.

## Source-level defects addressed

- `StoryKnowledgeGraph`: unverified `Proposed`/orphan events previously entered event nodes, links, expansion terms and chapter boosts. Now uses unique committed chapter provenance, unique ID and Confirmed status; shared character nicknames cannot establish a subject edge.
- `StoryEventContextMatcher`: matching a confirmed event by chapter number alone could accept orphan or duplicate chapter numbers, or a duplicate event ID. Now requires the same verified evidence predicate.
- `StoryRetrievalPlanner`: generic event expansion, actor-based event boosts and auto-continue event hints could still promote invalid records after targeted event retrieval had been fixed. All are filtered. Spatial scene and relationship history are bound to uniquely committed chapters; current POV/position is sorted by chapter, not merely update timestamp.
- `StoryCausalEvidenceTrace`: causal endpoints are eligible only if their events have verified committed provenance. Invalid chapter-number link citations are rejected.
- `StoryNarrativeFocus`: latest unique committed spatial/POV state is placed ahead of nonessential creative hints to reduce truncation in long sessions.
- `StoryMemoryRetriever`: historical previous-chapter summaries and event context respect unique committed chapter references rather than all chapter numbers; a retrieval plan also restricts the generic event ledger to verified events.

## Regression tests authored (not run on Windows)

- `StoryLongContinuityAuditSelfTest` (20 chapters in memory; arcs, threads, current scene, verified event, proposed event, orphan event, shared nickname, duplicate event ID, duplicate chapter number) is called via the existing `StoryNarrativeQualitySelfTest` inside `StoryLongRetrievalSelfTest`.
- Updated old causal- and action-retrieval fixtures with explicit committed chapters. Previously these fixtures supplied only event numbers; the stricter provenance gate would otherwise correctly reject them.
- No new top-level case was added to the existing Quick/Stress test list.

## Verification performed in this environment

| Item | Observed |
| --- | --- |
| File count in source directory | 362 files before packaging |
| C# source | 210 files |
| XAML | 5 files |
| Longest C#/XAML file | 990 lines |
| XAML event hookups | 166 references, 99 unique; 0 missing methods found by static name scan |
| XAML and csproj | XML parsed |
| GitHub Actions YAML | YAML parsed |
| BUILD.bat / VERSION.txt / csproj | 2.39.0 aligned |
| Root BAT files | Windows CRLF retained |
| Windows .NET BUILD | **NOT RUN** |
| Windows Quick/Stress | **NOT RUN** |
| Real 20-chapter Gemini quality test | **NOT RUN** |

## Boundaries and follow-up

- The predicate uses *unique committed entries from Story Memory*. It does not itself verify the chapter file contents or an external checksum at this read path; manuscript storage/recovery performs separate file-integrity checks.
- Current character and relationship state is still drawn from durable Story Memory. Corrupted old data or bad content from AI bookkeeping can require recovery/user correction, not only retrieval filters.
- GitHub Actions must build and run Quick/Stress successfully before installing 2.39.0. A green static audit is **not** proof that the app is stable on Windows.
