using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class BorrowCompletionIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ReaderBorrowExistenceQueries_MatchBorrowingStatusAndRetainReturnedHistory()
    {
        int bookId = NewBook();
        int copyId = new BookCopyService().GetCopies(bookId).Single().CopyId;
        int readerId = NewReader();
        var repository = new BorrowRepository();

        Assert.False(repository.HasActiveBorrowByReader(readerId));
        Assert.False(repository.HasBorrowHistoryByReader(readerId));

        int loanId = new BorrowService().BorrowBook(readerId, copyId);
        Assert.True(repository.HasActiveBorrowByReader(readerId));
        Assert.True(repository.HasBorrowHistoryByReader(readerId));

        new BorrowService().ReturnBook(loanId, ReturnCondition.Normal);
        Assert.False(repository.HasActiveBorrowByReader(readerId));
        Assert.True(repository.HasBorrowHistoryByReader(readerId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BorrowAndReturn_IgnoreLegacyCountersAndNeverUpdateThem()
    {
        int bookId = NewBook();
        int copyId = new BookCopyService().GetCopies(bookId).Single().CopyId;
        Execute("UPDATE Books SET Quantity=50, AvailableQuantity=49 WHERE BookId=@Id", bookId);
        int loanId = new BorrowService().BorrowBook(NewReader(), copyId);
        var book = new BookRepository().GetById(bookId)!;
        Assert.Equal(1, book.Quantity);
        Assert.Equal(0, book.AvailableQuantity);
        var report = Assert.Single(new InventoryCheckService().Check(), row => row.BookId == bookId);
        Assert.Equal(49, report.StoredAvailable);
        Assert.Equal(0, report.InconsistentCopies);
        new BorrowService().ReturnBook(loanId, ReturnCondition.Normal);
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(49, Assert.Single(new InventoryCheckService().Check(), row => row.BookId == bookId).StoredAvailable);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BarcodeLookup_UsesExactUniqueCopyAndBlocksUnverifiedClaim()
    {
        int bookId = NewBook();
        var copy = new BookCopyService().GetCopies(bookId).Single();
        Assert.Equal(copy.CopyId, new BookCopyService().GetByBarcode(copy.Barcode)!.CopyId);
        Assert.Null(new BookCopyService().GetByBarcode("MISSING-" + Guid.NewGuid()));
        Execute("UPDATE BookCopies SET Condition='LegacyUnverified' WHERE CopyId=@Id", copy.CopyId);
        Assert.Throws<BusinessRuleException>(() => new BorrowService().BorrowBook(NewReader(), copy.CopyId));
        Assert.Empty(new BorrowService().GetHistory(bookId: bookId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task DifferentCopiesOfSameBook_CanBorrowWhileOtherCopyTransactionIsOpen()
    {
        // Match the app's startup path so lazy schema DDL does not run under the held copy transaction.
        LibraryDatabaseStartupMigration.Apply();
        LoanPolicyMigration.Apply();
        ReturnOutcomeMigration.Apply();
        CirculationAuditMigration.Apply();
        HistorySchemaMigration.Apply();

        int bookId = NewBook();
        var copies = new BookCopyService();
        int firstCopy = copies.GetCopies(bookId).Single().CopyId;
        int secondCopy = copies.AddCopy(bookId);
        int readerId = NewReader();
        // Simulate a borrow holding only a shared parent guard and exact copy claim.
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        new BookRepository().GetCirculationStatus(connection, transaction, bookId);
        Assert.True(new BookCopyRepository().TryClaimForBorrow(connection, transaction, firstCopy));
        var otherBorrow = Task.Run(() => new BorrowService().BorrowBook(readerId, secondCopy));
        try
        {
            var completed = await Task.WhenAny(otherBorrow, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(otherBorrow, completed);
            Assert.True(await otherBorrow > 0);
        }
        finally
        {
            transaction.Rollback();
            // Drain the worker before fixture cleanup, also when the timeout assertion fails.
            try { await otherBorrow; } catch { }
        }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ReturnAuditFailure_RollsBackLoanCopyAndAudit()
    {
        int bookId = NewBook();
        int copyId = new BookCopyService().GetCopies(bookId).Single().CopyId;
        int loanId = new BorrowService().BorrowBook(NewReader(), copyId);
        var failing = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new NoFinancialStandingProvider(), new FailingAuditRepository(), new AuthServiceCurrentUserContext());
        Assert.Throws<InvalidOperationException>(() => failing.ReturnBook(loanId, ReturnCondition.Normal));
        Assert.Equal("Borrowing", new BorrowRepository().GetById(loanId)!.Status);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetCopies(bookId).Single().Status);
        Assert.Equal(CirculationAuditEventType.BorrowCreated, Assert.Single(new BorrowService().GetAuditEvents(new[] { loanId })).EventType);
    }

    private sealed class FailingAuditRepository : CirculationAuditRepository
    {
        public override void Add(SqlConnection connection, SqlTransaction transaction, CirculationAuditEvent auditEvent) =>
            throw new InvalidOperationException("Audit unavailable");
    }
    private static int NewBook() => new BookService().AddBook(new Book
    { Title = "Borrow completion", Author = "Author", PublishYear = 2026, Quantity = 1 });
    private static int NewReader() => new ReaderService().AddReader(new Reader
    { FullName = "Completion reader", StudentId = "DONE-" + Guid.NewGuid(), Phone = "0901234567" });
    private static void Execute(string sql, int id)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();
    }
}
