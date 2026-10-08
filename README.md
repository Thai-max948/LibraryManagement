# LibraryManagement

Ứng dụng desktop quản lý thư viện trên Windows, dùng WPF và SQL Server để quản lý sách, độc giả, mượn/trả, phí, lịch sử và thông báo.

## Công nghệ

- C# / .NET 10 for Windows, WPF và MVVM
- SQL Server qua ADO.NET và Microsoft.Data.SqlClient
- BCrypt.Net-Next cho băm mật khẩu
- xUnit và Moq cho kiểm thử

Cần Windows, .NET 10 SDK và SQL Server đã cấu hình cho ứng dụng trong LibraryManagement/appsettings.json.

## Build, chạy và kiểm thử

Từ thư mục gốc:

    dotnet build LibraryManagement.slnx
    dotnet run --project LibraryManagement/LibraryManagement.csproj
    dotnet test LibraryManagement.slnx --filter "FullyQualifiedName!~Integration"

SQL integration tests do người dùng/môi trường quản lý. Xem [hướng dẫn phát triển](docs/development.md).

## Tài liệu

- [Cấu trúc dự án và quy tắc đặt file](docs/project-structure.md)
- [Kiến trúc và các luồng chính](docs/architecture.md)
- [Bản đồ module](docs/modules.md)
- [Hướng dẫn phát triển](docs/development.md)
- [Quy ước viết mã](docs/coding-conventions.md)
- [Báo cáo kiểm tra trước đây](docs/audits/README.md)
