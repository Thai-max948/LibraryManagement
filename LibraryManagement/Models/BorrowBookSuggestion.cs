namespace LibraryManagement.Models;

public sealed record BorrowBookSuggestion(
    int BookId,
    string Title,
    string Author,
    string Category,
    string? Isbn,
    int AvailableCopyCount);
