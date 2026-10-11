# WorkChat Mini — lần thiết lập phát hành cuối cùng

## Mục tiêu

Cài một lần bằng Setup EXE, không giữ source/`_Internal`, không BUILD cho các lần cập nhật sau. **GitHub Actions là máy build**, Windows cá nhân chỉ cài và dùng.

## A. Phát hành lần đầu (khuyến nghị, không cần Windows BUILD)

1. Tạo/đăng nhập tài khoản GitHub. Cài **Git for Windows** và **GitHub CLI (`gh`)** nếu muốn tự động hóa việc đưa source lên repo.
2. Giải nén **source preview23** vào thư mục **mới, trống** (không đặt trong profile Story và không giải nén đè app).
3. Mở Terminal một lần và chạy `gh auth login`; đăng nhập bằng trình duyệt, cấp quyền cho repo. Để Git commit được, cấu hình tác giả một lần bằng `git config --global user.name "Your Name"` và `git config --global user.email "you@example.com"`.
4. Nhấn đúp **`PUBLISH-GITHUB-ONCE.bat`**. Chọn tên repo, nhập chính xác `PUBLISH PUBLIC` khi đã tự xem code không có bí mật. Script tạo repo GitHub public và đẩy source qua Git. Nếu đã có repo, làm theo Git/GitHub thông thường thay vì cố chạy lại script.
5. Trong trang GitHub repo → **Settings → Pages → Build and deployment → Source → GitHub Actions**. Nếu dùng GitHub Free thì repo cần public để dùng Pages. GitHub Pages có thể truy cập công khai kể cả một số repo private có hỗ trợ trả phí.
6. Mở repo → **Actions → WorkChat Mini - Build and Publish Windows App → Run workflow** trên `main`. Bảo đảm Actions được bật và token workflow được phép tạo Release. Script `contents: write` yêu cầu repo Settings → Actions → General → Workflow permissions phù hợp.
7. Đợi **cả build-test-release và publish-latest-feed** hiển thị thành công. Mở tab **Releases** và tải `WorkChatMini-Setup-v2.30.0-preview23.exe`. Kiểm tra `https://OWNER.github.io/REPO/latest.json` trả JSON (nếu tên repo là `OWNER.github.io`, URL có dạng `https://OWNER.github.io/latest.json`).
8. Trên máy có WorkChat cũ: đóng hoàn toàn bằng **Exit** tại system tray rồi chạy **Setup EXE thật**. Không xóa `%LOCALAPPDATA%\WorkChatMini`. Mở app → More → Updates, kiểm tra HTTPS feed mặc định đã có. Bấm Check; nếu cùng phiên bản, app báo đã mới nhất.
9. Kiểm tra Story và đăng nhập cũ còn nguyên. Có thể chạy **`TEST-APP.bat`** tại thư mục app đã cài để lấy Quick/Stress; không cần source/SDK. Chỉ sau đó mới xóa folder source/ZIP cũ.

> **Không tải/khởi chạy Setup EXE nếu Actions chưa PASS hoặc Release chưa tồn tại.** Source ZIP không phải bản cập nhật.

## B. Chỉ khi chưa thể dùng GitHub

Chạy `FIRST-AND-LAST-LOCAL-BUILD.bat` từ source preview23 đã giải nén; script tự Source Check → BUILD → Quick+Stress → Install local, **không xóa bản đã cài**. Thoát WorkChat từ tray trước khi chạy. Nếu thất bại ở BUILD/test, đừng gỡ WorkChat cũ.

Build offline **không thể tự biết repository GitHub chưa tồn tại**. Sau khi có repo/feed, dán **một lần** URL `https://OWNER.github.io/REPO/latest.json` vào More → Updates. App lưu URL trong settings. Đây là ngoại lệ; ưu tiên cách A để không BUILD tại máy.

## C. Phát hành bản sau (không cần BUILD tại máy)

Khi có source phiên bản mới hơn và đã được duyệt, commit/push source lên **cùng GitHub repository**, cập nhật `_Internal/VERSION.txt`, `WorkChatMini.csproj`, `BUILD.bat` và release notes đồng bộ. Sau đó chạy workflow **Run workflow** từ `main` (hoặc push tag `v<version>` khớp chính xác). GitHub tự test, build và công bố `latest.json`. Trong WorkChat: More → Updates → Check → Verify → Restart & install. Nếu build/test thất bại, feed cũ không được thay đổi.

GitHub Actions không được tự động chạy chỉ vì app mở; bản cập nhật chỉ xuất hiện **sau khi source mới được đưa lên repository và phát hành thành công**. Không có repo thì không có feed online. Nếu đổi tên repo/owner, người dùng phải nhập feed mới một lần.

## D. Khôi phục / lỗi

- App chạy nhưng Story chưa thấy: đừng xóa dữ liệu; kiểm tra `%LOCALAPPDATA%\WorkChatMini` và đúng user Windows.
- Lỗi CI: tải artifact `workchat-windows-test-reports` từ Actions, gửi log cụ thể. Build/test Windows chưa được chứng minh bằng kiểm tra tĩnh.
- Cập nhật thất bại: giữ EXE cũ, log ở `%LOCALAPPDATA%\WorkChatMini-Updates\Logs`. Có `RESTORE-PREVIOUS.bat` trong bản cài và `WorkChatMini.previous.exe` khi cập nhật tại chỗ.
- Build local không có feed nhúng: nhập feed trong More → Updates một lần, **không cần build lại**.
- Release chưa được ký số Authenticode: Windows SmartScreen có thể cảnh báo. Chỉ tin repo bạn kiểm soát, kiểm tra checksum; cân nhắc ký số nếu phân phối công khai.

**Tài liệu tham khảo:** https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site và https://cli.github.com/manual/gh_release_create
