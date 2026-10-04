using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BookCopyBulkIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BulkAdd_OneCopyUsesItsDatabaseIdentity()
    {
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "One generated copy", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        var copies = new BookCopyService();

        int id = Assert.Single(copies.AddCopies(bookId, 1));
        var stored = Assert.Single(copies.GetCopies(bookId));
        Assert.Equal(id, stored.CopyId);
        Assert.Equal(BookCopyBarcode.Format(id), stored.Barcode);
        Assert.Equal(BookCopyStatuses.Available, stored.Status);
        Assert.Equal("Good", stored.Condition);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BulkAdd_GeneratesIdentityBarcodesAndExistingCopiesRemainCanonical()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int bookId = books.AddBook(new Book
        {
            Title = "Bulk barcode test", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        int oldId = copies.AddCopy(bookId);
        string oldBarcode = BookCopyBarcode.Format(oldId);
        copies.ChangeStatus(oldId, BookCopyStatuses.Retired);

        var ids = copies.AddCopies(bookId, 3);
        var stored = copies.GetCopies(bookId);
        Assert.Equal(3, ids.Count);
        Assert.Equal(4, stored.Count);
        Assert.Equal(oldBarcode, stored.Single(copy => copy.CopyId == oldId).Barcode);
        Assert.Equal(4, stored.Select(copy => copy.Barcode).Distinct().Count());
        Assert.All(stored.Where(copy => ids.Contains(copy.CopyId)), copy =>
        {
            Assert.Equal(BookCopyBarcode.Format(copy.CopyId), copy.Barcode);
            Assert.Equal(BookCopyStatuses.Available, copy.Status);
            Assert.Equal("Good", copy.Condition);
            Assert.InRange(copy.CreatedAt, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
        });
        var inventory = copies.GetInventory(bookId);
        Assert.Equal(4, inventory.TotalCopies);
        Assert.Equal(3, inventory.ActiveCopies);
        Assert.Equal(3, inventory.Available);
        Assert.Equal(1, inventory.Retired);
        Assert.True(inventory.IsBalanced);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BarcodeMigration_NormalizesLegacyValuesAndPreservesCopyAndLoanRelationships()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int bookId = books.AddBook(new Book
        {
            Title = "Barcode migration", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        var ids = copies.AddCopies(bookId, 3);
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Barcode migration reader", StudentId = "BARCODE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });
        int borrowId = new BorrowService().BorrowBook(readerId, ids[0]);
        Execute(@"UPDATE dbo.BookCopies SET Barcode = CASE CopyId
                WHEN @First THEN @FirstLegacy WHEN @Second THEN N'de' ELSE @Canonical END
            WHERE CopyId IN (@First, @Second, @Third);
            UPDATE dbo.BorrowRecords SET BarcodeSnapshot = N'legacy-history-snapshot' WHERE BorrowId = @BorrowId;",
            ("@First", ids[0]), ("@Second", ids[1]), ("@Third", ids[2]),
            ("@FirstLegacy", "BK000003-5e524e9ff75d4a31bfa792bed34ad195"),
            ("@Canonical", BookCopyBarcode.Format(ids[2])), ("@BorrowId", borrowId));
        var before = copies.GetCopies(bookId).ToDictionary(copy => copy.CopyId);

        using (var connection = LibraryManagement.Data.Database.GetConnection())
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            BookCopyBarcodeMigration.Apply(connection, transaction);
            transaction.Commit();
        }

        var after = copies.GetCopies(bookId).ToDictionary(copy => copy.CopyId);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(after.Values, copy => Assert.Equal(BookCopyBarcode.Format(copy.CopyId), copy.Barcode));
        Assert.Equal(after.Count, after.Values.Select(copy => copy.Barcode).Distinct().Count());
        Assert.All(before, pair =>
        {
            var migrated = after[pair.Key];
            Assert.Equal(pair.Value.BookId, migrated.BookId);
            Assert.Equal(pair.Value.Status, migrated.Status);
            Assert.Equal(pair.Value.Condition, migrated.Condition);
            Assert.Equal(pair.Value.CreatedAt, migrated.CreatedAt);
        });
        Assert.Equal(ids[0], new BorrowRepository().GetById(borrowId)!.BookCopyId);
        var activeByBarcode = new BorrowService().FindActiveReturnByBarcode(BookCopyBarcode.Format(ids[0]));
        Assert.Equal(borrowId, activeByBarcode.BorrowId);
        Assert.Equal("legacy-history-snapshot", ReadBorrowBarcodeSnapshot(borrowId));
        Assert.Equal(ids[1], copies.GetByBarcode(BookCopyBarcode.Format(ids[1]))!.CopyId);
        Assert.True(ReadFilteredUniqueBarcodeIndex());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BarcodeMigration_FailureRollsBackEveryBarcodeUpdate()
    {
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Barcode rollback", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        var copies = new BookCopyService();
        var ids = copies.AddCopies(bookId, 2);
        Execute("UPDATE dbo.BookCopies SET Barcode = N'legacy-' + CONVERT(NVARCHAR(20), CopyId) WHERE CopyId IN (@First, @Second);",
            ("@First", ids[0]), ("@Second", ids[1]));
        Execute(@"CREATE TRIGGER dbo.TR_BookCopies_RejectBarcodeMigration ON dbo.BookCopies
            AFTER UPDATE AS
            IF UPDATE(Barcode) THROW 51000, 'Injected barcode migration failure.', 1;");

        try
        {
            using var connection = LibraryManagement.Data.Database.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            Assert.Throws<SqlException>(() => BookCopyBarcodeMigration.Apply(connection, transaction));
            transaction.Rollback();
        }
        finally
        {
            Execute("DROP TRIGGER dbo.TR_BookCopies_RejectBarcodeMigration;");
        }

        Assert.Equal(ids.Select(id => "legacy-" + id).Order(), copies.GetCopies(bookId).Select(copy => copy.Barcode).Order());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BulkAdd_FailureAfterPartialInserts_RollsBackEntireBatch()
    {
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Atomic bulk test", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        var failing = new BookCopyService(new FailAfterSecondCopyRepository(), new BookRepository());

        var error = Assert.Throws<BusinessRuleException>(() => failing.AddCopies(bookId, 3));
        Assert.Contains("Injected failure", error.Message);
        Assert.Empty(new BookCopyService().GetCopies(bookId));
        Assert.Equal(0, new BookCopyService().GetInventory(bookId).TotalCopies);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void CopyMigration_RestoresConditionFromVerificationFlag()
    {
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Legacy condition migration", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        int copyId = new BookCopyService().AddCopy(bookId);
        Execute(@"
            DECLARE @ConditionDefaultConstraint sysname;
            SELECT @ConditionDefaultConstraint = dc.name
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID('dbo.BookCopies') AND c.name = 'Condition';
            IF @ConditionDefaultConstraint IS NOT NULL
                EXEC(N'ALTER TABLE dbo.BookCopies DROP CONSTRAINT ' + QUOTENAME(@ConditionDefaultConstraint));
            ALTER TABLE dbo.BookCopies DROP COLUMN Condition;
            ALTER TABLE dbo.BookCopies ADD IsLegacyUnverified BIT NOT NULL
                CONSTRAINT DF_BookCopies_IsLegacyUnverified DEFAULT (0);
            UPDATE dbo.BookCopies SET IsLegacyUnverified = 1 WHERE CopyId = @CopyId;",
            ("@CopyId", copyId));

        BookCopyMigration.Apply();

        Assert.Equal("LegacyUnverified", new BookCopyService().GetCopies(bookId).Single().Condition);
        using var connection = LibraryManagement.Data.Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(
            "SELECT CASE WHEN COL_LENGTH('dbo.BookCopies', 'IsLegacyUnverified') IS NULL THEN 0 ELSE 1 END", connection);
        Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BulkAdd_ArchivedBookIsRejectedWithoutNewCopies()
    {
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Archived bulk test", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        books.ArchiveBook(bookId);

        Assert.Throws<BusinessRuleException>(() => new BookCopyService().AddCopies(bookId, 3));
        Assert.Empty(new BookCopyService().GetCopies(bookId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BulkAdd_UpgradesNotNullBarcodeSchemaAndNormalizesExistingValues()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int bookId = books.AddBook(new Book
        {
            Title = "Old barcode schema", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        int oldId = copies.AddCopy(bookId);
        string oldBarcode = BookCopyBarcode.Format(oldId);
        using (var connection = LibraryManagement.Data.Database.GetConnection())
        {
            connection.Open();
            foreach (string statement in new[]
            {
                "DROP INDEX UX_BookCopies_Barcode_NotNull ON dbo.BookCopies",
                "ALTER TABLE dbo.BookCopies ALTER COLUMN Barcode NVARCHAR(100) NOT NULL",
                "ALTER TABLE dbo.BookCopies ADD CONSTRAINT UQ_BookCopies_Barcode UNIQUE (Barcode)"
            })
            {
                using var command = new SqlCommand(statement, connection);
                command.ExecuteNonQuery();
            }
        }

        int newId = Assert.Single(copies.AddCopies(bookId, 1));
        Assert.Equal(oldBarcode, copies.GetCopies(bookId).Single(copy => copy.CopyId == oldId).Barcode);
        Assert.Equal(BookCopyBarcode.Format(newId), copies.GetCopies(bookId).Single(copy => copy.CopyId == newId).Barcode);
    }

    private static void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = LibraryManagement.Data.Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static bool ReadFilteredUniqueBarcodeIndex()
    {
        using var connection = LibraryManagement.Data.Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"SELECT CASE WHEN EXISTS (
            SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BookCopies')
            AND name = 'UX_BookCopies_Barcode_NotNull' AND is_unique = 1 AND has_filter = 1
            AND filter_definition LIKE '%Barcode%IS NOT NULL%') THEN 1 ELSE 0 END", connection);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static string? ReadBorrowBarcodeSnapshot(int borrowId)
    {
        using var connection = LibraryManagement.Data.Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT BarcodeSnapshot FROM dbo.BorrowRecords WHERE BorrowId = @BorrowId", connection);
        command.Parameters.AddWithValue("@BorrowId", borrowId);
        return command.ExecuteScalar() as string;
    }

    private sealed class FailAfterSecondCopyRepository : BookCopyRepository
    {
        private int _inserted;

        public override int InsertGeneratedCopy(SqlConnection connection, SqlTransaction transaction, int bookId,
            string status = BookCopyStatuses.Available, string condition = "Good")
        {
            int id = base.InsertGeneratedCopy(connection, transaction, bookId, status, condition);
            if (++_inserted == 2) throw new InvalidOperationException("Injected failure after second insert.");
            return id;
        }
    }
}
