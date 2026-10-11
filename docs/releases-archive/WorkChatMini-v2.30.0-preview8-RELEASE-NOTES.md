# WorkChat Mini v2.30.0-preview8 — Full Conversation Search & Memory Safety

**Ngày:** 2026-10-10. **Nền source:** toàn bộ `v2.30.0-preview7` (chưa có báo cáo Windows cho preview7). **Baseline Windows đã xác minh gần nhất:** preview6, Quick + Stress đều **22 PASS / 0 FAIL / 0 WARN**, theo báo cáo người dùng ngày 2026-10-10.

**Trạng thái preview8:** Source/static audit trong Linux, **chưa Windows BUILD, Quick/Stress hoặc kiểm tra giao diện WPF thực tế**. Người dùng chủ động dồn nhiều đợt mới kiểm thử một lần; bản này không bắt buộc chạy test ngay.

## Hạng mục đã thay đổi

### 1. Tìm kiếm toàn bộ hội thoại, không ảnh hưởng AI/Memory

- Hộp **Find** ở phía trên Chat, `Ctrl+F` mở nhanh; gõ cụm từ rồi Enter/Find, dùng ↑/↓ để chuyển các kết quả, Escape để xóa tìm kiếm và trở về Chat.
- Quét toàn bộ danh sách tin nhắn của Story đang mở (bao gồm hàng trăm/từng nghìn tin không được dựng sẵn trên màn hình). So khớp không phân biệt hoa thường, xử lý trên worker từ bản chụp các chuỗi immutable.
- Khi nhảy tới kết quả, dựng **tối đa 60 bubble** quanh tin đó; bôi đường viền của tin được chọn, cuộn tới đúng tin và có navigation **Earlier/Later context**; nút **Latest messages** trở về 60 tin gần nhất.
- Mỗi lần đổi Story/branch hay bắt đầu lượt Chat mới sẽ reset kết quả tìm kiếm, vô hiệu các search task cũ bằng request-id và Story-id. Không thay `conversation.json`, creative decisions hoặc memory schema khi tìm kiếm.

### 2. Cô lập luồng streaming khi ngắt/gửi lại

- Thêm generation epoch cho delta callbacks; chunk đến muộn từ run cũ bị từ chối.
- Dọn pending streaming buffer và timer khi kết thúc run; tránh gắn phần dư từ một request trước sang tin của request khác, không tăng WPF dispatcher calls.

### 3. Fail-closed khi Story Memory hỏng

- `StoryMemoryStore.LoadAsync` không còn trả memory mới trắng nếu một file `story-memory.json` **đã tồn tại** mà đọc/parse/normalize lỗi.
- Lỗi được báo ra UI; khóa các thao tác biên tập và giữ nguyên file cần phục hồi. Story chưa có file Memory vẫn khởi tạo v6 trắng như cũ.
- File temp lưu memory có tên riêng, dọn trong `finally`, giữ atomic replace thay vì chung `.tmp` dễ va chạm.

### 4. Proposal application có receipt/idempotency

- Trước khi **Add to Story / Save as Proposed / Apply Revision**, merge vào bản sao JSON của Memory; không biến đổi `_memory` trực tiếp trước khi lưu thành công.
- Lưu `AppliedProposalKeys` tùy chọn: SHA-256 từ thời điểm và payload proposal, nằm **trong chính `story-memory.json` được ghi cùng delta**. Nếu crash giữa bước ghi Memory và bước ghi `ProposalApplied` ở conversation, lần áp dụng sau phát hiện receipt, chỉ phục hồi flag, không merge delta lần 2.
- Không nâng schema khỏi v6; field optional không xuất hiện trên Story cũ khi chưa dùng. Receipt là dữ liệu kỹ thuật, không phải fact/canon, không gửi tới Gemini.
- **Giới hạn:** UI có thể báo lỗi khi lần lưu conversation flag thất bại; dữ liệu Memory đã lưu vẫn được bảo toàn. Các thao tác commit chương dùng checkpoint journal riêng không bị sửa.

### 5. Regression assertions (chưa được chạy trên Windows)

- Search 1.000 tin tìm keyword hiếm tại message 14 và 811, case-insensitive, query rỗng/nonmatch, window cố định 60.
- Memory JSON malformed/null fail closed; stage proposal không sửa original; receipt vẫn có sau serialize/load và không nhân bản khi gắn lại.
- Các bài test này nằm trong **Story foundation** của bộ 22 case hiện có; không bỏ qua assertion cũ.

## Không thay đổi

Shell browser, Discord call/media guard, tray/hotkey, adblock, notification quiet policy, Gemini/OpenAI provider, chapter retrieval, checkpoint / branch storage formats, Story Memory version, hay chính sách nội dung. Không thêm package, dịch vụ bên ngoài hoặc lượt gọi Gemini.

## Khi test theo lô sau

Mở ZIP vào thư mục mới, giữ nguyên `%LOCALAPPDATA%\WorkChatMini`; chạy `AUTO-TEST.bat` một lượt và gửi output BUILD nếu fail cùng hai báo cáo Quick/Stress nếu chạy được. Smoke-test riêng: Story 1.000 tin/nhánh A-B, Ctrl+F tìm tin cũ rồi Latest; tìm khi đang chuyển Story; Stop và Send liên tục; proposal Add-to-Story/Apply Revision. Chưa thể xác nhận hiệu năng/tương thích runtime khi chưa chạy Windows.
