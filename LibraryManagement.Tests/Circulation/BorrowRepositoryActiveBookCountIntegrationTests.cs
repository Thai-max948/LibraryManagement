using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class BorrowRepositoryActiveBookCountIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void CountActiveBorrowsByBook_MatchesGetBorrowingRecordsProjection()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        var borrows = new BorrowService();
        var repository = new BorrowRepository();
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Active book count reader",
            StudentId = "COUNT-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });

        int copyBasedBookId = AddBook(books, "copy-based");
        int secondActiveBookId = AddBook(books, "second-active");
        int returnedBookId = AddBook(books, "returned-only");
        int noLoanBookId = AddBook(books, "no-loans");

        borrows.BorrowBook(readerId, copies.GetCopies(copyBasedBookId).Single().CopyId);
        borrows.BorrowBook(readerId, copies.GetCopies(secondActiveBookId).Single().CopyId);
        int returnedLoanId = borrows.BorrowBook(readerId,
            copies.GetCopies(returnedBookId).Single().CopyId);
        borrows.ReturnBook(returnedLoanId, ReturnCondition.Normal);
        AddLegacyActiveLoan(copyBasedBookId, readerId);

        var oldPath = repository.GetBorrowingRecords();

        Assert.Equal(2, repository.CountActiveBorrowsByBook(copyBasedBookId));
        Assert.Equal(1, repository.CountActiveBorrowsByBook(secondActiveBookId));
        Assert.Equal(0, repository.CountActiveBorrowsByBook(returnedBookId));
        Assert.Equal(0, repository.CountActiveBorrowsByBook(noLoanBookId));
        foreach (int bookId in new[]
        {
            copyBasedBookId, secondActiveBookId, returnedBookId, noLoanBookId
        })
        {
            Assert.Equal(oldPath.Count(record => record.BookId == bookId),
                repository.CountActiveBorrowsByBook(bookId));
        }
    }

    private static int AddBook(BookService service, string suffix) => service.AddBook(new Book
    {
        Title = "Active count " + suffix + " " + Guid.NewGuid().ToString("N"),
        Author = "Repository test",
        PublishYear = 2026,
        Quantity = 1
    });

    private static void AddLegacyActiveLoan(int bookId, int readerId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BorrowRecords
                (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status, CopyId)
            VALUES
                (@BookId, @ReaderId, @BorrowDate, @DueDate, NULL, N'Borrowing', NULL);", connection);
        command.Parameters.Add("@BookId", System.Data.SqlDbType.Int).Value = bookId;
        command.Parameters.Add("@ReaderId", System.Data.SqlDbType.Int).Value = readerId;
        command.Parameters.Add("@BorrowDate", System.Data.SqlDbType.DateTime).Value = DateTime.Now;
        command.Parameters.Add("@DueDate", System.Data.SqlDbType.DateTime).Value = DateTime.Now.AddDays(14);
        command.ExecuteNonQuery();
    }
}
