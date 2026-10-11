# WorkChat Mini v2.30.0-preview20 — Publish Once, Update Inside App

Base: full source `v2.30.0-preview19` with Story Director, Narrative Quality 2.0, native Story UI, backup/recovery and app updater. No behavior changes to Story writing, browser/Discord, WebView2 or memory schema.

## Added

- A **real GitHub release pipeline**: tag push or Actions > Run workflow executes Windows source-check, Release BUILD, binary ZIP validations, Quick and Stress self-test gate, and publishes GitHub Release assets.
- A **GitHub Pages HTTPS stable update feed** at `<owner>.github.io/<repo>/latest.json`, generated only after the release exists. Unlike preview19's release-specific `latest.json` asset, this address remains constant across versions.
- An **embedded feed URL** from GitHub Actions using `AssemblyMetadataAttribute`. In a GitHub-built WorkChat installation, More > Updates pre-fills the feed and checks it when the window opens. Offline/local builds keep the optional feed blank.
- A **self-contained first-install ZIP** (`WorkChatMini-install-v<version>.zip`) containing published EXE, INSTALL-APP.bat and the update/restore helpers. End users need not download source or run BUILD.bat. `WorkChatMini-update-v<version>.zip` remains for in-app updates.
- ZIP structural/manifest/SHA-256 validation in GitHub Actions before release publication; release checksum file and a minimal GitHub Pages install/download page.
- Source `.gitignore`, detailed setup and acceptance steps in `docs/UPDATE-FEED.md`.
- App update selftest assertion verifying embedded default feed (when present) is HTTPS.

## Limitations / honest validation

- No GitHub repository, Pages site, code signing certificate or Windows runtime is provisioned by this ZIP. User/repo owner must complete one-time setup; until then no online source exists.
- Windows BUILD, Quick/Stress and real update/restore have NOT been run in this Linux container. Existing preview14.1 remains last Windows-selftest-validated baseline in the conversation.
- GitHub Actions WPF selftest may require an interactive Windows runner; publication stays blocked if tests fail. Do not bypass the gate without an equivalent test environment.
- The current updater replaces the EXE only; updating updater helper logic still needs backward compatibility or first-install helper migration. SHA-256 is integrity, not publisher identity authentication; public distribution needs signing.

## Packaging contract

- Install ZIP: EXE, installer batch, restore batch, PowerShell helper files, first-install instructions.
- Update ZIP: exactly `WorkChatMini.exe` and `update.json` (unchanged format), for compatibility with preview18+ updater.
- Feed: `version`, `packageUrl`, `packageSha256` (SHA-256 of the **whole update ZIP**, not the EXE).
