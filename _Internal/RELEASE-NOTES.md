# WorkChat Mini v2.30.0-preview22

## AI Story — Scene Compass

- Added a per-branch, read-only Scene Compass accessible from the sidebar and story composer chip.
- Displays up to 2 recent unambiguously committed chapter summaries, up to 3 eligible open threads, up to 2 active promises, and up to 3 character goals from current Memory.
- Strict local-only feature: no provider call, no persisted new story data, no automatic messaging/continuation/canon commit.
- Single-click insert of an editable Continue Story direction, preserving any unsent input.
- New deterministic self-test for empty branches, ambiguous duplicate chapter numbers, unopened/future threads, closed threads and uncommitted promises.
- Existing Story Director, dialogue rules, Memory v6, checkpoint recovery and Writer Pipeline unchanged.

## Distribution and updates

- Fixed order-dependent update ZIP validation in `_Internal/Updater/Update-This-PC.ps1` and Windows GitHub release workflow.
- GitHub release gate verifies the archived EXE's uncompressed size and SHA-256, not just the on-disk executable.
- Bumped source/publish/manifest version to 2.30.0-preview22; original in-place updater expects a NEWER version.
- Unchanged installed data locations; app data, Story and WebView2 profiles are not part of the source ZIP.

## Verification

- Source/static lint, XAML/XML and YAML parsing, event handler resolution, ZIP integrity: checked locally.
- Windows WPF build, Windows Quick/Stress, GitHub release and installation from Setup EXE: **NOT checked** in this environment.
- No binary release asset is provided until a real Windows build is executed via connected GitHub Actions/release infrastructure.
