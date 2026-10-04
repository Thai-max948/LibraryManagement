# Kiến trúc

## Mục đích và cấu trúc

LibraryManagement là ứng dụng desktop WPF trên Windows. Solution hiện dùng MVVM cùng các technical layer; không có DI container hay Clean Architecture project split.

Thư mục mã nguồn chính:

- Views: XAML và code-behind cho giao diện.
- ViewModels: state và command cho màn hình.
- Services: điều phối nghiệp vụ, kiểm tra quy tắc và transaction.
- Repositories: SQL và ánh xạ dữ liệu.
- Data: kết nối database và schema migrations.
- Models: entity, request, result, query và policy.
- Commands, Converters, Helpers, Properties: tiện ích WPF và assembly metadata.

## Hướng gọi

    View
      ↓
    ViewModel
      ↓
    Service
      ↓
    Repository
      ↓
    SQL Server

View hiển thị và xử lý tương tác giao diện. ViewModel giữ state và command. Service điều phối workflow nghiệp vụ. Repository đọc/ghi dữ liệu. Data quản lý kết nối và migration. Model chứa dữ liệu/policy được chia sẻ giữa các layer.

Composition hiện được viết thủ công trong MainViewModel, code-behind của một số View và XAML; notification runtime cũng được cấu hình tĩnh khi App khởi động.

## Khởi động và database

    App.OnStartup
      → cấu hình NotificationRuntime
      → AuthWindow
      → đăng nhập thành công
      → LibraryDatabaseStartupMigration
      → kiểm tra DueSoon
      → MainWindow
      → MainViewModel điều hướng module

AuthWindow điều phối migration sau đăng nhập và trước khi mở MainWindow. Startup migration chạy theo thứ tự dependency: BookValue, BookAudit, BookCopy, LoanPolicy, ReturnOutcome, CirculationAudit, HistorySchema, Fee, Notification. Một số BookRepository, BorrowService và FeeService vẫn có lazy compatibility guards; giữ chúng khi thay đổi workflow tương ứng.

MainWindow nạp notification center khi được mở và dispose ViewModel khi đóng.

## Mượn và trả

BorrowViewModel có thể preview trạng thái lựa chọn; BorrowService vẫn kiểm tra lại điều kiện trong transaction. BorrowService khóa/kiểm tra quyền mượn, policy, đầu sách và bản sách; claim chính xác BookCopy còn Available bằng cập nhật có điều kiện; sau đó ghi BorrowRecord, circulation audit và phí mượn nếu có. Các thay đổi commit cùng nhau. Thông báo nghiệp vụ được phát sau commit.

ReturnViewModel gọi qua IReturnCirculationService, interface do BorrowService triển khai. ReturnViewModel dùng IUserDialogService; MessageBoxDialogService là adapter UI hiện tại. Return/Lost cập nhật phiếu, trạng thái đúng BookCopy, audit và các phí liên quan trong cùng transaction do BorrowService sở hữu.

Borrow và Return preview ở UI không thay thế kiểm tra cuối trong transaction. Legacy loan chưa gắn bản sách cần được đối chiếu barcode trước khi map; không tự gán ngẫu nhiên.

## Phí, lịch sử và thông báo

- FeeService điều phối đánh giá/phát sinh phí, snapshot nguồn và persistence qua FeeRepository. Khi được gọi từ workflow mượn/trả, FeeService tham gia transaction do BorrowService sở hữu; các thao tác phí riêng do FeeService quản lý transaction.
- FeeCalculator chỉ tính toán từ tham số/policy, không truy cập database hay làm I/O. Fee lưu snapshot của giá trị và nguồn tại thời điểm phát sinh. Nguồn phí và khóa idempotency ngăn phát sinh/ghi nhận trùng.
- HistoryViewModel gọi HistoryService và HistoryRepository. Repository truy vấn/phân trang, có tương thích schema cũ, nhưng đường đọc không chạy migration DDL.
- Sự kiện nghiệp vụ được chuyển qua handler tới NotificationService và lưu bởi NotificationRepository. DueSoonNotificationChecker quét khi đăng nhập đã hoàn tất migration; khóa theo BorrowId và ngày đến hạn giúp tránh gửi lặp.

## Ranh giới và ngoại lệ hiện có

Đối với thay đổi mới, giữ hướng View → ViewModel → Service → Repository. Không đưa SQL vào View/ViewModel, không để Repository phụ thuộc UI, và không gọi MessageBox từ Service.

IUserDialogService là boundary được dùng bởi BorrowViewModel, ReturnViewModel, DashboardViewModel và MyAccountViewModel. Một số ViewModel màn hình cũ vẫn gọi MessageBox trực tiếp: AccountsViewModel, BooksViewModel, FeesViewModel và ReadersViewModel. Giữ tương tác View-specific trong View/code-behind; không coi các chỗ cũ này là pattern cho luồng mới.

AuthWindow là ngoại lệ điều phối khởi động được mô tả ở trên; lazy migration guards nằm ở Service/Repository theo nhu cầu tương thích. Không chuyển migration sang query path của History.

## Tương thích dữ liệu cũ

- Sau migration, BookCopies là nguồn sự thật cho tồn kho bản vật lý. Books.Quantity và Books.AvailableQuantity là snapshot/cột tương thích cũ; không dùng chúng để thay đổi circulation.
- BookCopyMigration backfill bản vật lý từ dữ liệu cũ. Copy/loan chưa xác minh giữ trạng thái cần rà soát cho tới khi được map rõ ràng.
- ReturnOutcome và HistorySchema migrations duy trì khả năng đọc dữ liệu cũ và các trường kết quả mới.
- Book.ReplacementValue là nguồn giá trị thay thế hiện tại. Fee giữ snapshot giá sách/giá mượn để giải thích khoản phí lịch sử.
- Fee source uniqueness, payment idempotency, circulation audit và một số lazy migration guards là cơ chế bảo toàn dữ liệu; không xóa chỉ vì không thấy caller đơn giản.
- Giữ lại các API tương thích BookCopyRepository.GetFirstAvailableCopyId và BookRepository.EnsureCopiesForLegacyBook(int, int, int) cho tới khi có bằng chứng chắc chắn chúng không còn consumer ngoài solution.

## Quy tắc thay đổi

Thêm màn hình vào View/ViewModel hiện hành, đưa workflow nghiệp vụ vào Service, truy cập database qua Repository, và đặt migration trong Data. Thêm test cùng vùng trách nhiệm; dùng unit test cho logic thuần và SQL integration test cho hành vi persistence. Không thêm folder layer mới chỉ để đổi tên architecture.
