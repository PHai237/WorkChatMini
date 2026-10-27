# WorkChat Core 2.57.0 — One-pass Windows test plan

Use the **full WorkChat.zip**, extract to a fresh source folder and back up your existing `%LOCALAPPDATA%\WorkChatMini\Reading\library.json` first if you already have books. This source package is **not** an installed app.

## Required release gates

1. Run `BUILD.bat` (or the repo `PUSH.bat` once when ready for CI). Do not treat SOURCE-CHECK as a Windows compile.
2. Run Quick and Stress self-tests using the repo scripts; the new self-test has an **offline MangaDex metadata parser** fixture and does not call MangaDex servers.
3. Confirm the Settings gear, original six pixel icons, Split, Messenger, Discord and YouTube still open normally.

## Reading Hub walk-through

1. Open the new book icon. At a narrow window width, make sure **Add book**, **Check chapters** and the Close X wrap instead of overlapping.
2. Add a MangaDex **title** link. Click Check chapter once; it should save a baseline (not flag “new”). Clicking again immediately should display the 20-minute cooldown.
3. Click `Lang: ALL` -> `Lang: VI` -> `Lang: EN`; each language change creates a fresh baseline. Check again when the cooldown resets.
4. Add a MangaDex **chapter** link as a separate book; Check chapter should resolve the manga ID using read-only chapter metadata. No artwork or chapter files should be downloaded by WorkChat's client.
5. Add Hako/DocLN and Webnovel/other HTTPS book links; their shelves and virtual tabs should work but **do not** promise automatic chapter checks yet.
6. Read a chapter with ordinary page scrolling, scroll far down, open Reading Hub, switch books, reopen the first book. The URL and approximate scroll percentage should resume on sites that use document scrolling. Custom manga reader containers may not work.
7. Create several virtual tabs and confirm that only the shared Web tab is reused. Pin the current Web tab; opening another book must never overwrite that pinned tab.
8. Click Check chapters with several tracked MangaDex books: at most eight are checked per click and a cooldown prevents repeated requests. After a genuine later chapter-ID change, a possible-new-chapter label appears and can be cleared with Mark seen.
9. Switch between YouTube, Messenger, Discord and Reading Hub, then restart WorkChat and check the library still exists.

## Failure log to collect

Send the **first Windows Build error** if compilation fails; otherwise send the Quick/Stress summary and a screenshot of Reading Hub at your usual window size. For MangaDex metadata issues, include the kind of link used (`/title/` or `/chapter/`) and HTTP error/status text (never send logins/cookies).

## Scope of this candidate

No automatic background poller; Hako/Webnovel/custom sites remain bookmarks only. Windows compile, desktop notification behavior, MangaDex API availability, exact position restoration, and WebView2 UI rendering have not been verified in the source-generation environment.
