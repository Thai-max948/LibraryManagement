# 📚 LibraryManagement - Library Management System (Library OS)

Hệ thống Quản lý Thư viện hiện đại, hiệu năng cao được xây dựng trên nền tảng **WPF .NET 10** theo mô hình kiến trúc **MVVM (Model - View - ViewModel)** kết hợp phân tầng **Service - Repository - ADO.NET** và cơ sở dữ liệu **Microsoft SQL Server**.

---

## 🚀 Công nghệ sử dụng

* **Framework & UI:** .NET 10.0-windows (WPF - Windows Presentation Foundation)
* **Kiến trúc:** MVVM (Model - View - ViewModel) + Service Pattern + Repository Pattern
* **Cơ sở dữ liệu:** Microsoft SQL Server (kết nối trực tiếp qua `Microsoft.Data.SqlClient`)
* **Bảo mật:** `BCrypt.Net-Next` (Băm mật khẩu người dùng với muối an toàn chuẩn công nghiệp)
* **Cấu hình:** `Microsoft.Extensions.Configuration` / `appsettings.json`

---

## 📂 Cấu trúc dự án

```text
LibraryManagement/
├── Commands/          # Triển khai RelayCommand chuẩn MVVM
├── Converters/        # Value Converters cho WPF Data Binding
├── Data/              # Quản lý kết nối CSDL (Database.cs) từ appsettings.json
├── Helpers/           # PasswordBoxHelper hỗ trợ micro-interaction ẩn/hiện mật khẩu
├── Models/            # Các thực thể dữ liệu: Book, Reader, BorrowRecord, User
├── Repositories/      # Tầng truy cập dữ liệu (Data Access Layer - ADO.NET thuần)
├── Services/          # Tầng nghiệp vụ (Business Logic Layer, kiểm soát phân quyền & transaction)
├── ViewModels/        # Tầng điều khiển giao diện & ràng buộc dữ liệu (INotifyPropertyChanged)
└── Views/             # Giao diện XAML người dùng (Windows, Controls, Dialogs)
```

---

## ⚙️ Hướng dẫn cài đặt & Khởi chạy

### 1. Chuẩn bị cơ sở dữ liệu (SQL Server)
1. Mở **SQL Server Management Studio (SSMS)** hoặc **Azure Data Studio**.
2. Kết nối tới SQL Server instance của bạn (mặc định: `.\SQLEXPRESS` hoặc `(local)`).
3. Mở và thực thi file script:
   ```text
   LibraryDB.sql
   ```
   *Script sẽ tự động tạo cơ sở dữ liệu `LibraryDB`, cấu trúc bảng và nạp sẵn dữ liệu kiểm thử (Seed Data).*

### 2. Cấu hình kết nối (Connection String)
Kiểm tra và cập nhật chuỗi kết nối trong file [`LibraryManagement/appsettings.json`](LibraryManagement/appsettings.json) nếu SQL Server của bạn có cấu hình khác:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=LibraryDB;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

### 3. Build & Chạy ứng dụng
Mở terminal tại thư mục gốc và chạy lệnh:

```bash
dotnet restore
dotnet build
dotnet run --project LibraryManagement/LibraryManagement.csproj
```
Hoặc mở file [`LibraryManagement.slnx`](LibraryManagement.slnx) bằng **Visual Studio 2022+** và nhấn **F5**.

---

## 🔑 Tài khoản kiểm thử mặc định (Seed Data)

Dữ liệu mẫu đã được mã hóa bằng **BCrypt Hash** an toàn:

| Tên tài khoản (Username) | Mật khẩu | Vai trò (Role) | Email |
| :--- | :--- | :--- | :--- |
| **`admin`** | `admin123` | **Administrator** (Toàn quyền hệ thống) | `admin@library.com` |
| **`librarian`** | `librarian123` | **Librarian** (Nghiệp vụ thư viện) | `librarian@library.com` |

---

## ✨ Các tính năng chính

1. **Bảo mật & Xác thực:**
   - Đăng nhập, đăng ký tài khoản mới với hiệu ứng chuyển trang (Interactive Swipe Card).
   - Micro-interaction: nút toggle xem/ẩn mật khẩu kèm đồng bộ con trỏ.
   - Mật khẩu lưu trữ băm BCrypt an toàn, loại bỏ 100% rủi ro plaintext.
2. **System Dashboard:**
   - Thống kê thời gian thực: Tổng số đầu sách, số độc giả, số sách đang mượn.
   - Bảng hoạt động mượn sách gần đây và sách mới được trả.
3. **Quản lý Sách (Books Management):**
   - Tìm kiếm sách theo tiêu đề hoặc tác giả.
   - Thêm mới, chỉnh sửa và xóa sách có kiểm tra ràng buộc số lượng sách đang lưu hành.
4. **Quản lý Độc giả (Readers Directory):**
   - Quản lý danh bạ thành viên thư viện, kiểm tra định dạng email và số điện thoại.
5. **Mượn & Trả sách (Circulation Desk):**
   - Thực hiện mượn sách với giao dịch Transaction an toàn (ACID).
   - Trả sách, cập nhật số lượng tồn kho khả dụng (`AvailableQuantity`) và tính toán ngày trả thực tế.
6. **Lịch sử mượn/trả (Circulation History):**
   - Lọc lịch sử theo trạng thái (Borrowing/Returned) và khoảng thời gian (From/To Date).
   - Hiển thị rõ ràng cả Hạn trả (`Due Date`) và Ngày trả thực tế (`Return Date`).
7. **Quản trị người dùng & Phân quyền (Accounts - Admin only):**
   - Phân quyền nghiêm ngặt giữa Administrator và Librarian.
   - Chặn xóa hoặc hạ quyền Administrator cuối cùng trong hệ thống.
8. **Trang cá nhân (My Account):**
   - Xem thông tin tài khoản, cập nhật họ tên, username, email và đổi mật khẩu cá nhân.