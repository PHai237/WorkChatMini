# WorkChat Mini 2.38.0 — Static Audit

## Scope

Built from the local `2.37.0` source snapshot. Added a branch-local, in-memory relationship evidence handoff for **Continue Story**. Uses unique committed chapter numbers and current cast identity; rejects stale relationship states, ambiguous aliases and uncommitted scenes. It is guidance, not an automatic plot beat or canonical write. Placed *after* spatial/POV focus so relational notes cannot take that core slot. Tightened continuity-repair instructions without creating new AI requests.

## Checks completed

- 208 C# + 5 XAML; longest source file 990 lines (`_Internal/Source/MainWindow.SelfTest.cs`); no file reaches 1,000.
- 6 XML/XAML/project files parsed.
- 99 XAML handler names found in source; 0 missing (static name-level check only).
- GitHub Actions workflow YAML parsed successfully; 2 jobs found.
- Windows BAT CRLF, VERSION.txt/csproj/BUILD.bat all at 2.38.0.
- No installer/updater/GitHub workflow changes. Root remains unchanged; no executable or private data packaged.
- New regression assertions included in the **existing** Story memory/retrieval self-test: committed history, ambiguous nickname, uncommitted chapter, current-state mismatch, duplicate chapter, uncommitted cast scene.

## Not verified

- Windows WPF build, actual execution of Quick/Stress, Github publish, actual Gemini creative quality or performance. .NET SDK / Windows runner is not available in this environment. No claim of runtime PASS.
- Static compilation checks cannot detect every C# type error, race or persistence issue.

## Data compatibility

No Story JSON changes, manuscript migrations, API key handling changes, WebView2 changes or updater schema changes. Existing Story data is untouched by packaging.
