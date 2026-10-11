# WorkChat Mini v2.30.0-preview23 — Release Bridge

**Nguồn phát hành cho Windows — không phải ZIP để chọn trong More → Updates.**

Phiên bản này bao gồm mọi tính năng WorkChat + AI Story đến preview22 (Memory v6, Story Director, Scene Compass, Review before commit), đồng thời chuẩn bị để **những bản sau được GitHub Actions BUILD** và người dùng **chỉ bấm Updates**.

## Cách sử dụng đúng (khuyên dùng)

1. Giải nén source ZIP vào thư mục **mới, sạch**, không giải nén đè lên WorkChat đã cài.
2. **Một lần duy nhất**: đưa source này lên GitHub repository mà bạn kiểm soát. Xem `docs/RELEASE-BRIDGE-START-HERE.md`. Có script `PUBLISH-GITHUB-ONCE.bat` nếu đã cài Git for Windows, GitHub CLI và đăng nhập GitHub.
3. Trong repo, bật **Settings → Pages → Source: GitHub Actions**. Sau đó chạy **Actions → WorkChat Mini - Build and Publish Windows App → Run workflow**. Chỉ khi build/test Windows thành công, Releases mới có `WorkChatMini-Setup-v2.30.0-preview23.exe` và `WorkChatMini-update-v2.30.0-preview23.zip`.
4. Trên PC có WorkChat cũ: thoát hẳn từ tray, tải **Setup EXE thật** từ GitHub Releases và cài **đè vào cùng vị trí**. App/Story/WebView2 không bị xóa có chủ đích. Đăng nhập và kiểm tra AI Story sau khi nâng cấp.
5. Từ bản tiếp theo: **More → Updates → Check for updates → Verify update → Restart & install**. EXE do GitHub phát hành có feed HTTPS nhúng sẵn. Không cần giữ source, `_Internal`, BAT hay .NET SDK trên máy sử dụng.

**Nếu chưa dùng GitHub:** `FIRST-AND-LAST-LOCAL-BUILD.bat` là phương án dự phòng chỉ dành cho Windows đã có .NET SDK. Nó build → Quick/Stress → cài đè tại LocalAppData; **bản build local không tự biết GitHub feed cho đến khi bạn dán URL `https://OWNER.github.io/REPO/latest.json` vào More → Updates**. Vì vậy nên ưu tiên GitHub từ đầu và tải Setup EXE.

## Đã sửa để tránh lặp lại vòng BUILD

- Bundled updater: EXE nhúng `_Updater/Apply-WorkChatUpdate.ps1` và khi bắt đầu cập nhật nó tự làm mới helper đang cài. Format update ZIP vẫn đúng **hai file** `WorkChatMini.exe` + `update.json`, tương thích preview20+.
- CI phát hành trên Windows: source audit, .NET restore/build/publish, kiểm tra ZIP & SHA-256, Quick/Stress 22/22, tạo Setup Inno, upload GitHub Release, deploy `latest.json` lên GitHub Pages.
- Không ghi đè binary của Release đã xuất bản. Cần **tăng số phiên bản** cho lần phát hành sau.
- `SOURCE-CHECK.bat --nopause` tránh treo GitHub Action; `AUTO-TEST.bat` không BUILD; root chỉ có công cụ phát triển và tài liệu.
- Cài bản mới khi đã có bản cũ sẽ giữ một bản EXE trước đó để phục hồi; không xóa `%LOCALAPPDATA%\WorkChatMini`.
- Helper cập nhật tự dọn staging EXE ở `%LOCALAPPDATA%\WorkChatMini-Updates\Pending` sau khi đã cài hoặc rollback, không động vào Story.

## Trạng thái kiểm chứng

Đã sửa source và chạy kiểm tra tĩnh/ZIP trong môi trường Linux. **Chưa thực hiện được Windows build, Quick/Stress hay GitHub Release thật tại đây**. Trước khi cài bản này, GitHub Actions phải PASS hoặc Windows local BUILD/Quick/Stress phải PASS. Không đưa source ZIP vào giao diện Updates.

Về an toàn: GitHub repo public sẽ công khai source. Không upload API keys, `.env`, dữ liệu cá nhân, `WorkChatMini-Data`, hoặc WebView2 profile. Release EXE hiện **chưa ký Authenticode**, nên chỉ tải từ repository bạn sở hữu và xác minh SHA-256; không phân phối cho người lạ dưới danh nghĩa ứng dụng đã ký xác thực.
