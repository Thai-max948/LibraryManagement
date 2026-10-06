namespace LibraryManagement.Models;

public sealed class HistoryQuery
{
    public string SearchText { get; init; } = string.Empty;
    public string Status { get; init; } = "All";
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed class HistoryRecordDto
{
    public int BorrowId { get; init; }
    public int ReaderId { get; init; }
    public string ReaderName { get; init; } = string.Empty;
    public bool IsReaderDeleted { get; init; }
    public int BookId { get; init; }
    public string BookTitle { get; init; } = string.Empty;
    public int? BookCopyId { get; init; }
    public string? Barcode { get; init; }
    public DateTime BorrowDate { get; init; }
    public DateTime DueDate { get; init; }
    public DateTime? ReturnDate { get; init; }
    public DateTime? LostDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }
    public int OverdueDays { get; init; }
    public DateTime ActionDate { get; init; }
    public bool IsLegacyRecord { get; init; }
    public IReadOnlyList<CirculationAuditEvent> Events { get; set; } = Array.Empty<CirculationAuditEvent>();
}

public sealed class HistoryPage
{
    public IReadOnlyList<HistoryRecordDto> Records { get; init; } = Array.Empty<HistoryRecordDto>();
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int OverdueCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));
}
