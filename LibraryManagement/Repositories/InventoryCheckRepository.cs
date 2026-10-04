using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class InventoryCheckRepository
{
    public virtual List<InventoryCheckRow> GetReport()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!new BookCopyRepository().HasSchema(connection, transaction))
            throw new InvalidOperationException("Cần cập nhật BookCopy trước khi kiểm tra tồn kho.");

        // Read in circulation lock order; no UPDATE, reconciliation never repairs data.
        var books = new List<(int Id, string Title, int Quantity, int Available)>();
        using (var command = new SqlCommand("SELECT BookId, Title, Quantity, AvailableQuantity FROM dbo.Books ORDER BY BookId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) books.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        var copies = new List<BookCopy>();
        using (var command = new SqlCommand("SELECT CopyId, BookId, Status FROM dbo.BookCopies ORDER BY BookId, CopyId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) copies.Add(new BookCopy { CopyId = reader.GetInt32(0), BookId = reader.GetInt32(1), Status = reader.GetString(2) });
        var loans = new List<BorrowRecord>();
        using (var command = new SqlCommand("SELECT BookId, CopyId, Status FROM dbo.BorrowRecords WHERE Status = 'Borrowing' ORDER BY BookId, CopyId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) loans.Add(new BorrowRecord { BookId = reader.GetInt32(0), BookCopyId = reader.IsDBNull(1) ? null : reader.GetInt32(1), Status = reader.GetString(2) });
        transaction.Commit();
        return books.Select(book => Evaluate(book.Id, book.Title, book.Quantity, book.Available, copies, loans)).ToList();
    }

    public static InventoryCheckRow Evaluate(int bookId, string title, int storedQuantity, int storedAvailable,
        IEnumerable<BookCopy> copies, IEnumerable<BorrowRecord> loans)
    {
        var bookCopies = copies.Where(copy => copy.BookId == bookId).ToList();
        var bookLoans = loans.Where(loan => loan.BookId == bookId && loan.Status == "Borrowing").ToList();
        var inventory = BookCopyInventory.FromCopies(bookCopies);
        int inconsistent = bookCopies.Count(copy =>
        {
            int active = loans.Count(loan => loan.Status == "Borrowing" && loan.BookCopyId == copy.CopyId);
            return copy.Status == BookCopyStatuses.Borrowed ? active != 1 : active != 0;
        });
        inconsistent += bookLoans.Count(loan => loan.BookCopyId.HasValue &&
            !bookCopies.Any(copy => copy.CopyId == loan.BookCopyId));
        return new InventoryCheckRow
        {
            BookId = bookId, Title = title, StoredQuantity = storedQuantity, StoredAvailable = storedAvailable,
            TotalCurrent = inventory.TotalCopies, AvailableCurrent = inventory.Available,
            Borrowed = inventory.Borrowed, DamagedUnderRepair = inventory.DamagedUnderRepair,
            Lost = inventory.Lost, Retired = inventory.Retired,
            DerivedQuantity = inventory.ActiveCopies, DerivedAvailable = inventory.Available,
            UnresolvedLegacyLoans = bookLoans.Count(loan => !loan.BookCopyId.HasValue), InconsistentCopies = inconsistent
        };
    }
}
