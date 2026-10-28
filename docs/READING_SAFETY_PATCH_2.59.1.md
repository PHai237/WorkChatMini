# WorkChat Mini 2.59.1 — Reading / History integrity patch

## Changed

1. **Legacy Reading Hub preservation:** `ReadingLibraryStore.Load()` recovers a missing or invalid `LastReadUrl` using the book's safe `Url`, rather than discarding the book. Missing IDs are filled. Normalized/migrated source files get a single `library.json.pre-migration.bak` BEFORE the first subsequent save. Do not manually delete this file.
2. **No silent shelf truncation:** malformed JSON, unsafe/missing `Url` entries and libraries with more than 500 entries block writes; the source JSON is left intact. The Reading Hub displays a read-only error until the invalid file is recovered. If valid books are mixed with damaged records, the UI deliberately does not silently omit the latter and save a shortened list.
3. **Per-book navigation:** chapter/page association checks a stable book identity in known URL routes, not merely domain. Hako mirrors (`docln.net`/`docln.sbs`) are supported for matching `/truyen/{id}`; same host but another `/truyen/{id}` is rejected. Unverifiable links (`/chuong/{chapterId}`, MangaDex `/chapter/{uuid}` when the shelf has `/title/{mangaUuid}`) are NOT automatically assigned to a series, because they lack an explicit book ID. Such transitions keep the last safe bookmark unchanged. An explicitly saved chapter still opens and tracks that exact chapter.
4. **Old bookmark guard:** on open, if the stored resume URL demonstrably identifies a different book, use the book's original URL without erasing the stored link. Unknown chapter links are retained as possible valid bookmarks.
5. **Reading timestamps:** `LastReadAt` sorts the Continue Reading card independently from `UpdatedAt` (editing/favoriting/status/note changes). Old JSON without `LastReadAt` remains supported.
6. **Scroll restore:** one additional delayed attempt is allowed only if the first restore found no scrollable element. Never repeatedly jump a page that has already been restored, and no extra WebView/crawler is created. Website-specific scroll restoration is still best-effort.
7. **Browser history file recovery:** if `browser-history.json` is malformed JSON, move it to uniquely named `browser-history.json.corrupt-*.bak` before accepting new history writes. A failed backup move or file read error leaves the old file in place and disables writes for that session. No history file is simply overwritten with an empty one.

## Added tests

- `ReadingSafetySelfTest.VerifyPersistence` (legacy migration, backup retention, timestamp round-trip, damaged/partially invalid JSON)
- `ReadingSafetySelfTest.VerifyUrlIsolation` (MangaDex, DocLN mirrors and cross-series, Webnovel, generic routes and hostile lookalike hosts)
- `BrowserHistoryStore.VerifyCorruptFileProtection` (temporary corrupt JSON backup before resumed writes)
- Continue Reading order test (real last reading time vs later editing)
- Source-check includes 2.59.1 integrity guards; existing 1,000-line C# file threshold preserved

## Windows validation / rollback

1. **Do not overwrite the existing running installation with this source ZIP.** Extract into an empty directory.
2. Back up `%LOCALAPPDATA%\WorkChatMini`, especially `Reading\library.json`, `browser-history.json`, `WebView2Profile`, and `settings.json`.
3. Run `SOURCE-CHECK.bat --nopause`, then `BUILD.bat --nopause`, and `AUTO-TEST.bat --built --nopause`. Check both Quick and Stress test reports inside `_Internal\Reports` and stop if any case fails.
4. Open two different saved books on the same source host, switch between them and verify neither saved URL changes to the other book. Test site-specific chapter links and position restore manually; some non-verifiable chapter URL types intentionally do not auto-track.
5. If anything regresses, close WorkChat and restore the backed up program. The profile and old source file are not supposed to be deleted by this patch. Do not release/push until the Windows gates pass.

**Validation status in the source-authoring environment:** Windows WPF compilation, self-test execution and real browser site checks could not be performed here. A source ZIP does not contain a built `.exe`. Windows checks are mandatory before treating 2.59.1 as a stable installation release.
