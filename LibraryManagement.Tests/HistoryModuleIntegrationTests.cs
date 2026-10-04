using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class HistoryModuleIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void HistoryPagesByPhysicalCopySearchesDatabaseAndPreservesBorrowSnapshots()
    {
        const string originalName = "History snapshot reader";
        const string originalTitle = "History snapshot book";
        var readers = new ReaderService();
        var books = new BookService();
        int readerId = readers.AddReader(new Reader
        {
            FullName = originalName,
            StudentId = "HIST-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });
        int bookId = books.AddBook(new Book { Title = originalTitle, Author = "History test", PublishYear = 2026, Quantity = 0 });
        var copies = new BookCopyService();
        int firstCopyId = copies.AddCopy(bookId);
        int secondCopyId = copies.AddCopy(bookId);
        string firstBarcode = copies.GetCopies(bookId).Single(copy => copy.CopyId == firstCopyId).Barcode;
        string secondBarcode = copies.GetCopies(bookId).Single(copy => copy.CopyId == secondCopyId).Barcode;
        var circulation = new BorrowService();
        int firstBorrowId = circulation.BorrowBook(readerId, firstCopyId);
        int secondBorrowId = circulation.BorrowBook(readerId, secondCopyId);

        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                UPDATE dbo.Readers SET FullName = @NewName WHERE ReaderId = @ReaderId;
                UPDATE dbo.Books SET Title = @NewTitle WHERE BookId = @BookId;", connection);
            command.Parameters.AddWithValue("@NewName", "Renamed reader");
            command.Parameters.AddWithValue("@ReaderId", readerId);
            command.Parameters.AddWithValue("@NewTitle", "Renamed book");
            command.Parameters.AddWithValue("@BookId", bookId);
            command.ExecuteNonQuery();
        }

        var history = new HistoryService();
        var firstPage = history.GetPage(new HistoryQuery
        {
            SearchText = originalTitle,
            PageNumber = 1,
            PageSize = 1
        });
        var secondPage = history.GetPage(new HistoryQuery
        {
            SearchText = originalTitle,
            PageNumber = 2,
            PageSize = 1
        });

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Single(firstPage.Records);
        Assert.Single(secondPage.Records);
        var allRows = firstPage.Records.Concat(secondPage.Records).ToArray();
        Assert.Equal(new[] { firstBorrowId, secondBorrowId }.Order(), allRows.Select(row => row.BorrowId).Order());
        Assert.All(allRows, row =>
        {
            Assert.Equal(originalName, row.ReaderName);
            Assert.Equal(originalTitle, row.BookTitle);
            Assert.Contains(row.Barcode, new[] { firstBarcode, secondBarcode });
            Assert.Contains(row.Events, item => item.EventType == CirculationAuditEventType.BorrowCreated);
        });
        Assert.NotEqual(allRows[0].BookCopyId, allRows[1].BookCopyId);

        var barcodeMatch = Assert.Single(history.GetPage(new HistoryQuery { SearchText = firstBarcode }).Records);
        Assert.Equal(firstCopyId, barcodeMatch.BookCopyId);
        Assert.Equal(firstBarcode, barcodeMatch.Barcode);

        circulation.ReturnBook(firstBorrowId, ReturnCondition.Normal);
        var returned = Assert.Single(history.GetPage(new HistoryQuery
        {
            Status = "Returned",
            DateFilter = HistoryDateFilter.ReturnDate,
            FromDate = DateTime.Today,
            ToDate = DateTime.Today
        }).Records);
        Assert.Equal(firstBorrowId, returned.BorrowId);
        Assert.NotNull(returned.ReturnDate);
        Assert.Contains(returned.Events, item => item.EventType == CirculationAuditEventType.ReturnedNormal);

        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                INSERT INTO dbo.BorrowRecords (BookId, ReaderId, BorrowDate, DueDate, Status)
                VALUES (@BookId, @ReaderId, GETDATE(), DATEADD(day, 7, GETDATE()), 'Borrowing');
                SELECT CAST(SCOPE_IDENTITY() AS int);", connection);
            command.Parameters.AddWithValue("@BookId", bookId);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            _ = Convert.ToInt32(command.ExecuteScalar());
        }

        var legacy = Assert.Single(history.GetPage(new HistoryQuery
        {
            SearchText = "Renamed book",
            Status = "Borrowing"
        }).Records);
        Assert.Null(legacy.BookCopyId);
        Assert.Null(legacy.Barcode);
        Assert.True(legacy.IsLegacyRecord);
    }
}
