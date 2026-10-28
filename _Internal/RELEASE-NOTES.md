# WorkChat Core 2.60.0 — Quiet Workbench redesign

- Impeccable-informed WPF visual hierarchy, typography, color and layout polish. No Impeccable CLI/runtime dependency.
- Dark neutral + muted mint design tokens; refreshed rail, button focus/hover, Web tabs, URL field and Settings flyout styling.
- Replace new-browser-tab generic graphic with functional quiet start page and quick links (no new WebView2).
- Reorganize Reading Hub with full-page vertical scroll for small windows, source strip, prominent resume card, search and filters; restyle book rows, preserve exact named controls and handlers.
- Retain all 2.59.1 safety fixes, app icons, update and PUSH pipeline, browser profiles and data.
- Source-level validation only in this environment; Windows WPF preflight, Quick/Stress and hands-on UI QA remain required.

---

# WorkChat Core 2.59.1 — Reading and browser history safety fixes

- Migrate missing reading `LastReadUrl` safely and preserve a pre-migration file backup; corrupted or partially invalid shelf files are never silently rewritten.
- Validate reading URLs per book rather than per host; refuse definitely conflicting old resume links while preserving raw stored data.
- Order Continue Reading by the most recent real read timestamp; distinguish edits and favorites from reading.
- Retry scroll restoration once only when the page had no scrollable content.
- Quarantine malformed browser history before accepting new writes; use read-only fallback if file recovery fails.
- Expand self-tests and source-check; preserve existing UI, WebView allocation policy, logins, updater and six pixel icons.
- **Source candidate**. Windows Build, Quick/Stress, and real-world site checks are release requirements.

---

# WorkChat Core 2.59.0 — Reading Hub usability and scroll positions

- Edit existing reading bookmarks (title or source URL) without deleting/recreating their library ID. Title-only edits keep chapter/scroll progress; changing the saved link clears only that book's old progress and MangaDex update baseline. Cancel editing without altering data; duplicate link protection remains.
- Favorite books, sort them first in the shelf and virtual tabs, and filter the library by Favorites. All data remains in `%LOCALAPPDATA%\WorkChatMini\Reading\library.json`; legacy entries default to unstarred.
- Build up to 40 shelf rows per pass, with Show more for the remainder, rather than rendering all 500 bookmarks every time the user filters or searches. This does not limit saved books.
- Best-effort reading position capture/restore also supports some independently scrolling reader elements, not only the document; no page content is extracted or stored. The previous document-scroll behavior remains the fallback.
- Only Reading Hub source, source checks, version/docs were changed. Existing pixel assets, Messenger/Discord/YouTube, Settings, Split, updater and notifications remain unchanged.
- Static verification is not a Windows WPF build. Run preflight, Quick/Stress and manual real-website reading checks before shipping.

---

# WorkChat Core 2.58.0 — Figma Make UI Port (WPF)

- Applied the user-approved Figma Make dark design tokens to the existing native WPF shell.
- Reworked the compact pixel-icon left navigation rail, browser control styling and Settings flyout while keeping production handlers and PNG icon bytes intact.
- Settings root still includes Updates and Exit directly, all subpages still call real actions. Chat alerts remain individually persisted Messenger/Discord/Zalo switches.
- Reading Hub now uses the Figma-inspired library layout: real recent-book continuation, persisted scroll progress, search, All/Reading/Waiting filters and stacked library rows with working original actions.
- Retained WebView2 websites, five live Web tabs, lightweight virtual reading tabs, MangaDex metadata API, existing profiles/notifications/split/companions and local library.
- No new WebView2 renderer, mock messaging screens, third-party scraper, fake update state or external Figma dependency added.
- SOURCE-CHECK and Windows self-test expanded. WPF build, runtime visual QA and Quick/Stress require Windows CI.

---

# WorkChat Core 2.57.0 — Reading Progress & MangaDex Chapter Checks

- Keep the existing virtual Reading Hub: up to 500 library entries but no new WebView2 just for the shelf. Only the first 32 open virtual tab buttons are drawn at once; remaining books stay on the shelf.
- Save the last page's URL and best-effort document scroll percentage locally, at most once every 30 seconds while reading (plus when switching away). Attempt to restore scroll after page navigation. Sites with custom manga viewers, dynamic image loading or custom scroll panes may not restore precisely.
- Use MangaDex's read-only metadata API for manual checks of saved title/chapter URLs. Per-book checks and an 8-series batch button; 20-minute cooldown per book and 550ms spacing for batch requests. First check creates a baseline, later chapter ID changes mark a possible new chapter/translation. Includes All/VI/EN language filter and manual Mark seen. No automatic background polling, manga image downloads or scrapes.
- Pinned Web tabs cannot be repurposed as a reading host. Header actions wrap on narrow windows. Existing pixel icons, Settings, WebView profile, chat alerts, audio and Split are unchanged.
- This is a source build candidate. Windows WPF Compile, Quick/Stress and real MangaDex/DocLN scrolling have not been run in the source-generation environment. Verify on Windows before releasing.

# WorkChat Core 2.56.0 — Reading Hub (local shelf / virtual reading tabs)

- New pixel book rail icon. The original six app icons are unchanged.
- Reading Hub opens a dark lightweight shelf, with MangaDex, Webnovel and Hako/DocLN quick links.
- Save up to 500 books from any public HTTP(S) site with title, URL, source, free-form chapter note,
  Reading/Waiting status and persisted virtual reading tab. Stored locally under WorkChatMini/Reading/library.json.
- Resume from the most recent successfully navigated same-host page; navigating unrelated sites
  does not overwrite a book's last read URL. Virtual tabs reuse one Web browser tab and do not
  create separate background WebView2 renderers. The existing Web max-five-tab policy still applies.
- Removal requires two clicks. File writes are atomic; malformed library files are never overwritten.
- No scraping, chapter downloading, background polling, API calls or automatic new chapter alerts
  in this initial version. No changes to chat notifications, Settings, Split, Web sessions or updater.

# WorkChat Core 2.55.0 — Everyday Web & Performance

- Familiar shortcuts in the existing WorkChat Web tab: Ctrl+1…5, Ctrl+Shift+T (reopen last closed), Ctrl+F, F5/Ctrl+R and Alt+Left/Right. Existing Ctrl+L/T/W/Tab shortcuts are preserved.
- Web tools includes Reopen closed tab. Undo holds at most 5 URL/pin records in memory, no extra WebView2 or permanent background process. It respects the existing 5-tab/2-pin limits.
- Changing page titles (including unread-count titles) no longer counts as a fresh visit or updates history when the title is unchanged. History stays local and does not recreate expired/blocked visits.
- Quick/Stress self-tests cover undo and a temporary on-disk history fixture; they do not access actual personal browsing history.
- WorkChat still relies on WebView2. Some browser-extension, enterprise auth, DRM and site compatibility workflows may still require a full browser. No user data migration, account key change or forced tab wake was introduced.
- Windows WPF compile and live UI check remain release gates. Do not publish solely on static checks.

---

# WorkChat Core 2.53.0 — Essential Actions Hotfix

- Fixed nonfunctional submenu parents. The old custom leaf MenuItem template omitted PART_Popup.
- Enabled WebView2 notifications only for Messenger, Discord and Zalo HTTPS service origins (opt-out via Settings > Chat alerts).
- Restored formerly denied WebView2 profile permissions; do not intercept permitted desktop notifications.
- Keep chat renderers awake and start them after Messenger startup for background/tray alerts.
- Trade-off: greater RAM use when alerts are enabled; no alerts after Exit.

---

# WorkChat Core 2.51.0 — Original Pixel Icons & Minimal Shell

- Restore original custom pixel-art icon assets and crisp scaling.
- Use only the vertical app rail; retire duplicate top toolbar/title/loose controls.
- Pin Split above the Settings gear at the bottom left.
- Put background audio and always-on-top solely inside settings; group options into concise menus.
- Preserve Split View, companion windows, session data and updater unchanged.

## Previous: On-demand companion windows

- **Window riêng:** nhấp phải icon Messenger/Facebook/YouTube/Discord/Zalo rồi chọn "Open ... in another window". Hoặc More → New companion window.
- **Ctrl+Shift+O:** mở cửa sổ riêng cho ứng dụng hiện hành (nếu là dịch vụ hỗ trợ).
- Cửa sổ phụ chia sẻ profile đăng nhập hiện tại nhưng tạo WebView2 mới khi được yêu cầu; không di chuyển WebView đang phát nhạc/đang gọi.
- Đóng cửa sổ phụ để giải phóng WebView; tối đa 2 cửa sổ phụ tránh tăng RAM vô hạn.
- Bảo vệ cuộc gọi Discord: không tạo cửa sổ Discord thứ hai khi WorkChat đã phát hiện một cuộc gọi đang diễn ra.
- Ghi thời gian tạo cửa sổ và snapshot tài nguyên vào log, không ghi nội dung chat/URL nhạy cảm.
- Thêm bài Quick Self-Test xác minh launch policy mà không mở browser thật.

> Lưu ý: Đây là giải pháp companion (WebView2 thứ hai dùng chung profile), **không phải reparent** tab đang phát video. Khi mở cửa sổ phụ có thể tăng RAM và trang trong cửa sổ phụ bắt đầu từ URL dịch vụ, không kế thừa chính xác vị trí/phát lại đang mở.

---

### 2.47.0 — Responsive UI audit
- Limit diagnostic, VPN and health panels on short windows; retain scrolling and close controls.
- Wrap panel action rows instead of letting controls overflow narrow windows.
- Bound Updates and Clear browsing data dialogs to their owner window while scrolling content.
- Prevent fixed-width browser history suggestions and new-tab content from spilling outside narrow panes.
- Add Quick Self-Test for window size / DPI boundary logic; no additional WebView2 instances.

# WorkChat Mini 2.47.0 — Split View Core

- Side-by-side YouTube + Messenger/Discord/Zalo/Facebook using existing WebView2 controls and one shared login profile.
- Split toolbar button and Ctrl+Shift+2; right-click Split to choose the second app. Click again to close.
- Adjustable divider, automatic single-pane fallback below 760px; no new browser process created just by enabling layout.
- Resource Saver, YouTube audio, and Discord call guard honor the second visible app.
- Quick Self-Test now validates the split layout without external network calls.
- Detachable windows deferred to a later release so that active playback and calls are not affected by WebView2 HWND reparenting risks.

## Previous updates

### 2.45.0 — UI & Performance Core

- Desktop app switcher moved to a left vertical rail, keeping browser/service content separate from the toolbar.
- Compact fallback at widths below 520px uses the existing Apps selector; short windows can scroll the rail.
- Fixed active Messenger highlight being overwritten by the retired Story enum fallback.
- Reduced unnecessary WebView2 zoom requests on height-only window resize.
- Suspended speculative hover wake when system memory pressure is elevated/critical; click navigation and Discord/YouTube keep-alive paths stay unchanged.
- Added a Windows UI self-test for the active app and responsive rail layout.

No changes to profile folders, credentials, browser cookies, user stories, or update packaging. Build/test on Windows required before release.

---

# WorkChat Mini 2.40.2 — AI Story bugfix (source candidate)

- Edit & Regenerate the latest uncommitted creator message, keeping a local pre-edit checkpoint and preserving the previous AI reply as view-only history (excluded from future prompts).
- An explicit two-step Save reply as Chapter command can convert a long, latest Chat-written chapter into a committed manuscript and run eligible character/location/event extraction. No automatic canonization of brainstorming.
- Discuss, Scene Compass and Continue Story navigation remain accessible while the current AI request runs; changes that write canon and the second Send are still blocked.
- Added regression checks for edited-prompt retry and JSON persistence of superseded response flags.
- Gemini primary 60-second timeout / connection failure now triggers fallback to the configured secondary model, while preserving the retry limits and safeguards against retrying after emitted text.
- Retry unanswered/interrupted last prompt without duplicating the creator message; completed and committed output is protected.
- Render common Markdown in bounded WPF text segments while keeping stored content unchanged.
- Toggle Find with Alt+F; leave Inspector Close available while a generation runs; record Inspector opening duration for performance diagnosis.
- No migration or personal Story data touched. **Requires a successful Windows Release build and Quick/Stress tests before publication.**

---

# WorkChat Mini 2.40.0 — Story Readiness Gate

## Reliability fixes

- Chapter-summary retrieval now checks `StoryCommittedEvidence.UniqueChapters` before treating a chapter's title and summary as search evidence. A duplicate chapter number is ambiguous and can no longer enter this path.
- A final provenance guard rejects all retrieval boosts that do not resolve to a unique committed chapter, including stale references from threads, arcs, canon or explicit chapter requests.
- Proposed, contradicted and retconned Canon facts no longer enter the retrieval Knowledge Graph, expand evidence-search terms, or boost chapters. Confirmed facts still do. Proposed ideas may remain visible as **labeled, uncertain** data in other brainstorming contexts; they do not get promoted into confirmed chapter evidence.
- Continue Story's three recent chapter hints now use the same committed-chapter set with O(n) membership instead of scanning the chapter list for each candidate.

## Readiness coverage

- Added a deterministic StoryReadinessSelfTest inside the **existing** Story memory Quick/Stress case: 20-chapter fixture, duplicate chapter numbers, confirmed vs proposed canon, orphan thread provenance, 24K-character context budget, rejected draft exclusion and Chat/Review safety.
- Tests do **not** call Gemini; writing quality and end-to-end generation must still be validated using real chapters.
- No extra top-level self-test was added; WorkChat Core uses 21 self-test scenarios; the test runner now checks actual case counts, at least 21 PASS, zero FAIL/WARN, and prints the full report on failure.
- No schema changes, extra API calls, UI widgets, updates workflow changes, or user data migrations.

## Readiness policy

See `docs/STORY-READY.md`. Do not describe this version as production-ready until a Windows GitHub build and actual story-writing pilot succeed.

## 2.53.0 critical navigation hotfix

- Updates is a first-level Settings command; no Tools submenu dependency.
- Exit WorkChat is a first-level Settings command (the tray Exit remains available).
- Ctrl+Shift+U opens Updates when the WorkChat window has keyboard focus.
- New regression guards check that Updates and Exit cannot be buried in submenus.
- Original pixel icons, Split View, companion windows, and chat alerts are unchanged.

## 2.53.0 Settings UI usability and independent chat alerts

- Gear menu now bottom-aligns beside the rail button; uniform-height compact rows.
- Chat alerts opens a separate persistent multi-select popup; each of Messenger, Discord and Zalo has an independently saved check.
- Existing 2.52 opt-out migrates to three disabled choices; alerts retain their original safe HTTPS origin restrictions.
- Updates and Exit are direct Settings actions, never buried in Tools; Ctrl+Shift+U is an extra recovery shortcut.
- No changes to the original six pixel PNG assets or installed WebView2 user data.
