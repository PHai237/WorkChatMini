# WorkChat Core 2.44.0 — Source synchronization repair

## Confirmed root cause

The 2.44.0 ZIP no longer contains `_Internal/Source/MainWindow.Story.cs` or the `_Internal/Source/AI/` module. However, `Ship-GitHub.ps1` previously copied new files over a clone without removing deleted ones. Because WPF SDK project compilation includes `*.cs` by default, GitHub still compiled old `MainWindow.Story.cs`, which called the now-removed StoryPanel instance API. It failed with CS0120.

## Changes

1. `Ship-GitHub.ps1` now treats the shipped `_Internal/Source` as authoritative. In its **temporary repository clone only**, it removes tracked `_Internal/Source/` files no longer in the 2.44.0 source ZIP. It then stages deletions with `git add -A` and verifies the staged source inventory exactly matches the package before initiating preflight.
2. `SOURCE-CHECK.ps1` now refuses a source folder with leftover `_Internal/Source/AI/` or `MainWindow.Story.cs`, preventing accidentally republishing files left after unzipping over an old folder.
3. Version stays `2.44.0`. No changes to AppData, installed application, Story backups, or user settings.

## Validation performed here

- ZIP content and extraction checked.
- Presence of `MainWindow.Story.cs` in the GitHub `main` repository and absence in the shipped Core ZIP confirmed.
- Changed PowerShell code manually reviewed for Windows PowerShell 5.1 syntax and staged inventory safety; **not executed in Windows PowerShell here**.
- Checks for XAML/XML/YAML and source inventory performed locally.
- Local Git simulation PASS: two retired tracked Story source files removed from the staging inventory while an unrelated tracked file was retained; this tests the expected Git staging behavior, not PowerShell execution.
- **Windows WPF compile, Quick and Stress tests have not yet been run for this patched ZIP.** GitHub Preflight must complete before release.

## Usage

Extract the new WorkChat.zip into a **fresh empty directory** (not on top of an older unpacked source), then use `PUSH.bat`. It will show a count of obsolete tracked source files removed from the temporary checkout, followed by `CORE SOURCE SYNC VERIFIED` before preflight. This does **not** remove `%LOCALAPPDATA%\WorkChatMini\AI`.

If preflight still fails, report the exact first `error CS...` line from the new run. Do not install until both build/release and update feed succeed.
