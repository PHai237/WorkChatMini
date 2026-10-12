# WorkChat 2.40.2 — Compile-first publishing fix

## Why this was necessary

The previous two patch ZIPs passed XML/static checks but failed **real Windows C# compilation**:

- 2.40.1: `CS0104` in `StoryInlineMarkdown.cs` for ambiguous WPF/Drawing types.
- 2.40.2: `CS0176` in `StoryPanel.ChatChapter.cs` for an unqualified `HorizontalAlignment.Left` in an object initializer.

The immediate `CS0176` fix uses `System.Windows.HorizontalAlignment.Left`.

## New one-click developer workflow

`PUSH.bat` runs the existing static check, asks for confirmation once, and uploads source to a disposable `workchat-preflight/...` branch. The added `preflight-windows.yml` workflow runs on `windows-latest`, restores win-x64 and invokes the **same Release WPF compile** as `BUILD.bat` with warnings treated as errors.

The publisher waits for the preflight to finish:

- **Pass:** fast-forward-pushes the **same tested commit** to `main`, verifies the remote SHA and version, then triggers the existing Windows release workflow.
- **Fail:** prints failed-job diagnostics, does **not** modify `main` or publish a release.
- **Missing workflow / GitHub error:** stops safely rather than publishing untested code.

This runs on GitHub Actions; developers do not need a local .NET SDK. No Story data, secrets, profiles or AppData are uploaded. Temporary branches are deleted when possible.

## What is and is not verified

Static checks: source version, code limits, batch formatting, XML/XAML well-formedness, and several known regressions. Real Windows compile preflight: C#/XAML build under the .NET 10 SDK targeting win-x64 Release with `TreatWarningsAsErrors=true`. The subsequent release workflow still runs publishing, packaging, Windows Quick/Stress and installer validation. **Compile preflight is not a runtime correctness guarantee.**

This source ZIP has been checked structurally in a Linux container; the new preflight workflow must run to prove WPF compilation. Do not claim BUILD PASS before its actual GitHub result.
