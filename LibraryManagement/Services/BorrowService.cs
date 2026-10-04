using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class BorrowService : IReturnCirculationService
    {
        private const string StatusBorrowing = "Borrowing";

        private readonly BookRepository _bookRepo;
        private readonly BorrowRepository _borrowRepo;
        private readonly BookCopyRepository _copyRepo;
        private readonly ReaderRepository _readerRepo;
        private readonly ReaderEligibilityService _eligibilityService;
        private readonly LoanPolicyService _loanPolicyService;
        private readonly CirculationAuditRepository _auditRepo;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly TimeProvider _timeProvider;
        private readonly IFeeService? _feeService;
        private readonly object _feeSchemaGate = new();
        private bool _feeSchemaReady;

        public BorrowService() : this(new BookRepository(), new BorrowRepository(), new ReaderRepository(), new BookCopyRepository(),
            new FeeFinancialStandingProvider(), new CirculationAuditRepository(), new AuthServiceCurrentUserContext(),
            timeProvider: null, feeService: new FeeService())
        {
        }

        public BorrowService(BookRepository bookRepo, BorrowRepository borrowRepo, ReaderRepository readerRepo)
            : this(bookRepo, borrowRepo, readerRepo, new BookCopyRepository())
        {
        }

        public BorrowService(BookRepository bookRepo, BorrowRepository borrowRepo, ReaderRepository readerRepo, BookCopyRepository copyRepo)
            : this(bookRepo, borrowRepo, readerRepo, copyRepo, new NoFinancialStandingProvider())
        {
        }

        public BorrowService(BookRepository bookRepo, BorrowRepository borrowRepo, ReaderRepository readerRepo,
            BookCopyRepository copyRepo, IReaderFinancialStandingProvider financialStandingProvider)
            : this(bookRepo, borrowRepo, readerRepo, copyRepo, financialStandingProvider,
                new CirculationAuditRepository(), new AuthServiceCurrentUserContext())
        {
        }

        public BorrowService(BookRepository bookRepo, BorrowRepository borrowRepo, ReaderRepository readerRepo,
            BookCopyRepository copyRepo, IReaderFinancialStandingProvider financialStandingProvider,
            CirculationAuditRepository auditRepo, ICurrentUserContext currentUserContext, TimeProvider? timeProvider = null,
            IFeeService? feeService = null)
        {
            _bookRepo = bookRepo;
            _borrowRepo = borrowRepo;
            _copyRepo = copyRepo;
            _readerRepo = readerRepo;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _eligibilityService = new ReaderEligibilityService(readerRepo, borrowRepo, financialStandingProvider, _timeProvider);
            _loanPolicyService = new LoanPolicyService();
            _auditRepo = auditRepo;
            _currentUserContext = currentUserContext;
            _feeService = feeService;
        }

        public bool CanBorrow(int readerId, int bookId, out string reason)
        {
            var eligibility = _eligibilityService.CheckEligibility(readerId);
            if (!eligibility.IsEligible)
            {
                reason = eligibility.ReasonSummary;
                return false;
            }

            var book = _bookRepo.GetById(bookId);
            if (book == null)
            {
                reason = "Sách không tồn tại.";
                return false;
            }
            if (book.Status == BookStatuses.Archived)
            {
                reason = "Đầu sách đã được lưu trữ.";
                return false;
            }
            if (book.AvailableQuantity <= 0)
            {
                reason = "Sách đã hết, không thể mượn.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public ReaderEligibilityResult GetReaderEligibility(int readerId)
        {
            EnsureFeeDependencies();
            return _eligibilityService.CheckEligibility(readerId);
        }

        public int GetCurrentLateDays(DateTime dueDate) =>
            _feeService?.GetLateDays(dueDate, _timeProvider.GetLocalNow().DateTime) ?? 0;

        public int BorrowBook(int readerId, int bookCopyId)
        {
            EnsureFeeDependencies();
            var eligibility = _eligibilityService.CheckEligibility(readerId);
            if (!eligibility.IsEligible)
            {
                throw new BusinessRuleException(eligibility.ReasonSummary);
            }
            var actor = RequireAuditActor();

            LoanPolicyMigration.Apply();
            ReturnOutcomeMigration.Apply();
            CirculationAuditMigration.Apply();
            HistorySchemaMigration.Apply();

            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                if (!_copyRepo.HasSchema(conn, tran))
                    throw new BusinessRuleException("Cần chạy cập nhật Book Copy trước khi lập phiếu mượn.");

                // One Reader lock serializes concurrent limit checks for this reader.
                // Lock order: Reader, eligibility, policy, shared Book lifecycle guard, BookCopy, BorrowRecord, Audit.
                if (!_readerRepo.LockForBorrow(conn, tran, readerId))
                    throw new BusinessRuleException("Độc giả không còn khả dụng.");
                var authoritativeEligibility = _eligibilityService.CheckEligibility(conn, tran, readerId);
                if (!authoritativeEligibility.IsEligible)
                    throw new BusinessRuleException(authoritativeEligibility.ReasonSummary);

                var currentReader = _readerRepo.GetById(conn, tran, readerId)!;
                var policy = _loanPolicyService.GetPolicyFor(conn, tran, currentReader.ReaderType);
                var actualBorrowDate = _timeProvider.GetLocalNow().DateTime;
                var dueDate = LoanPolicyService.CalculateDueDate(policy, actualBorrowDate);

                // Read the parent ID, then claim the exact scanned copy with a conditional UPDATE.
                var book = _copyRepo.GetById(conn, tran, bookCopyId);
                if (book == null)
                    throw new BusinessRuleException("Bản sách không tồn tại.");

                int bookId = book.BookId;
                var parentStatus = _bookRepo.GetCirculationStatus(conn, tran, bookId);
                if (parentStatus == null)
                {
                    throw new BusinessRuleException("Sách không tồn tại.");
                }
                if (parentStatus == BookStatuses.Archived)
                    throw new BusinessRuleException("Đầu sách đã được lưu trữ, không thể mượn.");
                if (!_copyRepo.TryClaimForBorrow(conn, tran, bookCopyId))
                    throw new BusinessRuleException("Bản sách này không còn khả dụng. Có thể vừa được mượn hoặc trạng thái đã thay đổi.");

                var record = new BorrowRecord
                {
                    BookId = bookId,
                    BookCopyId = bookCopyId,
                    ReaderId = readerId,
                    BorrowDate = actualBorrowDate,
                    DueDate = dueDate,
                    LoanPeriodDaysApplied = policy.LoanPeriodDays,
                    ReturnDate = null,
                    Status = StatusBorrowing
                };
                int borrowId = _borrowRepo.Add(conn, tran, record);
                _auditRepo.Add(conn, tran, CreateAuditEvent(borrowId, CirculationAuditEventType.BorrowCreated,
                    actor, actualBorrowDate, bookCopyId, null));
                var borrowFee = _feeService?.CreateBorrowFee(conn, tran, borrowId);

                tran.Commit();
                NotificationEvents.PublishAfterSuccess(new(BusinessAction.BorrowCreated, borrowId.ToString()));
                if (borrowFee != null)
                    NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeeCreated, borrowFee.FeeId.ToString()));
                return borrowId;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public virtual BorrowRecord FindActiveReturnByBarcode(string barcode)
        {
            EnsureFeeDependencies();
            string normalizedBarcode = NormalizeBarcode(barcode);
            var copy = _copyRepo.GetByBarcode(normalizedBarcode);
            if (copy == null)
                throw new BusinessRuleException("Barcode không tồn tại.");
            if (copy.Status != BookCopyStatuses.Borrowed)
                throw new BusinessRuleException("Bản sách này không đang được mượn.");
            return _borrowRepo.GetActiveByCopyId(copy.CopyId)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu mượn đang hoạt động của barcode này.");
        }

        public ReturnResult ReturnBook(int borrowId, ReturnCondition condition, string? note = null) =>
            ReturnBookCore(borrowId, condition, note, expectedBarcode: null);

        public ReturnResult ReturnBookByBarcode(string barcode, ReturnCondition condition, string? note = null)
        {
            EnsureFeeDependencies();
            string normalizedBarcode = NormalizeBarcode(barcode);
            var copy = _copyRepo.GetByBarcode(normalizedBarcode);
            if (copy == null) throw new BusinessRuleException("Barcode không tồn tại.");
            if (copy.Status != BookCopyStatuses.Borrowed)
                throw new BusinessRuleException("Bản sách này không đang được mượn.");
            var loan = _borrowRepo.GetActiveByCopyId(copy.CopyId)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu mượn đang hoạt động của barcode này.");
            return ReturnBookCore(loan.BorrowId, condition, note, normalizedBarcode);
        }

        private static string NormalizeBarcode(string? barcode)
        {
            string exactValue = barcode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exactValue) || exactValue.Length > 100 || exactValue.Any(char.IsControl))
                throw new BusinessRuleException("Vui lòng nhập barcode hợp lệ (tối đa 100 ký tự).");
            return exactValue;
        }

        private ReturnResult ReturnBookCore(int borrowId, ReturnCondition condition, string? note, string? expectedBarcode)
        {
            if (borrowId <= 0) throw new BusinessRuleException("Mã phiếu mượn không hợp lệ.");
            if (!Enum.IsDefined(condition)) throw new BusinessRuleException("Tình trạng trả sách không hợp lệ.");
            note = NormalizeNote(note);
            if ((condition is ReturnCondition.Damaged or ReturnCondition.NeedsRepair) && note == null)
                throw new BusinessRuleException("Vui lòng ghi chú tình trạng sách bị hỏng hoặc cần sửa chữa.");
            var actor = RequireAuditActor();
            EnsureFeeDependencies();
            ReturnOutcomeMigration.Apply();
            CirculationAuditMigration.Apply();
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                if (!_copyRepo.HasSchema(conn, tran))
                    throw new BusinessRuleException("Cần cập nhật Book Copy trước khi ghi nhận trả sách.");
                var initial = _borrowRepo.GetById(conn, tran, borrowId)
                    ?? throw new BusinessRuleException("Bản ghi mượn không tồn tại.");
                if (!initial.BookCopyId.HasValue)
                    throw new BusinessRuleException("Phiếu mượn cũ chưa được gắn với bản sách cụ thể. Hãy đối chiếu barcode trước khi xử lý.");

                if (!_readerRepo.ExistsForCirculation(conn, tran, initial.ReaderId))
                    throw new BusinessRuleException("Độc giả của phiếu mượn không tồn tại.");
                if (_bookRepo.GetCirculationStatus(conn, tran, initial.BookId) == null)
                    throw new BusinessRuleException("Đầu sách của phiếu mượn không tồn tại.");

                var copy = expectedBarcode == null
                    ? _copyRepo.GetByIdForReturn(conn, tran, initial.BookCopyId.Value)
                    : _copyRepo.GetByBarcode(conn, tran, expectedBarcode, lockForUpdate: true);
                var record = _borrowRepo.GetByIdForReturn(conn, tran, borrowId);
                if (record == null || record.Status != StatusBorrowing)
                    throw new BusinessRuleException("Phiếu mượn này đã được trả hoặc xử lý trước đó.");
                if (record.BookCopyId != initial.BookCopyId || record.ReaderId != initial.ReaderId ||
                    record.BookId != initial.BookId || copy == null || copy.CopyId != initial.BookCopyId.Value ||
                    copy.BookId != record.BookId || copy.Status != BookCopyStatuses.Borrowed ||
                    (expectedBarcode != null && !string.Equals(copy.Barcode, expectedBarcode, StringComparison.Ordinal)))
                    throw new BusinessRuleException("Bản sách không khớp phiếu mượn hoặc không còn ở trạng thái đang mượn.");

                var occurredAt = _timeProvider.GetLocalNow().DateTime;
                if (occurredAt < record.BorrowDate || record.DueDate < record.BorrowDate)
                    throw new BusinessRuleException("Ngày mượn hoặc hạn trả của phiếu không hợp lệ.");
                bool isLost = condition == ReturnCondition.Lost;
                bool closed = isLost
                    ? _borrowRepo.MarkAsLost(conn, tran, borrowId, occurredAt, note)
                    : _borrowRepo.MarkAsReturned(conn, tran, borrowId, occurredAt, condition, note);
                if (!closed) throw new BusinessRuleException("Phiếu mượn này đã được trả hoặc xử lý trước đó.");
                string targetStatus = condition switch
                {
                    ReturnCondition.Normal => BookCopyStatuses.Available,
                    ReturnCondition.Damaged => BookCopyStatuses.Damaged,
                    ReturnCondition.NeedsRepair => BookCopyStatuses.UnderRepair,
                    ReturnCondition.Lost => BookCopyStatuses.Lost,
                    _ => throw new BusinessRuleException("Tình trạng trả sách không hợp lệ.")
                };
                if (!_copyRepo.UpdateStatus(conn, tran, record.BookCopyId.Value, BookCopyStatuses.Borrowed, targetStatus))
                    throw new BusinessRuleException("Trạng thái bản sách đã thay đổi. Vui lòng tải lại trước khi trả.");
                var eventType = condition switch
                {
                    ReturnCondition.Normal => CirculationAuditEventType.ReturnedNormal,
                    ReturnCondition.Damaged => CirculationAuditEventType.ReturnedDamaged,
                    ReturnCondition.NeedsRepair => CirculationAuditEventType.ReturnedNeedsRepair,
                    ReturnCondition.Lost => CirculationAuditEventType.MarkedLost,
                    _ => throw new BusinessRuleException("Tình trạng trả sách không hợp lệ.")
                };
                var returnAudit = CreateAuditEvent(borrowId, eventType, actor, occurredAt, record.BookCopyId.Value, note);
                _auditRepo.Add(conn, tran, returnAudit);
                var result = ReturnResult.FromLoan(record, occurredAt, condition) with
                {
                    ReturnEventId = returnAudit.AuditEventId
                };
                var assessmentCreated = new List<Fee>();
                if (_feeService is not null)
                {
                    FeeAssessmentResult assessment = _feeService.AssessReturnFees(conn, tran, result);
                    assessmentCreated.AddRange(assessment.Created);
                    result = result with
                    {
                        LateDays = assessment.LateDays,
                        FeeWarnings = assessment.Warnings,
                        FeesCreated = assessment.Created.Count
                    };
                }
                tran.Commit();
                NotificationEvents.PublishAfterSuccess(new(
                    condition == ReturnCondition.Damaged ? BusinessAction.ReturnDamaged : BusinessAction.ReturnCompleted,
                    borrowId.ToString()));
                foreach (var fee in assessmentCreated)
                    NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeeCreated, fee.FeeId.ToString()));
                return result;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public ReturnResult MarkAsLost(int borrowId, string? note = null) =>
            ReturnBook(borrowId, ReturnCondition.Lost, note);

        private static string? NormalizeNote(string? note)
        {
            string? normalized = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (normalized?.Length > 500)
                throw new BusinessRuleException("Ghi chú không được dài quá 500 ký tự.");
            return normalized;
        }

        public void LinkLegacyBorrowToCopy(int borrowId, int bookCopyId)
        {
            var actor = RequireAuditActor();
            CirculationAuditMigration.Apply();
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                var record = _borrowRepo.GetById(conn, tran, borrowId);
                if (record == null || record.Status != StatusBorrowing)
                    throw new BusinessRuleException("Phiếu mượn đang xử lý không tồn tại.");
                if (record.BookCopyId.HasValue)
                    throw new BusinessRuleException("Phiếu mượn đã gắn với một bản sách.");

                _bookRepo.GetCirculationStatus(conn, tran, record.BookId);
                var copy = _copyRepo.GetById(conn, tran, bookCopyId);
                if (copy == null || copy.BookId != record.BookId)
                    throw new BusinessRuleException("Barcode được chọn không thuộc đầu sách của phiếu mượn.");
                bool canLink = copy.Status == BookCopyStatuses.Available ||
                    (copy.Status == BookCopyStatuses.UnderRepair && copy.Condition == "LegacyUnverified");
                if (!canLink)
                    throw new BusinessRuleException("Bản sách này không thể gắn với phiếu mượn cũ.");

                if (!_copyRepo.UpdateStatus(conn, tran, bookCopyId, copy.Status, BookCopyStatuses.Borrowed) ||
                    !_borrowRepo.LinkLegacyBorrowToCopy(conn, tran, borrowId, record.BookId, bookCopyId))
                    throw new BusinessRuleException("Phiếu hoặc bản sách vừa thay đổi. Vui lòng tải lại danh sách.");

                _copyRepo.ConfirmLegacyCopy(conn, tran, bookCopyId);
                _auditRepo.Add(conn, tran, CreateAuditEvent(borrowId, CirculationAuditEventType.LegacyCopyMapped,
                    actor, _timeProvider.GetLocalNow().DateTime, bookCopyId, null));
                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        private void EnsureFeeDependencies()
        {
            if (_feeService is null || _feeSchemaReady) return;
            lock (_feeSchemaGate)
            {
                if (_feeSchemaReady) return;
                _feeService.EnsureSchema();
                _feeSchemaReady = true;
            }
        }

        private User RequireAuditActor()
        {
            var actor = _currentUserContext.CurrentUser;
            if (actor == null || actor.Id <= 0)
                throw new BusinessRuleException("Bạn cần đăng nhập lại trước khi thực hiện thao tác mượn trả.");
            string name = string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessRuleException("Tài khoản hiện tại thiếu tên để ghi nhật ký thao tác.");
            return actor;
        }

        private static CirculationAuditEvent CreateAuditEvent(int borrowId, CirculationAuditEventType eventType,
            User actor, DateTime occurredAt, int? bookCopyId, string? note)
        {
            string actorName = string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username.Trim() : actor.FullName.Trim();
            return new CirculationAuditEvent
            {
                BorrowId = borrowId,
                EventType = eventType,
                ActorUserId = actor.Id,
                ActorNameSnapshot = actorName[..Math.Min(150, actorName.Length)],
                OccurredAt = occurredAt,
                BookCopyId = bookCopyId,
                Note = note
            };
        }

        public virtual List<CirculationAuditEvent> GetAuditEvents(IEnumerable<int> borrowIds)
        {
            CirculationAuditMigration.Apply();
            return _auditRepo.GetByBorrowIds(borrowIds);
        }

        public List<BorrowRecord> GetBorrowingBooks() => _borrowRepo.GetBorrowingRecords();

        public List<BorrowRecord> GetHistory(
            int? readerId = null,
            int? bookId = null,
            string? status = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            return _borrowRepo.GetHistory(readerId, bookId, status, fromDate, toDate);
        }
    }
}
