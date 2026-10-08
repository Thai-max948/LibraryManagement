namespace LibraryManagement.Models;

/// <summary>A single SQL-paged result for the current-loans panel.</summary>
public sealed record CurrentBorrowingPage(
    IReadOnlyList<CurrentBorrowingRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 1
        : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}
