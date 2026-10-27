# WorkChat Core 2.59.0 — Reading Hub user test plan

1. **Windows compile gate:** run `PUSH.bat` on this complete source in a fresh directory. Confirm Windows preflight and release Quick/Stress; only install the successful build.
2. Open the Reading Hub with the pixel book icon. Add a MangaDex book and a novel from Hako/DocLN or any other HTTPS site. Ensure web reader uses existing Web tab with no new blank renderer per book.
3. Click **Edit** on a book: change only its title, save. Confirm the original URL, virtual tab, manual chapter note and position remain. Edit again, change URL to another site: confirm progress resets *only for that book*, no duplicated entry, new source label correct.
4. Click **Edit**, then **Cancel**. Confirm neither the book's title nor URL changed. Try saving an already-used URL; the duplicate should be rejected.
5. Star a book (**☆ Favorite**). Verify it sorts first, is starred in the title and virtual tabs, and appears in the **Favorites** filter. Restart WorkChat, check that the favorite survives. Unstar and check it disappears from Favorites (but not All).
6. Open a novel chapter and scroll roughly halfway. Switch to YouTube or Messenger, return to Reading Hub, select Continue/Read, and check the chapter and scroll position. Test both a conventional document-scroll page and, if available, a reader using an independently scrolling panel. This is best-effort; sites with dynamic lazy-loaded images and custom reader widgets may not restore exactly.
7. Add/import more than 40 entries or use a temporary test library and verify that **Show more** reveals additional rows without opening any new website. Test search resets the view to 40 rows.
8. Verify the **Waiting** filter, chapter notes, MangaDex check (manual rate-limited), Split View, Pop out, Settings, service switching and original pixel icons are unchanged.
9. Test small WorkChat window (~720×540) and large/fullscreen window; ensure Edit panel and Favorites filter do not obstruct book actions. If anything fails, save the exact CI log and one screenshot.

The reading library is `%LOCALAPPDATA%\WorkChatMini\Reading\library.json`. A version upgrade must **never** delete it, the user's WebView2 profile or any legacy Story data.
