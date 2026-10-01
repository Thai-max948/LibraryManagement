using System;
using System.Collections.Generic;

namespace LibraryManagement.Models
{
    public sealed class ReaderEligibilityResult
    {
        public bool IsEligible { get; init; }
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
        public int CurrentLoans { get; init; }
        public int OverdueLoans { get; init; }
        public int BorrowLimit { get; init; }

        public string StatusText => IsEligible ? "Eligible ✓" : "Not Eligible ✕";
        public string LoanSummary => $"Đang mượn {CurrentLoans}/{BorrowLimit} sách • Quá hạn {OverdueLoans}";
        public string ReasonSummary => IsEligible
            ? "Độc giả hiện đủ điều kiện mượn sách."
            : string.Join(" • ", Reasons);
    }
}
