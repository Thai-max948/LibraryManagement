using System;
using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services
{
    public sealed class ReaderEligibilityService
    {
        public const int BorrowLimit = 3;

        private readonly ReaderRepository _readerRepository;
        private readonly BorrowRepository _borrowRepository;
        private readonly IReaderFinancialStandingProvider _financialStandingProvider;
        private readonly TimeProvider _timeProvider;

        public ReaderEligibilityService()
            : this(new ReaderRepository(), new BorrowRepository())
        {
        }

        public ReaderEligibilityService(ReaderRepository readerRepository, BorrowRepository borrowRepository)
            : this(readerRepository, borrowRepository, new NoFinancialStandingProvider())
        {
        }

        public ReaderEligibilityService(ReaderRepository readerRepository, BorrowRepository borrowRepository,
            IReaderFinancialStandingProvider financialStandingProvider, TimeProvider? timeProvider = null)
        {
            _readerRepository = readerRepository;
            _borrowRepository = borrowRepository;
            _financialStandingProvider = financialStandingProvider;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public ReaderEligibilityResult CheckEligibility(int readerId)
        {
            var reader = _readerRepository.GetById(readerId);
            if (reader == null || reader.IsDeleted)
            {
                return Evaluate(reader, Array.Empty<BorrowRecord>());
            }

            var currentBorrowings = (_borrowRepository.GetBorrowingRecords() ?? new List<BorrowRecord>())
                .Where(record => record.ReaderId == readerId);
            var standing = _financialStandingProvider.GetStanding(readerId);
            return Evaluate(reader, currentBorrowings, financialStanding: standing);
        }

        public ReaderEligibilityResult CheckEligibility(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            var reader = _readerRepository.GetById(connection, transaction, readerId);
            if (reader == null || reader.IsDeleted)
                return Evaluate(reader, Array.Empty<BorrowRecord>());
            var records = _borrowRepository.GetEligibilityRecords(connection, transaction, readerId);
            var standing = _financialStandingProvider.GetStanding(connection, transaction, readerId);
            return Evaluate(reader, records, financialStanding: standing);
        }

        public ReaderEligibilityResult Evaluate(
            Reader? reader,
            IEnumerable<BorrowRecord> currentBorrowings,
            DateTime? asOf = null,
            ReaderFinancialStanding? financialStanding = null)
        {
            var activeLoans = currentBorrowings
                .Where(record => string.Equals(record.Status, "Borrowing", StringComparison.OrdinalIgnoreCase))
                .ToList();
            DateTime evaluationDate = (asOf ?? _timeProvider.GetLocalNow().DateTime).Date;
            int overdueLoans = activeLoans.Count(record =>
                record.DueDate != default && record.DueDate.Date < evaluationDate);
            var reasons = new List<string>();

            if (reader == null || reader.IsDeleted)
            {
                reasons.Add("Độc giả không tồn tại hoặc đã bị xóa.");
            }
            else if (!string.Equals(reader.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(reader.IsSuspended
                    ? "Độc giả đang bị tạm khóa (Suspended)."
                    : reader.IsInactive
                        ? "Độc giả đã ngừng hoạt động (Inactive)."
                        : "Độc giả không ở trạng thái hoạt động.");
            }

            if (reader?.MembershipExpiresOn?.Date < evaluationDate)
                reasons.Add("Thẻ thư viện đã hết hạn.");

            if (overdueLoans > 0)
            {
                reasons.Add($"Có {overdueLoans} sách đang quá hạn.");
            }

            if (financialStanding?.BlocksBorrowing == true)
                reasons.Add(financialStanding.OutstandingAmount > 0
                    ? $"Còn khoản phí chưa thanh toán: {financialStanding.OutstandingAmount:N0}đ."
                    : "Còn nghĩa vụ tài chính chưa được xử lý.");

            if (activeLoans.Count >= BorrowLimit)
            {
                reasons.Add($"Đã đạt giới hạn {BorrowLimit} sách đang mượn.");
            }

            return new ReaderEligibilityResult
            {
                IsEligible = reasons.Count == 0,
                Reasons = reasons,
                CurrentLoans = activeLoans.Count,
                OverdueLoans = overdueLoans,
                BorrowLimit = BorrowLimit,
                MembershipExpiresOn = reader?.MembershipExpiresOn,
                OutstandingAmount = financialStanding?.OutstandingAmount ?? 0
            };
        }
    }
}
