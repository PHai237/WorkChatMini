# WorkChat Mini 2.34.0 — Reader & Literary Polish

## Long Story performance

- Writing conversation context now scans backward only until the 20 eligible messages / 32k-character bound is reached. It no longer builds a skip-index set for every message in a long conversation; no change to persisted history or confirmed canon.
- Skip logic explicitly excludes rejected narrative drafts, their originating creator direction, and interrupted replies, while preserving approved manuscript and ordinary Chat questions.
- Large message bubbles no longer assign full 70k+ character text to WPF before the bounded 6k-character reader is attached. Full saved text and Copy are unchanged.
- The Story chat fonts use shared FontFamily instances, avoiding per-message font allocations.

## AI Story quality

- Existing Writer/Editor prompts now flag decorative fate/foreboding endings, generic weather openings, unnaturally frictionless dialogue, and translated-English phrasing in Vietnamese.
- Scene agency, POV, canon, genre and the creator's requested writing style still have priority. No extra provider calls.

## UI

- Closed-book tile now appears without a second nested decorative background in Story welcome, creation, and assistant avatars.
- No additional tabs, settings, or root-level files.

## Release safety

- Aligned VERSION.txt, csproj assembly/file/informational version and BUILD.bat package manifest version to 2.34.0. Earlier 2.33.0 source still contained 2.32.0 in BUILD.bat/csproj; this is now corrected.
- Updated deterministic long-session self-test to cover filtering with thousands of rejected drafts and interrupted Chat output.
- No changes to updater, GitHub Actions, Story data, checkpoints, backup or user profile paths.

## Verification

- Linux source/XML/YAML/static checks are not equivalent to Windows WPF build. GitHub Actions must run BUILD, Quick and Stress before release.
