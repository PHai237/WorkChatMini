# WorkChat Mini v2.30.0-preview10 — Durable Recovery + Verified Backup Import

**Built from:** v2.30.0-preview9 full source (no downgrade). **Windows test status:** Preview6 was the latest confirmed Quick/Stress 22/0/0 baseline; previews7–10 have not yet been compiled/tested on the user's Windows system. Static auditing is not a compiler.

## 1. Import backups as a new, isolated Story

- In **Story → Memory → Versions → Storage Health**, use **Restore backup as a new Story…** and select a ZIP previously created with **Create verified Story backup (ZIP)**.
- Validate manifest, CRC/ZIP contents via per-entry SHA-256, byte lengths, entry paths (including Windows device names), project identity, conversation and memory JSON before publishing.
- Extract to a disposable staging directory under AppData, reject unfinished commit/restore/branch journals and unsafe paths, then publish by moving the completed directory into a **new random project ID**. Source ZIP and existing Stories remain unchanged.
- Original branch/checkpoint IDs remain local to this newly copied Story. No cross-story merging or automatic canon modification.
- Bounded stream-based extraction (512 MiB per file, 8 GiB total, 100,000 entries maximum), no API keys/browser profile included.
- Backups are unencrypted and the SHA-256 manifest checks *integrity*, not authenticity. Keep private backups private; do not accept untrusted ZIPs.

## 2. Rescue manuscript files excluded from checkpoints

- Prior behavior removed the previous chapter directory after restore, including files not referenced by Memory. Such untracked files, or original chapters externally modified after checkpoint capture, were not necessarily protected by snapshot objects.
- After a committed restore journal is completed, compare prior live chapter files with the verified safety checkpoint. Files absent from that snapshot or no longer matching its SHA-256 are moved into `branches/recovered/<transaction-id>/` before the old directory is discarded.
- A junction/reparse-linked directory, IO failure, or invalid snapshot means the old archive is kept for manual recovery. The rescue directory is outside active chapters and does not become canon.
- Storage Health reports number/size of protected recovery groups; they are not treated as temporary garbage or automatically deleted.

## 3. Self-test coverage expanded (not yet Windows-executed)

- Backup exported from a disposable Story can be imported as a new project with a different ID and matching conversation/chapter counts; imported fixture is then removed.
- Interrupted commit recovery preserves an untracked chapter note in the rescue folder while restoring the pre-commit canonical state.
- Existing malformed backup checksum rejection and chapter overwrite tests retained. Top-level self-tests stay at 22.

## 4. Unchanged and limitations

- Core browser/notification/Discord/media/AdBlock behavior unchanged; AI provider and call counts unchanged; Memory schema remains v6.
- Import uses the system file picker; WPF layout and interaction still need visual testing on Windows.
- Import does not automatically diagnose every semantic inconsistency inside a previously corrupted checkpoint. Run the read-only **Verify checkpoint integrity** after importing critical backups.
- .NET SDK/WPF compiler unavailable in the current build environment. Windows `AUTO-TEST.bat` has not yet run for preview10; test on the next scheduled consolidated milestone, not after every preview.
