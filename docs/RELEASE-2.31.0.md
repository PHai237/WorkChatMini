# WorkChat Mini 2.31.0 — AI Story & streamlined releases

- Added per-story **Chapter Craft Feedback** for future Continue Story and Story Director.
- Preserved existing Story Memory and canon approval boundary; no new API stage or auto rewrite.
- Reduced repository root to CI/developer entry points; installation wrappers are now under `_Internal/Installer` but remain rooted appropriately inside the legacy install ZIP.
- Shortened download filenames: `WorkChat-Setup.exe`, `WorkChat-Update.zip`, `WorkChat-Install.zip`.
- Applied earlier GitHub workflow hotfixes to source baseline: deterministic installer ZIP entry paths, independent artifact logs, correct first-release check.
- Switched to stable version 2.31.0 for proper ordering after 2.30.0-preview23.
- **Validation needed:** Windows BUILD and Quick/Stress via GitHub Actions; no EXE compiled in the Linux authoring environment.
