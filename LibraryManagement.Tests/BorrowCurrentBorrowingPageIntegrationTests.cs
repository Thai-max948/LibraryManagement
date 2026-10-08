using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class BorrowCurrentBorrowingPageIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void CurrentBorrowingsAreCountedPagedAndOrderedInSql()
    {
        // LibraryDB.sql seeds several active demo loans; isolate the paging cases in this disposable test database.
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                UPDATE dbo.BorrowRecords
                SET Status = N'Returned', ReturnDate = COALESCE(ReturnDate, GETDATE())
                WHERE Status = N'Borrowing' AND ReturnDate IS NULL;", connection);
            command.ExecuteNonQuery();
        }

        var repository = new BorrowRepository();
        var emptyPage = repository.GetCurrentBorrowingPage(new CurrentBorrowingPageQuery());
        Assert.Empty(emptyPage.Items);
        Assert.Equal(0, emptyPage.TotalCount);
        Assert.Equal(1, emptyPage.PageNumber);
        Assert.Equal(1, emptyPage.TotalPages);

        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Current loans paging reader",
            StudentId = "PAGE-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Current loans paging book " + Guid.NewGuid().ToString("N"),
            Author = "Repository test",
            PublishYear = 2026,
            Quantity = 1
        });

        var inserted = new List<(int BorrowId, DateTime BorrowDate)>();
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            for (int index = 0; index < 12; index++)
            {
                DateTime borrowDate = new DateTime(2099, 1, 1).AddDays(index / 3);
                using var command = new SqlCommand(@"
                    INSERT INTO dbo.BorrowRecords
                        (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status, CopyId)
                    VALUES
                        (@BookId, @ReaderId, @BorrowDate, @DueDate, NULL, N'Borrowing', NULL);
                    SELECT CAST(SCOPE_IDENTITY() AS int);", connection);
                command.Parameters.Add("@BookId", SqlDbType.Int).Value = bookId;
                command.Parameters.Add("@ReaderId", SqlDbType.Int).Value = readerId;
                command.Parameters.Add("@BorrowDate", SqlDbType.DateTime).Value = borrowDate;
                command.Parameters.Add("@DueDate", SqlDbType.DateTime).Value = borrowDate.AddDays(14);
                int borrowId = Convert.ToInt32(command.ExecuteScalar());
                inserted.Add((borrowId, borrowDate));
            }
        }

        int[] expectedIds = inserted
            .OrderByDescending(row => row.BorrowDate)
            .ThenByDescending(row => row.BorrowId)
            .Select(row => row.BorrowId)
            .ToArray();

        var first = repository.GetCurrentBorrowingPage(new CurrentBorrowingPageQuery(1));
        var second = repository.GetCurrentBorrowingPage(new CurrentBorrowingPageQuery(2));
        var third = repository.GetCurrentBorrowingPage(new CurrentBorrowingPageQuery(3));
        var outOfRange = repository.GetCurrentBorrowingPage(new CurrentBorrowingPageQuery(99));

        Assert.Equal(12, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(5, first.PageSize);
        Assert.Equal(5, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(2, third.Items.Count);
        Assert.Equal(expectedIds.Take(5), first.Items.Select(row => row.BorrowId));
        Assert.Equal(expectedIds.Skip(5).Take(5), second.Items.Select(row => row.BorrowId));
        Assert.Equal(expectedIds.Skip(10), third.Items.Select(row => row.BorrowId));
        Assert.Equal(3, outOfRange.PageNumber);
        Assert.Equal(expectedIds.Skip(10), outOfRange.Items.Select(row => row.BorrowId));
    }
}
