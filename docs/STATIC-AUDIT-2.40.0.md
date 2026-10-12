# WorkChat 2.40.0 — Static Audit

## Source checks performed here

- C# source files: **211**; XAML files: **5**. Longest file: **990 lines** (`_Internal/Source/MainWindow.SelfTest.cs`). All source files remain below 1,000 lines.
- XAML event handler references: **165**, representing **98** names; every referenced method name was found in the C# source. This is a source-name check, not a WPF runtime test.
- XML/XAML and csproj: parsing successful. GitHub workflow YAML: parsing successful.
- Windows BAT files: CRLF confirmed; no UTF-8 BOM. `VERSION.txt`, BUILD and csproj agree on **2.40.0**.
- Test wiring confirmed: `MainWindow.SelfTest` → `StoryLongRetrievalSelfTest` → `StoryNarrativeQualitySelfTest` → `StoryReadinessSelfTest`.
- Same **22 top-level Quick and Stress tests** remain; new assertions execute inside the existing Story memory test.

## Code findings fixed

1. Chapter-summary retrieval could expand from a duplicate chapter number; now requires unique committed chapter entries.
2. Knowledge Graph and retrieval expansion could treat `Proposed` Canon as search evidence; these evidence pathways now require `Confirmed`. Brainstorming context still labels uncertain ideas rather than silently promoting them.
3. Multiple ranking pathways could leave stale/orphan chapter boosts; a central eligibility filter now removes non-unique or noncommitted chapter identifiers.
4. Auto Continue recent-chapter selection now reuses a set of eligible chapters instead of rescanning the list for each chapter.

## What has **not** passed yet

- Windows .NET/WPF compilation, Quick/Stress runtime reports and GitHub packaging: **not run in this environment**.
- Live Gemini calls, generation/cancellation/recovery, and whether the prose reads well across five chapters: **not tested**.
- CPU/RAM figures and UI smoothness on the user's laptop: **not measured**.

**Release gate:** run GitHub Actions first. If it fails, preserve the last successful release. If it passes, pilot a separate Story with `Review before commit` using `docs/STORY-READY.md` before treating AI Story as ready for a main novel.
