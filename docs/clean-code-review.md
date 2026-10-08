# Clean code review — 08/10/2026

Phạm vi: toàn bộ mã C# của ứng dụng và project test sau lần sắp xếp thư mục.
Mục tiêu của lượt này là làm mã nhất quán mà không đổi hành vi đã chốt.

## Đã thực hiện

- Chạy formatter C# chuẩn của .NET cho cả hai project ở chế độ chỉ sửa whitespace.
- 30 trong 218 file C# thay đổi về trình bày (10 file ứng dụng, 20 file test).
- Không đổi tên public API, namespace, binding, XAML, SQL, migration, transaction, điều kiện nghiệp vụ hoặc dependency.
- Lưu bản sao 218 file C# trước khi chỉnh tại `C:\Users\Admin\Documents\Codex\2026-10-04\new-chat\outputs\clean-code-20261008\cs-before-valid.zip`.

## Đối chiếu 5 rule

| Rule | Kết quả lượt này |
|---|---|
| Readability | Chuẩn hóa các lỗi định dạng trên toàn bộ mã C#; tên và luồng xử lý giữ nguyên để tránh đổi binding hoặc hành vi. |
| Single Responsibility | Rà các ViewModel/Repository dài. Việc tách lớp hoặc luồng cần kiểm thử UI và dữ liệu thực tế, nên chưa thực hiện trong lượt giữ nguyên hành vi. |
| Duplication | `BorrowLimit` đang có một nguồn trong `ReaderEligibilityService`; kiểm tra tài chính mượn sách đi qua `FeeFinancialStandingRules` và provider. Không gộp thêm các đoạn SQL/transaction khi chưa có kiểm thử tích hợp. |
| Maintainability | Cả hai project dùng cùng formatter; kết quả kiểm tra lại không còn lỗi whitespace. |
| Behavior Preservation | Thay đổi mã chỉ là định dạng. Build Release thành công, 0 warning và 0 error. |

## Kiểm chứng và giới hạn

- `dotnet format <project-folder> whitespace --folder --verify-no-changes` thành công cho cả ứng dụng và test.
- `dotnet build LibraryManagement.slnx -c Release --no-restore -p:UseSharedCompilation=false -m:1` thành công.
- `git diff --check` không báo lỗi whitespace.
- Test `Category!=Integration` đã thử chạy nhưng VSTest không kết nối được với testhost sau 30 giây; không có ca test nào được xác nhận pass. Chưa kiểm thử UI thủ công hoặc SQL thực tế trong lượt này.
- Formatter khi mở solution trực tiếp không kết nối được build host qua named pipe trong môi trường hiện tại; chế độ folder dùng để hoàn tất phần định dạng và kiểm tra lại.

Các class dài và vùng lặp nghiệp vụ vẫn cần đánh giá riêng nếu sau này có thể chạy đủ test và kiểm tra app trực tiếp. Không coi định dạng là bằng chứng đã giải quyết toàn bộ Single Responsibility hay duplication.
