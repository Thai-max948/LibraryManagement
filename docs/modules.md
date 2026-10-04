# Bản đồ module

ViewModels hiện nằm trong một folder chung; bảng dưới đây ghi các View/ViewModel chính và dependency nghiệp vụ. Composition được tạo thủ công qua MainViewModel, View code-behind hoặc XAML.

| Module | View / ViewModel | Service chính | Repository chính | Phụ thuộc đáng chú ý |
|---|---|---|---|---|
| Books | BooksView / BooksViewModel; BookDetailDialog / BookDetailViewModel | BookService, BookCopyService, InventoryCheckService | BookRepository, BookCopyRepository, InventoryCheckRepository | BookCopies quản lý bản vật lý; hỗ trợ barcode, archive và đối soát tồn kho. |
| Readers | ReadersView / ReadersViewModel; các reader dialogs | ReaderService, ReaderEligibilityService | ReaderRepository, BorrowRepository | Shared Student/External reader lifecycle và eligibility. |
| Borrow | BorrowView / BorrowViewModel | BorrowService, ReaderEligibilityService, LoanPolicyService | BorrowRepository, BookCopyRepository, ReaderRepository, CirculationAuditRepository | Claim bản sách và ghi phiếu trong transaction. |
| Return | ReturnView / ReturnViewModel | IReturnCirculationService (BorrowService), BookCopyService, FeeService | BorrowRepository, BookCopyRepository, CirculationAuditRepository | Barcode lookup, disposition, legacy mapping và phí cùng transaction. |
| Fees | FeesView / FeesViewModel | IFeeService (FeeService) | FeeRepository | FeeCalculator thuần; snapshot, thanh toán, waive/cancel và idempotency. |
| History | HistoryView / HistoryViewModel | HistoryService | HistoryRepository | Query/phân trang; tương thích schema cũ, không migration DDL trên đường đọc. |
| Notifications | NotificationCenterView, NotificationToastView / NotificationViewModel | NotificationService, DueSoonNotificationChecker | NotificationRepository, BorrowRepository | Event handlers, persistence và due-soon scan sau migration lúc đăng nhập. |
| Authentication | AuthWindow, LoginView, RegisterView / AuthViewModel | AuthService | UserRepository | AuthWindow phát sự kiện đăng nhập thành công và điều phối startup migration. |
| Accounts | AccountsView / AccountsViewModel; MyAccountView / MyAccountViewModel | UserService | UserRepository | Phân quyền và cập nhật tài khoản cá nhân. |
| Dashboard | DashboardView / DashboardViewModel | BookService, ReaderService, BorrowService | BookRepository, ReaderRepository, BorrowRepository | Tổng hợp snapshot thống kê và hoạt động gần đây. |

## Giao cắt giữa module

- BorrowService triển khai IReturnCirculationService để ReturnViewModel không phụ thuộc các chi tiết nội bộ của return workflow.
- BorrowViewModel, ReturnViewModel, DashboardViewModel và MyAccountViewModel nhận IUserDialogService; Views cung cấp MessageBoxDialogService.
- FeeService được gọi từ BorrowService khi fee phải commit cùng circulation; thao tác phí độc lập vẫn thuộc FeeService.
- NotificationRuntime là composition tĩnh cho dispatcher và due-soon checker; App khởi tạo runtime một lần.
