# Review dự án LibraryManagement

Repo: https://github.com/Thai-max948/LibraryManagement.git
Ngày review: 19/09/2026
Phạm vi: đọc tĩnh toàn bộ source (~7.900 dòng, không tính bin/obj). Chưa build, chưa chạy.

## Tổng quan

- Kiến trúc View → ViewModel → Service → Repository → ADO.NET khớp tài liệu thiết kế.
- Phần nghiệp vụ mượn/trả làm chắc, transaction đúng.
- Có 3 vấn đề nghiêm trọng (bảo mật + repo), 4 bug logic, 7 điểm kiến trúc nên dọn.

## Thứ tự xử lý đề xuất

1. #1, #2, #3 (bảo mật, repo)
2. #4, #5, #6 (bug hiển thị và seed data)
3. #8, #9 (rủi ro mặc định quyền, schema lặp)
4. #10 → #14, #7

## Nhóm 1: nghiêm trọng

### #1. Password plaintext trong LibraryDB.sql

- File: `LibraryDB.sql`
- Seed `admin123` và `librarian123` được ghi thẳng vào cột `PasswordHash`.
- Repo public nên credential lộ hoàn toàn.
- Sửa: seed bằng hash BCrypt thật (tạo bằng `BCrypt.Net.BCrypt.HashPassword("...")` rồi dán vào script). Đổi mật khẩu admin mặc định.

### #2. Nhánh đăng nhập plaintext trong Authenticate()

- File: `Repositories/UserRepository.cs`, hàm `Authenticate()` và `VerifyPassword()`.
- Nhánh `isLegacyPlainText` so sánh `storedHash == password`. Ai ghi được vào DB chỉ cần đặt một dòng plaintext là đăng nhập được, BCrypt bị bỏ qua.
- Nhánh này chỉ tồn tại vì #1. Sửa #1 xong thì xóa hẳn nhánh legacy, `IsBCryptHash()` và `UpgradeToHashedPassword()`.

### #3. Commit bin, obj, .vs và thư mục không liên quan

- Repo nặng 246MB, riêng `.git` là 64MB.
- `LibraryManagement/bin` 169MB, `obj` 7.2MB, `.vs` 1.4MB, `.agents/skills` 4.6MB (không thuộc project).
- File `.gitignore.txt` sai tên (phải là `.gitignore`) và đang rỗng.
- Sửa:

```bash
git rm -r --cached LibraryManagement/bin LibraryManagement/obj .vs .agents
git mv .gitignore.txt .gitignore
```

Nội dung `.gitignore` tối thiểu:

```
bin/
obj/
.vs/
*.user
.agents/
```

Lịch sử git vẫn giữ file cũ (64MB). Muốn nhẹ hẳn thì dùng `git filter-repo` hoặc tạo repo mới.

## Nhóm 2: bug logic

### #4. History không hiển thị ngày trả thật

- File: `ViewModels/HistoryyViewModel.cs`
- `HistoryRow.ReturnDate = r.DueDate.ToString(...)`, trong khi cột XAML tên "Return".
- Kết quả: màn History hiện Due Date ở cột Return.
- Sửa: đổi thành `r.ReturnDate?.ToString("dd/MM/yyyy") ?? "—"`. Nếu vẫn muốn hiện hạn trả thì thêm cột "Due" riêng.

### #5. Sort ngày bằng string trong ReturnViewModel

- File: `ViewModels/ReturnViewModel.cs`, hàm `Load()`.
- `OrderByDescending(x => x.BorrowDate)` với `BorrowDate` là chuỗi `dd/MM/yyyy`, nên sort theo ngày trước tháng.
- Sửa: thêm field `DateTime BorrowDateValue` vào `ActiveBorrowRow` (giống `ActionDate` bên History) rồi sort theo field đó.

### #6. Seed data tự mâu thuẫn

- File: `LibraryDB.sql`, dòng BorrowId = 3.
- `Status = 'Returned'` nhưng `ReturnDate = NULL`.
- Ảnh hưởng: Recent Returnings trên Dashboard lọc `ReturnDate.HasValue` nên bỏ sót dòng này.
- Sửa: gán `ReturnDate` cụ thể, hoặc đổi Status thành `Borrowing`.

### #7. Username tự sinh bỏ qua hậu tố 1

- File: `Services/AuthService.cs`, hàm `Register()`.
- `suffix` khởi tạo 1 rồi `suffix++` trước khi dùng, nên username trùng nhảy từ `abc` sang `abc2`.
- Ảnh hưởng nhỏ. Sửa: khởi tạo `suffix = 0`.

## Nhóm 3: kiến trúc và rủi ro tiềm ẩn

### #8. Role mặc định mâu thuẫn

- `Models/User.cs`: `Role = "Administrator"`.
- DB: `DEFAULT 'Librarian'`.
- Hiện chưa lỗi vì mọi chỗ tạo user đều gán Role rõ ràng. Thêm một chỗ `new User()` quên gán là tự sinh admin.
- Sửa: đổi default trong model thành `"Librarian"`.

### #9. UserRepository tự tạo bảng trong constructor

- File: `Repositories/UserRepository.cs`, hàm `EnsureTableExists()`.
- Mỗi lần `new UserRepository()` tốn một lượt gọi DB.
- Schema bảng `Users` nằm ở hai nơi (script SQL và code), sửa một nơi dễ quên nơi kia.
- Sửa: bỏ khỏi constructor, để `LibraryDB.sql` tạo bảng.

### #10. Kiểm tra trùng email/username bằng GetAll()

- File: `Services/UserService.cs` (5 chỗ), `Services/AuthService.cs` (1 chỗ).
- Tải cả bảng Users về RAM rồi lọc bằng LINQ để kiểm tra một dòng.
- DB đã có `UQ_Users_Email` và `UQ_Users_Username`.
- Sửa: thêm `ExistsByEmail()` / `ExistsByUsername()` trong repository dùng `WHERE`, hoặc bắt `SqlException` 2601/2627 như `UserRepository.Add()` đang làm.

### #11. DataContext khai báo trong XAML

- Ví dụ: `HistoryView.xaml` có `<vm:HistoryViewModel/>`.
- Constructor ViewModel gọi DB, nên Visual Studio designer cũng gọi DB khi mở file. Dễ treo hoặc lỗi designer.
- Sửa: gán `DataContext = new HistoryViewModel();` trong code-behind.

### #12. Database chỉ chứa hàm tạo connection

- File: `Data/Database.cs`
- Class không có state nhưng mỗi repository vẫn `new Database()`.
- Sửa: đổi `GetConnection()` thành static, bỏ field `_db` trong các repository.

### #13. catch rỗng khi đọc appsettings.json

- File: `Data/Database.cs`
- `catch { }` nuốt mọi lỗi đọc config rồi âm thầm dùng connection string mặc định. Config hỏng nhưng app vẫn chạy với DB khác, rất khó debug.
- Sửa: ít nhất `Debug.WriteLine(ex)` trong catch.

### #14. README gần như trống

- README chỉ có một dòng `# LibraryManagement`.
- Nên có: mô tả ngắn, công nghệ, cách chạy `LibraryDB.sql`, tài khoản test, ảnh chụp màn hình.

## Điểm làm tốt (giữ nguyên)

- `BorrowService.BorrowBook()` và `ReturnBook()` dùng transaction, rollback khi lỗi.
- `UpdateAvailableQuantity()` có guard `AvailableQuantity + @Delta >= 0` ngay trong SQL.
- `MarkAsReturned()` có điều kiện `AND Status = 'Borrowing'`, chống trả hai lần ở tầng DB.
- Toàn bộ truy vấn dùng `SqlParameter`, kể cả `GetHistory()` build SQL động.
- `UpdateBook()` tính `AvailableQuantity = Quantity - borrowing`, không làm sai số sách đang mượn khi sửa Quantity.
- `EnsureAdmin()` nằm ở Service, không chỉ ẩn nút trên UI.
- Chặn xóa hoặc hạ quyền Administrator cuối cùng, chặn tự xóa tài khoản đang đăng nhập.
- `MapToX()` dùng `GetOrdinal` và `IsDBNull`.

## Giới hạn của review này

- Môi trường review là Linux, không có SQL Server, không chạy được WPF.
- Chưa kiểm chứng: build có qua không, lỗi binding lúc chạy, hành vi UI thật, hiệu năng với dữ liệu lớn.
- Các race condition trong `DeleteBook()`, `DeleteReader()`, `CanBorrow()` đã được chấp nhận trước đó (phạm vi single-user), không tính là lỗi.

## Checklist tự test sau khi sửa

- Đăng nhập bằng tài khoản seed mới (hash BCrypt) thành công.
- Sửa thẳng một dòng Users thành plaintext trong DB thì đăng nhập phải thất bại.
- Mượn, trả một sách, mở History: cột Return hiện đúng ngày trả.
- Màn Return sort đúng khi có nhiều bản ghi khác tháng.
- Dashboard hiện đủ Recent Returnings sau khi sửa BorrowId 3.
- `git status` không còn liệt kê bin, obj, .vs.
