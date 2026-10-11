# WorkChat Mini 2.30.0-preview23 — Release Bridge

- Giữ nguyên tính năng từ preview22, bao gồm AI Story Scene Compass, Story Director, Memory v6, Review before commit.
- Đóng gói updater script trong EXE qua EmbeddedResource. EXE tự đồng bộ helper cho các update kế tiếp (ZIP vẫn gồm đúng `WorkChatMini.exe`, `update.json`).
- Self-test kiểm tra embedded updater hiện diện.
- CI không overwrite Release hiện hữu; ngăn tái xuất bản cùng version với binary khác.
- `SOURCE-CHECK.bat --nopause` chạy trong GitHub Actions không chờ Enter.
- Bổ sung script xuất bản repo một lần với xác nhận `PUBLISH PUBLIC` rõ ràng, kiểm tra các file lạ/sensitive trước khi push.
- Bổ sung `FIRST-AND-LAST-LOCAL-BUILD.bat` tùy chọn (SourceCheck → Build → Quick/Stress → cài), nếu chưa dùng GitHub.
- Script cài local backup EXE đã cài bằng File.Replace, giữ `%LOCALAPPDATA%\WorkChatMini`.
- Helper dọn file EXE staging generated sau update; không động dữ liệu người dùng.

**Chưa chạy được Windows compiler/Quick/Stress/GitHub Release từ môi trường này.** Không dùng ZIP source làm gói update. Bản build chính thức chỉ sinh ra khi GitHub Action hoặc Windows local build PASS.

- Giữ tối đa hai bản EXE archive cũ, cộng một `WorkChatMini.previous.exe` để rollback ngay trước đó.
- Sửa đường dẫn feed GitHub Pages bảo toàn chữ hoa/thường của repository trong URL.
