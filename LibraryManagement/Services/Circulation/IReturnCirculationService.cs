using LibraryManagement.Models;

namespace LibraryManagement.Services;

public interface IReturnCirculationService
{
    BorrowRecord FindActiveReturnByBarcode(string barcode);
    int GetCurrentLateDays(DateTime dueDate);
    List<BorrowRecord> GetBorrowingBooks();
    List<ActiveReturnLoanRow> SearchActiveLoansForReturn(string? searchText, int limit = 100);
    ReturnResult ReturnBook(int borrowId, ReturnCondition condition, string? note = null);
    ReturnResult ReturnBookByBarcode(string barcode, ReturnCondition condition, string? note = null);
    ReturnResult MarkAsLost(int borrowId, string? note = null);
    void LinkLegacyBorrowToCopy(int borrowId, int bookCopyId);
}
