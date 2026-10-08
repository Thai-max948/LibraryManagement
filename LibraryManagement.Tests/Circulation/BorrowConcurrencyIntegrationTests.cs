using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BorrowConcurrencyIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task SameCopy_OnlyOneConcurrentBorrowSucceeds()
    {
        int copyId = NewCopy();
        int firstReader = NewReader();
        int secondReader = NewReader();
        Task<Exception?> Attempt(int readerId) => Task.Run(() =>
        {
            try
            {
                new BorrowService().BorrowBook(readerId, copyId);
                return null;
            }
            catch (Exception error) { return error; }
        });

        var results = await Task.WhenAll(Attempt(firstReader), Attempt(secondReader));
        Assert.Single(results, error => error == null);
        Assert.Single(results.OfType<BusinessRuleException>());
        Assert.Equal(1, ActiveLoans(copyId));
        Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void FailedInsert_RollsBackCopyClaim()
    {
        int copyId = NewCopy();
        var service = new BorrowService(new BookRepository(), new FailingBorrowRepository(),
            new ReaderRepository(), new BookCopyRepository());
        Assert.Throws<InvalidOperationException>(() =>
            service.BorrowBook(NewReader(), copyId));
        Assert.Equal(0, ActiveLoans(copyId));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void FailedAuditInsert_RollsBackBorrowAndCopyClaim()
    {
        int copyId = NewCopy();
        var service = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new NoFinancialStandingProvider(), new FailingAuditRepository(),
            new AuthServiceCurrentUserContext());

        Assert.Throws<InvalidOperationException>(() => service.BorrowBook(NewReader(), copyId));
        Assert.Equal(0, ActiveLoans(copyId));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void UnauthenticatedBorrow_DoesNotClaimCopy()
    {
        int copyId = NewCopy();
        AuthService.CurrentUser = null;
        try
        {
            Assert.Throws<BusinessRuleException>(() => new BorrowService().BorrowBook(NewReader(), copyId));
            Assert.Equal(0, ActiveLoans(copyId));
            Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
        }
        finally
        {
            AuthService.CurrentUser = new UserRepository().GetById(1);
        }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Database_RejectsSecondActiveLoanForSameCopy()
    {
        int copyId = NewCopy();
        int firstReader = NewReader();
        int secondReader = NewReader();
        new BorrowService().BorrowBook(firstReader, copyId);

        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BorrowRecords (BookId, CopyId, ReaderId, BorrowDate, DueDate, Status)
            SELECT BookId, CopyId, @ReaderId, GETDATE(), DATEADD(day, 7, GETDATE()), 'Borrowing'
            FROM dbo.BookCopies WHERE CopyId = @CopyId", connection);
        command.Parameters.AddWithValue("@ReaderId", secondReader);
        command.Parameters.AddWithValue("@CopyId", copyId);
        Assert.Throws<SqlException>(() => command.ExecuteNonQuery());
        Assert.Equal(1, ActiveLoans(copyId));
    }

    private static int NewCopy()
    {
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Concurrency test",
            Author = "Test",
            PublishYear = 2026,
            Quantity = 1
        });
        return new BookCopyService().GetCopies(bookId).Single().CopyId;
    }

    private static int NewReader() => new ReaderService().AddReader(new Reader
    {
        FullName = "Concurrency reader",
        StudentId = $"CON-{Guid.NewGuid():N}",
        Phone = "0901234567"
    });

    private static int ActiveLoans(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.BorrowRecords WHERE CopyId = @CopyId AND Status = 'Borrowing'", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (int)command.ExecuteScalar()!;
    }

    private static string CopyStatus(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT Status FROM dbo.BookCopies WHERE CopyId = @CopyId", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }

    private sealed class FailingBorrowRepository : BorrowRepository
    {
        public override int Add(SqlConnection connection, SqlTransaction? transaction, BorrowRecord record) =>
            throw new InvalidOperationException("Simulated insert failure");
    }

    private sealed class FailingAuditRepository : CirculationAuditRepository
    {
        public override void Add(SqlConnection connection, SqlTransaction transaction, CirculationAuditEvent auditEvent) =>
            throw new InvalidOperationException("Simulated audit insert failure");
    }
}
