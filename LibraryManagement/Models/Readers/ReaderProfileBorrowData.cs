using System;
using System.Collections.Generic;

namespace LibraryManagement.Models;

/// <summary>
/// Bounded borrow-data projection used to build a reader profile without loading full history.
/// </summary>
public sealed class ReaderProfileBorrowData
{
    public IReadOnlyList<ReaderBorrowHistoryItem> RecentHistory { get; init; } = Array.Empty<ReaderBorrowHistoryItem>();
    public IReadOnlyList<BorrowRecord> EligibilityRecords { get; init; } = Array.Empty<BorrowRecord>();
    public int CurrentlyBorrowing { get; init; }
    public int TotalBorrowed { get; init; }
    public int OverdueCount { get; init; }
}
