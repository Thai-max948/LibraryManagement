using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class LegacySchemaIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void AppReadsAndCirculatesBooksBeforeBookCopyMigration()
    {
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                DROP TABLE dbo.CirculationAuditEvents;
                DROP INDEX UX_Books_ISBN ON Books;
                ALTER TABLE Books DROP COLUMN ISBN;
                ALTER TABLE Books DROP CONSTRAINT CK_Books_Status;
                ALTER TABLE Books DROP CONSTRAINT DF_Books_Status;
                ALTER TABLE Books DROP COLUMN Status, ArchivedAt;
                ALTER TABLE Books DROP COLUMN Publisher, Language;
                ALTER TABLE Books DROP CONSTRAINT DF_Books_CreatedAt;
                ALTER TABLE Books DROP COLUMN CreatedAt, UpdatedAt;
                DROP INDEX UX_BorrowRecords_ActiveCopy ON BorrowRecords;
                ALTER TABLE BorrowRecords DROP CONSTRAINT FK_BorrowRecords_BookCopies;
                ALTER TABLE BorrowRecords DROP COLUMN CopyId;
                DROP TABLE BookCopies;", connection);
            command.ExecuteNonQuery();
        }

        Assert.All(new BorrowRepository().GetBorrowingRecords(), record => Assert.Null(record.CopyId));
        Assert.NotEmpty(new BorrowRepository().GetHistory());

        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Legacy schema reader",
            StudentId = $"LEGACY-{Guid.NewGuid():N}",
            Phone = "0901234567"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Legacy schema book",
            Author = "Test",
            PublishYear = 2026,
            Quantity = 2,
            Isbn = "0-13-235088-2",
            Publisher = "  Nhà xuất bản Trẻ  ",
            Language = " EN "
        });
        Assert.Equal("9780132350884", new BookRepository().GetById(bookId)!.Isbn);
        Assert.Equal(BookStatuses.Active, new BookRepository().GetById(bookId)!.Status);
        Assert.Equal("Nhà xuất bản Trẻ", new BookRepository().GetById(bookId)!.Publisher);
        Assert.Equal("en", new BookRepository().GetById(bookId)!.Language);
        Assert.NotNull(new BookRepository().GetById(bookId)!.CreatedAt);
        Assert.Null(new BookRepository().GetById(bookId)!.UpdatedAt);
        // This test isolates pre-BookCopy compatibility; financial eligibility is covered separately.
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new NoFinancialStandingProvider());
        Assert.True(circulation.CanBorrow(readerId, bookId, out _));
        Assert.Throws<BusinessRuleException>(() =>
            circulation.BorrowBook(readerId, bookId));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);

        var migration = BookCopyMigration.Apply();
        Assert.True(migration.CopiesCreated > 0);
        Assert.True(migration.CopiesNeedingReview > 0);
        Assert.True(migration.UnresolvedLoans > 0);
        using var upgradedConnection = Database.GetConnection();
        upgradedConnection.Open();
        Assert.True(new BookCopyRepository().HasSchema(upgradedConnection, null));
        var migratedCopies = new BookCopyService().GetCopies(bookId);
        Assert.Equal(2, migratedCopies.Count);
        Assert.All(migratedCopies, copy => Assert.Equal(BookCopyStatuses.Available, copy.Status));
        var legacyUnverified = new BookCopyService().GetCopies(1)
            .Where(copy => copy.Status == BookCopyStatuses.UnderRepair && copy.Condition == "LegacyUnverified");
        Assert.Equal(3, legacyUnverified.Count());
        Assert.Equal(8, new BookRepository().GetById(1)!.AvailableQuantity);
        Assert.Equal(migration.UnresolvedLoans, new BorrowRepository().GetBorrowingRecords().Count(record => !record.BookCopyId.HasValue));
        Assert.Equal(0, BookCopyMigration.Apply().CopiesCreated);

        var legacyLoan = new BorrowRepository().GetBorrowingRecords().First(record => record.BookId == 1 && !record.BookCopyId.HasValue);
        var verifiedCopy = legacyUnverified.First();
        Assert.Throws<BusinessRuleException>(() => circulation.LinkLegacyBorrowToCopy(legacyLoan.BorrowId, migratedCopies[0].CopyId));
        circulation.LinkLegacyBorrowToCopy(legacyLoan.BorrowId, verifiedCopy.CopyId);
        Assert.Equal("Good", new BookCopyService().GetCopies(1).Single(copy => copy.CopyId == verifiedCopy.CopyId).Condition);
        Assert.Equal(verifiedCopy.CopyId, new BorrowRepository().GetById(legacyLoan.BorrowId)!.BookCopyId);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetCopies(1).Single(copy => copy.CopyId == verifiedCopy.CopyId).Status);
        circulation.ReturnBook(legacyLoan.BorrowId, ReturnCondition.Normal);
        Assert.Equal(BookCopyStatuses.Available, new BookCopyService().GetCopies(1).Single(copy => copy.CopyId == verifiedCopy.CopyId).Status);

        int borrowId = circulation.BorrowBook(readerId, migratedCopies[0].CopyId);
        Assert.Equal(migratedCopies[0].CopyId, new BorrowRepository().GetById(borrowId)!.BookCopyId);
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        circulation.ReturnBook(borrowId, ReturnCondition.Normal);
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Throws<BusinessRuleException>(() => new BookService().ArchiveBook(bookId));
        var copies = new BookCopyService();
        foreach (var copy in copies.GetCopies(bookId))
            copies.ChangeStatus(copy.CopyId, BookCopyStatuses.Retired);
        new BookService().ArchiveBook(bookId);
        Assert.Equal(BookStatuses.Archived, new BookRepository().GetById(bookId)!.Status);
        new BookService().RestoreBook(bookId);
        Assert.All(copies.GetCopies(bookId), copy => Assert.Equal(BookCopyStatuses.Retired, copy.Status));
    }
}
