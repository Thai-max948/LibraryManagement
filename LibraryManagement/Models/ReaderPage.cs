using System;
using System.Collections.Generic;

namespace LibraryManagement.Models
{
    public sealed class ReaderPage
    {
        public IReadOnlyList<Reader> Items { get; init; } = Array.Empty<Reader>();
        public int TotalCount { get; init; }
        public int PageNumber { get; init; }
        public int PageSize { get; init; }
        public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed class ReaderBorrowHistoryItem
    {
        public int BorrowId { get; init; }
        public string BookTitle { get; init; } = string.Empty;
        public DateTime BorrowDate { get; init; }
        public DateTime DueDate { get; init; }
        public DateTime? ReturnDate { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    public sealed class ReaderProfile
    {
        public Reader Reader { get; init; } = new();
        public ReaderEligibilityResult Eligibility { get; init; } = new();
        public IReadOnlyList<ReaderBorrowHistoryItem> BorrowingHistory { get; init; } = Array.Empty<ReaderBorrowHistoryItem>();
        public int CurrentlyBorrowing { get; init; }
        public int TotalBorrowed { get; init; }
        public int OverdueCount { get; init; }
    }
}
