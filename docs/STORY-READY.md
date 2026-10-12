# AI Story — Tiêu chí sẵn sàng sử dụng

Không cần đợi AI Story hoàn hảo mới bắt đầu đọc truyện. Hãy phân biệt **chạy an toàn** và **chất lượng văn chương**.

## Cổng 1 — Phần mềm (bắt buộc)

- GitHub Actions của chính phiên bản sắp cài: BUILD, Quick, Stress, đóng gói, Release và latest.json đều thành công.
- Quick và Stress có báo cáo mới `22 PASS / 0 FAIL / 0 WARN` **mỗi chế độ**.
- Có thể mở Story, tạo một project thử riêng, lưu lại, thoát và mở lại mà không mất dữ liệu.
- Có thể tạo bản nháp, từ chối nó, tiếp tục Chat và xác nhận bản nháp bị từ chối không trở thành chương hoặc nguồn canon.
- Có thể chấp nhận chương và tạo backup rồi thử restore trên **project thử** (không làm với truyện thật).

Nếu lỗi một trong các mục trên: chưa dùng cho truyện chính; gửi log, không phát hành thêm tính năng.

## Cổng 2 — Chất lượng đọc (phải thử với Gemini thật)

Tạo Story **thử nghiệm** (không dùng dữ liệu riêng hay truyện đang viết), chọn `Review before commit`, viết ít nhất 5 chương liên tiếp và đọc lại. Sau mỗi chương đánh dấu những vấn đề:

1. Có lỗi sự kiện canon, người đang ở đâu, hoặc nhân vật biết bí mật chưa được tiết lộ không?
2. Hành động có nguyên nhân và hậu quả? Tính cách/quan hệ giữ đúng nhưng không máy móc?
3. Cảnh có đáng đọc, lời thoại có tự nhiên, nhịp có bị tóm tắt nhanh hoặc văn lặp lại?
4. Sự kiện của chương trước còn tác động đến chương sau?
5. Lượt viết hủy giữa chừng không tạo chương/canon mới?

Tiêu chí khởi đầu để dùng cá nhân: 5 chương có thể đọc tiếp, **0 lỗi continuity nghiêm trọng bị commit**, không thất lạc dữ liệu, và không cần sửa thủ công đại bộ phận chương. Đây là tiêu chí thực dụng, không phải điểm số đánh giá mô hình chính thức.

## Cổng 3 — Truyện dài (sau khi qua hai cổng đầu)

Thử 10–20 chương; kiểm tra lại các đầu mối từ chương 2–5, những thay đổi quan hệ và nội dung bí mật nhân vật. Chỉ sau khi qua cổng này mới cân nhắc `Auto-commit narrative` cho Story chính.

## Ranh giới của self-test

- Test tĩnh và test tổng hợp có thể phát hiện lỗi **code, lưu trữ, truy xuất và quản lý trạng thái**.
- Các bài thử đó **không** chứng minh Gemini sẽ luôn viết hay, viết đúng hoặc hiểu mọi ngụ ý.
- Khi chưa có log từ GitHub Actions/Windows và mẫu 5 chương thực tế: tình trạng là **chưa xác minh**, không phải PASS.

## Sử dụng ngay trong lúc chờ

Với bản app đã cài và hoạt động bình thường, có thể brainstorm bằng Chat ngay. Nếu thử Continue Story, hãy bắt đầu với Story thử và chọn `Review before commit` trong Settings, không dùng Auto-commit cho bản nháp còn cần đánh giá.
