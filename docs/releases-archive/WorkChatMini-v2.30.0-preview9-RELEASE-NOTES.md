# WorkChat Mini v2.30.0-preview9 — Portable Story Backup & Integrity Hardening

**Ngày:** 2026-10-10. **Source:** phát triển trực tiếp từ `v2.30.0-preview8`, không quay về code cũ. **Windows validated baseline mới nhất:** `v2.30.0-preview6` (Quick + Stress đều 22 PASS / 0 FAIL / 0 WARN). **preview7, preview8, preview9 chưa Windows-validated.**

## I. Portable Story Backup

Thêm nút **Create verified Story backup (ZIP)** tại **Story → Memory → Versions → Storage Health**.

- Xuất riêng từng Story sang `%LOCALAPPDATA%\WorkChatMini\AI\Backups`, không tạo folder con bên trong Projects, không sửa file sống.
- Sao lưu toàn bộ trạng thái Story trên disk: `project.json`, `conversation.json`, `memory/story-memory.json`, file Markdown chương, dữ liệu branch, checkpoint snapshot và chapter objects. Không lưu app settings, key Gemini/OpenAI, WebView2 cookies hay profile trình duyệt.
- Tạo ZIP ở tên `.partial`, ghi `backup-manifest.json` gồm SHA-256 và byte size của từng file, mở lại ZIP xác minh nội dung rồi so sánh SHA-256 nguồn trên disk; chỉ rename `.zip` sau khi tất cả khớp. Streams buffer nhỏ và chạy worker thread, không đưa toàn bộ chương dài vào RAM một lượt.
- Từ chối backup khi có `pending-commit.json`, `pending-restore.json` hoặc `PendingSwitch` chưa phục hồi; không đi theo symbolic file/directory links; kiểm tra path để chống backup lạc khỏi Story.
- Không có tính năng tự import/restore ZIP trong release này: việc bung đè live data khi chưa có transaction restore sẽ quá rủi ro. Archive được giữ độc lập để sao chép ra nơi an toàn. Không có tác vụ nền hay xóa backup tự động.
- Giới hạn: tự động sao lưu định kỳ chưa được triển khai; file ZIP chưa được mã hóa và có thể chứa nội dung truyện riêng tư. Không đưa ZIP lên nơi công khai.

## II. Live Story Consistency & Non-destructive Diagnostics

- Nâng **Analyze storage (read-only)**: ngoài branch/checkpoint objects, rà tập file `chapters/` đang dùng và các reference từ Story Memory. Báo cáo thiếu file, file trống, file bị bỏ ngoài index, số/tên file trùng, tên chứa path traversal.
- Chỉ liệt kê tối đa 15 lỗi chi tiết và tổng số lỗi; không xóa, phục hồi hay tự phát minh dữ kiện.
- Lệnh rà đọc Story Memory từ disk trên worker, không lấy object UI có thể đang bị chỉnh sửa.

## III. Durability fixes

- `AppendChapterAsync`: từ chối mọi trường hợp số chương/tên chương trùng với Memory hoặc file `.md` đang có trên disk, dùng `FileMode.CreateNew` thay overwrite. Nếu ghi lỗi, chỉ dọn file chính thao tác vừa tạo.
- `StoryCheckpointStore.WriteAtomicAsync` và `StoryBranchStore.SaveAsync`: cleanup file `.tmp` unique trong `finally` khi ghi lỗi, không xóa journal/live manifest đã công bố.
- Khi backup và storage scan, các nút tạo Story, gửi AI, chọn Role/Provider và chuyển Story được vô hiệu theo trạng thái `_storageAuditBusy`; UI trả về bình thường sau khi hoàn thành.

## IV. Self-test/validation

- Source chứa thêm self-test: tạo archive của Story test tách biệt, đọc/verify SHA-256, mô phỏng manifest bị can thiệp, kiểm tra bảo tồn chapter orphan và diagnostics. Giữ 22 test cấp cao nhất, **assertions chưa chạy ở Windows**.
- Chưa thể xác minh .NET compiler / WPF XAML load / real windows filesystem semantics trong môi trường phát triển hiện tại. Không nói preview9 đã BUILD PASS.

## Test khi đến mốc tích hợp (không cần chạy ngay)

`AUTO-TEST.bat` một lượt trên preview cuối cùng; nếu build fail gửi compiler output; nếu build thành công gửi Quick/Stress. Smoke-test bằng Story thử nghiệm: nhấn backup, mở ZIP xem `backup-manifest.json`, chạy Storage Health, tạo/chuyển branch và commit một chương, sau đó backup lại. Không sửa tay dữ liệu Story cá nhân để thử corruption. Test dữ liệu backup là ZIP độc lập, không được coi là đã phục hồi thành công tới live Story.
