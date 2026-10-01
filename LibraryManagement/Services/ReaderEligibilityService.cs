using System;
using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public sealed class ReaderEligibilityService
    {
        public const int BorrowLimit = 3;

        private readonly ReaderRepository _readerRepository;
        private readonly BorrowRepository _borrowRepository;

        public ReaderEligibilityService()
            : this(new ReaderRepository(), new BorrowRepository())
        {
        }

        public ReaderEligibilityService(ReaderRepository readerRepository, BorrowRepository borrowRepository)
        {
            _readerRepository = readerRepository;
            _borrowRepository = borrowRepository;
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
            return Evaluate(reader, currentBorrowings);
        }

        public ReaderEligibilityResult Evaluate(
            Reader? reader,
            IEnumerable<BorrowRecord> currentBorrowings,
            DateTime? asOf = null)
        {
            var activeLoans = currentBorrowings
                .Where(record => string.Equals(record.Status, "Borrowing", StringComparison.OrdinalIgnoreCase))
                .ToList();
            DateTime evaluationDate = (asOf ?? DateTime.Now).Date;
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
                    : "Độc giả không ở trạng thái hoạt động.");
            }

            if (overdueLoans > 0)
            {
                reasons.Add($"Có {overdueLoans} sách đang quá hạn.");
            }

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
                BorrowLimit = BorrowLimit
            };
        }
    }
}
