# Cấu trúc dự án

Solution giữ hai project hiện tại: ứng dụng WPF và test. Việc nhóm thư mục không thay đổi namespace, tên lớp, binding, SQL, API hoặc cấu hình chạy.

[Kết quả kiểm tra lần tinh gọn](structure-cleanup.md).

```text
LibraryManagement.slnx
LibraryDB.sql                  Schema dùng bởi SQL integration fixture
LibraryManagement/
  App.xaml, App.xaml.cs        Điểm khởi động và resource WPF
  appsettings.json            Cấu hình chạy, vẫn được copy ra output
  Views/                      Giữ đường dẫn XAML và code-behind hiện tại
  ViewModels/                 State và command của các màn hình
  Models/
    Accounts/                 Tài khoản và hồ sơ cá nhân
    Books/                    Đầu sách, bản sách, tìm kiếm và tồn kho
    Circulation/              Mượn/trả, lựa chọn bản sách và chính sách mượn
    Fees/                     Khoản phí và quy tắc công nợ
    Notifications/            Thông báo và thông tin nhắc hạn
    Readers/                  Độc giả, eligibility và truy vấn hồ sơ
  Services/
    Accounts/, Books/, Circulation/, Fees/, Notifications/, Readers/
    ...                       Service độc lập và lỗi nghiệp vụ dùng chung
  Repositories/               SQL và ánh xạ dữ liệu
  Data/
    Database.cs               Kết nối database
    LibraryDatabaseStartupMigration.cs
    Migrations/               Các migration hiện có, giữ nguyên thứ tự gọi
  Commands/, Converters/, Helpers/, Properties/
LibraryManagement.Tests/
  Accounts/, Books/, Circulation/, Readers/, Fees/, Notifications/
  Dashboard/, History/        Test theo module
  Application/                Navigation, tích hợp toàn app và kiểm tra chung
  Data/                       Kiểm thử schema và index
  Support/                    SqlIntegrationFixture, StaHelper
  AssemblyInfo.cs             Thiết lập assembly test
docs/
  architecture.md, modules.md, development.md, project-structure.md
  coding-conventions.md
  audits/                     Các báo cáo lịch sử
scripts/
  Run-SqlIntegrationTests.ps1, Test-BorrowSql.ps1
  sql/FillMissingReaderDemoData.sql
NuGet/NuGet.Config             Giữ vị trí cấu hình NuGet hiện tại
.github/workflows/            CI hiện tại
```

## Quy tắc đặt file

- Giữ các layer hiện tại; chọn nhóm module có sẵn trong Models, Services và project test. Các layer nhỏ như Repositories và ViewModels vẫn để phẳng để tránh thêm cấp thư mục không cần thiết.
- Namespace không tự đổi theo thư mục con. Ví dụ các model vẫn dùng `LibraryManagement.Models`; service vẫn dùng `LibraryManagement.Services`.
- SDK tự tìm file C# theo thư mục con. Không thêm Compile Include trùng với mặc định của SDK.
- Giữ đường dẫn XAML, `x:Class`, URI resource và cặp code-behind; việc đổi đường dẫn giao diện cần một thay đổi riêng có kiểm tra WPF.
- `LibraryDB.sql` vẫn ở root vì project test copy file này vào output. Script SQL mẫu ở `scripts/sql` chỉ chạy thủ công; không được thêm vào startup hay migration.
- Không gộp hoặc xóa migration cũ, field snapshot, API tương thích hay kiểm tra transaction chỉ để giảm số file.
- Các báo cáo trong `docs/audits` mô tả thời điểm kiểm tra trước đây. Trạng thái pass/fail và số dòng trong đó không phải kết quả kiểm tra hiện tại.
- `bin`, `obj`, `.vs`, `TestResults` và log là sản phẩm cục bộ được Git bỏ qua. Log ở root đã được gom vào `work/logs`; chúng không tham gia build.

## Kiểm tra khi thay đổi cấu trúc

Chạy từ root:

```powershell
dotnet build LibraryManagement.slnx -c Release
dotnet test LibraryManagement.slnx -c Release --no-build --filter "Category!=Integration"
```

Khi chuyển file, kiểm tra nội dung giữ nguyên, danh sách Compile/Page/Resource không mất file và `LibraryDB.sql`/`appsettings.json` vẫn được copy ra output. Build thành công không thay thế kiểm tra giao diện và SQL trên môi trường chạy thực tế.
