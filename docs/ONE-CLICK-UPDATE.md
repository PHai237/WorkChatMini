# Cập nhật WorkChat trên Windows mà không xóa app

## Khi bạn đang ở preview20 / preview21.1

**Cách khuyến nghị — preview21.2:**

1. Giải nén `WorkChatMini-source-v2.30.0-preview21.2.zip` vào một thư mục riêng, không ghi đè app đã dùng.
2. Nhấn đúp `UPDATE-NOW.bat` trong thư mục vừa giải nén. Bạn không cần tự chạy `BUILD.bat`.
3. Bản này tự build bằng .NET SDK sẵn có trên máy, tạo gói ZIP nhị phân, kiểm tra SHA-256 và nhận diện EXE WorkChat đang dùng.
4. Nếu WorkChat còn chạy, hãy hoàn thành Story save, cuộc gọi và download, chọn **Exit** trong system tray (bấm X chỉ ẩn ứng dụng), rồi nhấn Enter để tiếp tục.
5. Script thay EXE và mở lại WorkChat. Dữ liệu `%LOCALAPPDATA%\WorkChatMini` không bị xóa.

**Sửa nhanh preview21.1 không cần tải lại full ZIP:** Đặt file `RETRY-UPDATE-NOW.bat` tải riêng vào chính thư mục source preview21.1 đang có (nằm cùng `UPDATE-NOW.bat`), rồi nhấn đúp file mới. Nó sửa duy nhất cách truyền `-SourceRoot` và vẫn build/cập nhật preview21.1. Không có Windows SDK thì phương án build cục bộ chưa chạy được.

## Hai loại ZIP không được nhầm

- `WorkChatMini-source-...zip` chứa source; updater trong app từ chối theo thiết kế.
- `WorkChatMini-update-...zip` chứa đúng `WorkChatMini.exe` và `update.json`, được tạo sau BUILD hoặc bởi GitHub Actions Windows. Chỉ file này mới đưa vào `More → Updates` được.

## Hướng cập nhật chỉ bằng một nút trong tương lai

Để không còn phải giải nén/build local, cần thiết lập GitHub repository, Windows release workflow, Pages feed và phát hành ít nhất một gói ZIP nhị phân. Ở thời điểm bàn giao preview21.2, **chưa có bản phát hành Windows đã xác minh hoặc feed thực tế** trong hội thoại này.

## An toàn

Không xóa hay sửa `%LOCALAPPDATA%\WorkChatMini`. Giữ backup Story trước một bản nâng cấp lớn. Nếu thất bại trước bước cài, EXE đang dùng không bị thay thế. Nhật ký updater sau khi chạy nằm trong `%LOCALAPPDATA%\WorkChatMini-Updates\Logs`.
