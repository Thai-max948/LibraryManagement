# Kết quả tinh gọn cấu trúc

Ngày: 08/10/2026. Phạm vi: tổ chức file của phiên bản ứng dụng hiện tại.

## Thay đổi

| Khu vực | Số file chuyển | Kết quả |
|---|---:|---|
| Models | 40 | Nhóm Accounts, Books, Circulation, Fees, Notifications, Readers |
| Services | 23 | Nhóm theo chức năng tương ứng; các service độc lập vẫn ở cấp Services |
| Data | 14 | Migration cụ thể vào Data/Migrations; kết nối và điều phối startup vẫn ở Data |
| Tests | 62 | Nhóm theo module; fixture và STA helper vào Support |
| Tài liệu và SQL mẫu ở root | 8 | Báo cáo vào docs/audits, convention vào docs, script mẫu vào scripts/sql |
| Log cục bộ ở root | 2 | Gom vào work/logs, tiếp tục được Git bỏ qua |

Tổng cộng 149 file được chuyển, gồm 147 file đã được Git quản lý và 2 log cục bộ. Không xóa file nguồn. Khi chưa stage, Git có thể hiển thị đường dẫn cũ là deleted và thư mục mới là untracked; cả hai phía thuộc cùng lần chuyển file.

Đã bổ sung bản đồ cấu trúc, cập nhật liên kết tài liệu và bỏ qua TestResults trong Git. Các báo cáo lịch sử vẫn được giữ; liên kết tới source được điều chỉnh theo vị trí mới.

## Kiểm chứng

- Trước khi chuyển: working tree sạch, Release build solution thành công với 0 warning/0 error. Có snapshot source và bảng hash lưu ngoài checkout.
- Sau khi chuyển: đủ 271 file gốc đã được Git quản lý tại đường dẫn cũ hoặc đường dẫn được ánh xạ mới.
- 253 file mã nguồn/cấu hình (C#, XAML, project, solution, SQL và JSON) giữ nguyên SHA-256. Các thay đổi nội dung chỉ nằm ở tài liệu và .gitignore.
- Danh sách MSBuild trước/sau khớp theo ánh xạ: app có 155 Compile, 28 Page, 1 ApplicationDefinition; test có 63 Compile. Resource, EmbeddedResource và None cũng khớp.
- Đường dẫn và nội dung XAML, namespace, tên class, command/binding, project reference và workflow CI giữ nguyên.
- appsettings.json trong output app và LibraryDB.sql trong output test khớp file nguồn.
- Release **Rebuild** solution sau cùng thành công: 0 warning, 0 error. git diff --check không có lỗi whitespace.
- Kiểm tra các liên kết file Markdown trong README/docs thành công.

## Giới hạn xác minh

Đã chạy nhóm test Category!=Integration nhưng VSTest dừng trước khi thực thi test vì không kết nối được testhost sau 30 giây. Không có kết quả pass/fail và không sửa hạ tầng test. Chưa chạy thao tác UI thủ công hoặc SQL integration trong lần tinh gọn này.

Không đổi nghiệp vụ, SQL/schema, thứ tự migration hoặc dữ liệu database. Đối chiếu hash và danh sách build xác nhận lần chuyển không sửa nội dung triển khai hay làm mất file được biên dịch; kiểm chứng này không thay thế QA trên app đang chạy.

Xem [cấu trúc hiện tại](project-structure.md) để đặt file cho các lần bảo trì sau.
