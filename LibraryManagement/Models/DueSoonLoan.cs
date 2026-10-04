namespace LibraryManagement.Models;

public sealed record DueSoonLoan(int BorrowId, int ReaderId, string BookTitle, DateTime DueDate);
