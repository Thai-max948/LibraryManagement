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
        DateTime filterDate = new(2099, 1, 10);
        int returnedOnFilterDateButBorrowedEarlierId = InsertReturnedHistoryRecord(
            bookId, readerId, originalName, originalTitle, filterDate.AddDays(-5), filterDate);
        int borrowedOnFilterDateId = InsertReturnedHistoryRecord(
            bookId, readerId, originalName, originalTitle, filterDate, filterDate.AddDays(1));
        var filteredByBorrowDate = Assert.Single(history.GetPage(new HistoryQuery
        {
            SearchText = originalTitle,
            Status = "Returned",
            FromDate = filterDate,
            ToDate = filterDate
        }).Records);
        Assert.Equal(borrowedOnFilterDateId, filteredByBorrowDate.BorrowId);
        Assert.NotEqual(returnedOnFilterDateButBorrowedEarlierId, filteredByBorrowDate.BorrowId);
        Assert.Equal(filterDate, filteredByBorrowDate.BorrowDate.Date);
        Assert.Equal(filterDate.AddDays(1), filteredByBorrowDate.ReturnDate?.Date);
        var returnedByCirculation = Assert.Single(history.GetPage(new HistoryQuery
        {
            SearchText = originalTitle,
            Status = "Returned"
        }).Records, item => item.BorrowId == firstBorrowId);
        Assert.NotNull(returnedByCirculation.ReturnDate);
        Assert.Contains(returnedByCirculation.Events,
            item => item.EventType == CirculationAuditEventType.ReturnedNormal);

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

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void HistoryUsesSqlPagesOfEightForFortySevenMatchingRecords()
    {
        ReturnOutcomeMigration.Apply();
        CirculationAuditMigration.Apply();
        HistorySchemaMigration.Apply();

        string token = Guid.NewGuid().ToString("N");
        string readerName = "History page reader " + token;
        string bookTitle = "History page book " + token;
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = readerName,
            StudentId = "HP-" + token,
            Phone = "0901234567"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = bookTitle,
            Author = "History paging test",
            PublishYear = 2026,
            Quantity = 0
        });

        var insertedIds = new List<int>();
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            for (int index = 0; index < 47; index++)
            {
                DateTime borrowDate = new DateTime(2026, 1, 1).AddDays(index);
                using var command = new SqlCommand(@"
                    INSERT INTO dbo.BorrowRecords
                        (BookId, ReaderId, BorrowDate, DueDate, Status, ReaderNameSnapshot, BookTitleSnapshot)
                    OUTPUT INSERTED.BorrowId
                    VALUES (@BookId, @ReaderId, @BorrowDate, @DueDate, 'Borrowing', @ReaderName, @BookTitle);", connection);
                command.Parameters.AddWithValue("@BookId", bookId);
                command.Parameters.AddWithValue("@ReaderId", readerId);
                command.Parameters.AddWithValue("@BorrowDate", borrowDate);
                command.Parameters.AddWithValue("@DueDate", borrowDate.AddDays(7));
                command.Parameters.AddWithValue("@ReaderName", readerName);
                command.Parameters.AddWithValue("@BookTitle", bookTitle);
                insertedIds.Add(Convert.ToInt32(command.ExecuteScalar()));
            }
        }

        var history = new HistoryService();
        var firstPage = history.GetPage(new HistoryQuery { SearchText = bookTitle, PageNumber = 1 });
        var lastPage = history.GetPage(new HistoryQuery { SearchText = bookTitle, PageNumber = 6 });

        Assert.Equal(47, firstPage.TotalCount);
        Assert.Equal(6, firstPage.TotalPages);
        Assert.Equal(8, firstPage.PageSize);
        Assert.Equal(8, firstPage.Records.Count);
        Assert.Equal(7, lastPage.Records.Count);
        Assert.Equal(insertedIds.TakeLast(8).Reverse(), firstPage.Records.Select(record => record.BorrowId));
        Assert.Equal(insertedIds.Take(7).Reverse(), lastPage.Records.Select(record => record.BorrowId));
        Assert.All(firstPage.Records.Concat(lastPage.Records), record =>
        {
            Assert.Equal(readerName, record.ReaderName);
            Assert.Equal(bookTitle, record.BookTitle);
        });
    }

    private static int InsertReturnedHistoryRecord(
        int bookId,
        int readerId,
        string readerName,
        string bookTitle,
        DateTime borrowDate,
        DateTime returnDate)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BorrowRecords
                (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status, ReaderNameSnapshot, BookTitleSnapshot)
            OUTPUT INSERTED.BorrowId
            VALUES (@BookId, @ReaderId, @BorrowDate, @DueDate, @ReturnDate, 'Returned', @ReaderName, @BookTitle);",
            connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@ReaderId", readerId);
        command.Parameters.AddWithValue("@BorrowDate", borrowDate);
        command.Parameters.AddWithValue("@DueDate", borrowDate.AddDays(7));
        command.Parameters.AddWithValue("@ReturnDate", returnDate);
        command.Parameters.AddWithValue("@ReaderName", readerName);
        command.Parameters.AddWithValue("@BookTitle", bookTitle);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
