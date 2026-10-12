# WorkChat Mini 2.40.0 — Story Readiness Gate

## Reliability fixes

- Chapter-summary retrieval now checks `StoryCommittedEvidence.UniqueChapters` before treating a chapter's title and summary as search evidence. A duplicate chapter number is ambiguous and can no longer enter this path.
- A final provenance guard rejects all retrieval boosts that do not resolve to a unique committed chapter, including stale references from threads, arcs, canon or explicit chapter requests.
- Proposed, contradicted and retconned Canon facts no longer enter the retrieval Knowledge Graph, expand evidence-search terms, or boost chapters. Confirmed facts still do. Proposed ideas may remain visible as **labeled, uncertain** data in other brainstorming contexts; they do not get promoted into confirmed chapter evidence.
- Continue Story's three recent chapter hints now use the same committed-chapter set with O(n) membership instead of scanning the chapter list for each candidate.

## Readiness coverage

- Added a deterministic StoryReadinessSelfTest inside the **existing** Story memory Quick/Stress case: 20-chapter fixture, duplicate chapter numbers, confirmed vs proposed canon, orphan thread provenance, 24K-character context budget, rejected draft exclusion and Chat/Review safety.
- Tests do **not** call Gemini; writing quality and end-to-end generation must still be validated using real chapters.
- No extra top-level self-test was added; Run-WorkChatSelfTest.ps1 still expects 22 PASS for Quick and Stress.
- No schema changes, extra API calls, UI widgets, updates workflow changes, or user data migrations.

## Readiness policy

See `docs/STORY-READY.md`. Do not describe this version as production-ready until a Windows GitHub build and actual story-writing pilot succeed.
