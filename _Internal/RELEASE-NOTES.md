# WorkChat Core 2.50.0 — On-demand companion windows

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
