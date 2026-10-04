# Hướng dẫn phát triển

## Yêu cầu môi trường

- Windows và .NET 10 SDK.
- SQL Server đã được cấu hình cho ứng dụng nếu cần đăng nhập/chạy app. Cấu hình ứng dụng nằm trong LibraryManagement/appsettings.json; không đưa connection string hoặc thông tin đăng nhập vào tài liệu hay source.

## Build và chạy

Từ thư mục gốc:

    dotnet build LibraryManagement.slnx
    dotnet run --project LibraryManagement/LibraryManagement.csproj

## Kiểm thử không dùng SQL

Chạy nhóm test theo tên fully-qualified:

    dotnet test LibraryManagement.slnx --filter "FullyQualifiedName!~Integration"

Project test dùng xUnit và Moq. Một số test integration có guard riêng và được skip nếu biến môi trường không có.

## SQL integration tests

SQL integration là user/environment-managed. Chúng yêu cầu LIBRARY_TEST_SQL_MASTER_CONNECTION trỏ tới database master mà người chạy có quyền sử dụng. Không lưu giá trị biến môi trường này trong repository và không dùng connection này thay cho cấu hình database của ứng dụng.

SQL integration nằm ngoài phạm vi xác minh mặc định của thay đổi tài liệu này. Người dùng tự quản lý instance, authentication và thời điểm chạy.

## Quy tắc kiến trúc khi thêm thay đổi

- Với workflow mới, đi theo View → ViewModel → Service → Repository → Data/SQL. View/ViewModel không mở SqlConnection hay chạy migration DDL.
- Service sở hữu điều phối nghiệp vụ/transaction; Repository không tham chiếu View hoặc WPF. Giữ các lazy compatibility guards hiện có khi cần tương thích.
- Dùng IUserDialogService cho tương tác dialog của luồng mới; adapter WPF nằm trong Views.
- Giữ ViewModel trong folder hiện tại, các service trong Services, persistence trong Repositories và migration trong Data. Không tạo Domain/Application/Infrastructure/Features chỉ để tổ chức lại.
- Tìm module gần nhất trong docs/modules.md; đặt unit test cạnh nhóm test hiện hành. Chỉ dùng SQL integration khi cần xác minh SQL/persistence.
- Trước khi xóa API legacy, migration helper, snapshot field hay idempotency check, tìm tất cả caller và kiểm tra ghi chú tương thích trong docs/architecture.md.
