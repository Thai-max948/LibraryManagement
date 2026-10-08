namespace LibraryManagement.Models;

/// <summary>A bounded, display-ready projection of one active borrowing.</summary>
public sealed class CurrentBorrowingRow
{
    public int BorrowId { get; init; }
    public int ReaderId { get; init; }
    public string ReaderName { get; init; } = string.Empty;
    public int BookId { get; init; }
    public string BookTitle { get; init; } = string.Empty;
    public int? BookCopyId { get; init; }
    public string? Barcode { get; init; }
    public DateTime BorrowDate { get; init; }
    public DateTime DueDate { get; init; }
}
