# WorkChat Mini — cài đặt một lần, cập nhật qua GitHub

**Bắt đầu từ source v2.30.0-preview20.** Cấu hình GitHub repository/Pages cần thực hiện một lần. Từ các lần phát hành sau, GitHub Actions tạo EXE, ZIP cài mới và ZIP cập nhật; người dùng không cần .NET SDK hoặc BUILD.bat.

## Cấu hình một lần (người phát triển)

1. Tạo **repository GitHub** (chưa được tạo hộ). Đưa source vào repo, bao gồm thư mục `.github/workflows/` và `_Internal/Updater/`. Không commit API key, `%LOCALAPPDATA%`, `WorkChatMini.exe`, `_Releases`, `bin`, `obj` hoặc dữ liệu Story. Dùng `.gitignore` trong source.
2. Trong repo → **Settings → Actions → General**, cấp quyền cho workflows cần tạo Release (`GITHUB_TOKEN` contents: write; workflow tự khai báo quyền). Nếu chính sách repo cấm write tokens, người quản trị phải cho phép.
3. Trong repo → **Settings → Pages → Build and deployment**, chọn **Source: GitHub Actions**. Kiểm tra mức visibility/GitHub Pages phù hợp với tài khoản. Một feed cập nhật mà app tải không xác thực sẽ cần địa chỉ HTTPS truy cập được từ máy người dùng. Nếu dùng repo private và không có Pages công khai phù hợp, dùng feed HTTPS được quản lý riêng thay vì giả định GitHub sẽ cho phép tải ẩn danh.
4. Đảm bảo `_Internal/VERSION.txt`, `<Version>` trong csproj và `BUILD.bat` cùng phiên bản. Không thay version bằng cách đổi tên ZIP.
5. Cách dễ nhất: repo → **Actions → WorkChat Mini - Build and Publish Windows App → Run workflow**, chọn nhánh mặc định. Hoặc tạo/push Git tag `v2.30.0-preview20` (và tag tương ứng các bản sau). **Không chạy BUILD ở máy người dùng.**
6. Workflow thực hiện Source-check → Windows Release BUILD/publish → xác minh hai ZIP → Quick/Stress self-test → tạo GitHub Release → triển khai GitHub Pages `latest.json` và trang tải cài đặt. Nếu BUILD/self-test thất bại, workflow chặn phát hành. **Không có release nào được xác nhận trước khi workflow chạy thành công trên repo thật.** Nếu GitHub-hosted Windows runner không hỗ trợ tương tác WPF của self-test, cần self-hosted Windows runner có desktop session hoạt động, hoặc bổ sung kiểm thử headless đủ mạnh trước khi phát hành; không bỏ test gate chỉ để lên release.

## URL mà WorkChat tự biết

Workflow đưa URL GitHub Pages vào assembly metadata lúc BUILD (không hardcode tài khoản trong source):

- Repository `ACCOUNT/REPO` → `https://account.github.io/repo/latest.json`
- Repository đặc biệt `ACCOUNT/ACCOUNT.github.io` → `https://account.github.io/latest.json`

Trong **More → Updates…**, ô feed được điền sẵn nếu app được biên dịch bằng workflow. Cửa sổ Updates tự kiểm tra phiên bản khi mở, nhưng **không tự cài/khởi động lại**. Người dùng bấm **Verify update → Restart & install** sau khi kết thúc cuộc gọi và hoạt động Story.

Trong các build local bằng `BUILD.bat`, địa chỉ mặc định trống. Người dùng có thể dán feed HTTPS một lần; WorkChat lưu URL trong settings. Người đã cài preview18/19 cũng cần nhập feed HTTPS thủ công **một lần** nếu chưa có feed mặc định.

## Hai tài sản trong mỗi GitHub Release

- `WorkChatMini-install-v<version>.zip`: dành cho máy cài **lần đầu**. Giải nén ZIP đầy đủ → chạy `INSTALL-APP.bat` → mở WorkChat Mini từ Start Menu. Không cần build, không cần .NET SDK, không cần giải nén source. Có EXE tự chứa và helper cài đặt/khôi phục.
- `WorkChatMini-update-v<version>.zip`: chỉ gồm `WorkChatMini.exe` và `update.json` (kích thước, version và SHA-256 của EXE). App tải, xác minh SHA-256 toàn ZIP theo feed, kiểm tra manifest/checksum EXE và phiên bản, sau đó trình helper thay EXE khi app đã thoát.

Có thêm `latest.json` (feed) và `checksums-sha256.txt` trong Release. Nội dung `latest.json` trên GitHub Pages được đồng bộ sau khi upload Release thành công; dùng địa chỉ Pages ổn định, không dùng link asset theo phiên bản cho ô feed.

## Dữ liệu và giới hạn an toàn

- App nằm ở `%LOCALAPPDATA%\Programs\WorkChatMini`; Stories, Memory, preferences, WebView2 profile và các đăng nhập nằm riêng ở `%LOCALAPPDATA%\WorkChatMini`. Cập nhật không xóa dữ liệu hay profile.
- Không tự cắt cuộc gọi, audio, download hoặc Story đang xử lý. EXE cũ được lưu dưới tên `WorkChatMini.previous.exe` trong thư mục ứng dụng. Có thể khôi phục bằng `RESTORE-PREVIOUS.bat` sau khi thoát WorkChat từ tray.
- **Mã cập nhật chưa được ký số Authenticode.** SHA-256 xác nhận file khớp với feed, nhưng nếu cả feed/repository đều bị chiếm quyền kiểm soát thì SHA-256 không chứng minh nhà phát hành. Không cài từ nguồn không tin cậy. Để phát hành công khai cho người khác, cần code signing và publisher verification.
- Cơ chế update nhị phân hiện thay **chỉ file EXE**, giữ updater helper và cấu trúc cài đặt có sẵn. Các bản cập nhật sau cần giữ tương thích với helper đã phát hành; nếu sửa helper không tương thích, hãy phát hành lại install ZIP và hướng dẫn người dùng nâng cấp helper an toàn.
- Github Release/Pages/Actions phải được thiết lập bởi chủ repo. Source ZIP này **không tự đăng nhập GitHub, không tự mở repository và không tự phát hành bản build**.

## Checklist xác nhận trên Windows / GitHub

1. GitHub Actions chạy Source-check, BUILD, Quick và Stress sạch, đúng 22/0/0 mỗi chế độ; nếu runner WPF không đáp ứng, cần có log chỉ rõ và fix trước khi deploy.
2. Release chứa đúng hai ZIP và checksum; GitHub Pages `latest.json` trả JSON qua HTTPS; ZIP tải từ link trong JSON khớp `packageSha256`.
3. Giải nén **install ZIP** trên máy Windows mới và chạy `INSTALL-APP.bat` (không cần source, .NET SDK); shortcut Start mở app.
4. Trên một bản đã cài cũ hơn: More → Updates → tự thấy bản mới → Verify update → Restart & install; vẫn giữ Story và WebView2 login.
5. Test từ chối ZIP cùng phiên bản, version thấp hơn, hash sai, thiếu file, HTTP redirect; không đổi EXE khi có Discord call/Story operation.
6. Thoát từ tray và kiểm tra `RESTORE-PREVIOUS.bat` trên bản cài thử nghiệm.

**Workflow sẽ không sản xuất binary thật ngay trong cuộc hội thoại.** Để tạo được file `.exe`, nó phải chạy trên Windows runner của GitHub hoặc máy Windows có SDK.


## From preview21: click-to-install Setup EXE and test without build

GitHub Release also publishes `WorkChatMini-Setup-v<version>.exe` from Inno Setup. This is the **primary user download**: no unzip, no SDK. Legacy install ZIP remains for compatibility. Setup installs under LocalAppData, does not delete `%LOCALAPPDATA%\WorkChatMini`, and refuses to auto-close a busy WorkChat session. Close app completely from tray before installing.

`AUTO-TEST.bat` **never builds**; it tests the installed executable by default, or the newly built binary with `--built`. Reports are kept out of root. `TEST-APP.bat` inside the new installed app can be run without a source tree or SDK. BUILD puts binaries under `_Internal/Build`, ZIPs and Setup EXE under `_Internal/Releases`.
