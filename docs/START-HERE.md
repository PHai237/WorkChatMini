# WorkChat Mini — bắt đầu từ preview22

**Đang dùng WorkChat trên máy:** giữ nguyên. Không cần giữ source, `_Internal`, Build, ZIP tạm sau khi cài. Không chạy `UPDATE-NOW.bat` nếu bạn chỉ muốn sử dụng ứng dụng.

**Muốn lên preview22 bằng More → Updates:** chỉ có thể dùng file `WorkChatMini-update-v2.30.0-preview22.zip` do Windows release pipeline tạo ra — KHÔNG dùng `WorkChatMini-source-v2.30.0-preview22.zip`.

**Muốn cài một lần từ đầu:** sau khi GitHub workflow phát hành thành công, tải `WorkChatMini-Setup-v2.30.0-preview22.exe` tại GitHub Releases, thoát ứng dụng từ tray rồi chạy Setup. Đây không phải yêu cầu gỡ/xóa data; dùng lại thư mục cài và user profile.

**Tại thời điểm phát hành source:** chưa có GitHub repository/Pages kết nối với phiên làm việc này, và không có trình build WPF Windows trong môi trường tạo ZIP. Không có sẵn file EXE mới để tải. Để từ đó chỉ cập nhật trong app, chủ repository phải cấu hình và chạy bản Windows release đầu tiên theo `docs/UPDATE-FEED.md`. App preview20 trở lên đã có cửa sổ Updates.

**Scene Compass:** mở AI Story → chọn Story có chương đã chốt → Scene Compass → đọc tóm tắt và các tuyến mở → Use as editable Continue Story cue → sửa lời nhắc → gửi khi muốn. Chế độ Chat không tự commit; nếu viết truyện quan trọng, bật Manuscript → Review before commit.

**Thông báo kiểm thử:** Preview22 mới được kiểm tra tĩnh, chưa BUILD hoặc chạy Quick/Stress trên Windows. Đừng xóa hoặc ghi đè bản WorkChat đã cài ổn định khi chưa có release Windows và kết quả test đạt.
