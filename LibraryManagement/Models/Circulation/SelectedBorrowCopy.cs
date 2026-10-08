namespace LibraryManagement.Models;

public sealed record SelectedBorrowCopy(int CopyId, int BookId, string Title, string Barcode);
