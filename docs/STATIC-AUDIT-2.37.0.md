# WorkChat Mini 2.37.0 — Static Audit

## Validated locally

- Based on the 2.36.0 full source snapshot.
- Fixed confirmed fall-through from indexed chapter retrieval to unscoped legacy raw `.md` directory scan. The legacy fallback now requires a committed Story Memory allowlist and returns zero chapter hits if the ledger cannot be read.
- Added regression assertions for orphan, empty-ledger and committed-prose cases in the existing `StoryLongRetrievalSelfTest`.
- Added minimal literary continuity-repair guidance to the existing model repair pass and a self-test assertion; no new model call.
- Source structure: **206 C# files, 5 XAML files**, maximum source lines **990** (limit < 1000).
- XML parsed: **5 XAML files and project file**.
- XAML event bindings: **98 unique handlers, 0 unresolved** (static name-level check).
- GitHub release workflow YAML parsed successfully.
- Version `2.37.0` consistent across VERSION.txt, csproj, BUILD.bat, README.
- Windows BAT files preserved with CRLF.
- ZIP integrity verified after packaging.

## Not verified

- A real Windows WPF/.NET compile, GitHub Actions Quick + Stress, in-app binary upgrade, RAM benchmark, and actual generated prose quality. This environment has no .NET SDK and no Windows runner.
- Static checks alone cannot prove absence of runtime races, data corruption or regressions.

## Data compatibility

- No changes to saved Story JSON schemas, Story chapter file storage, credentials, WebView2 profiles, or binary updater package format.
- No automatic Story deletion or migration. Existing release/GitHub pipeline remains unchanged.
