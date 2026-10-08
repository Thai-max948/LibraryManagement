using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class BookCopyMigrationStateIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    private const string CompletionKey = "BookCopyBackfillV1";

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void EnsureStateTable_IsIdempotent_AndMissingMarkerIsIncomplete()
    {
        using var connection = OpenConnection();
        BookCopyMigration.EnsureMigrationStateTable(connection);
        BookCopyMigration.EnsureMigrationStateTable(connection);
        DeleteCompletionMarker(connection);

        Assert.False(BookCopyMigration.IsBackfillCompleted(connection));
        Assert.Equal(1, ScalarInt(connection, @"
            SELECT COUNT(*) FROM sys.tables
            WHERE object_id = OBJECT_ID(N'dbo.BookCopyMigrationState', N'U');"));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void MarkBackfillCompleted_IsIdempotent_AndPreservesInitialAppliedAt()
    {
        using var connection = OpenConnection();
        BookCopyMigration.EnsureMigrationStateTable(connection);
        DeleteCompletionMarker(connection);

        using (var transaction = connection.BeginTransaction())
        {
            BookCopyMigration.MarkBackfillCompleted(connection, transaction);
            transaction.Commit();
        }

        DateTime firstAppliedAt = ReadAppliedAt(connection);
        Assert.True(BookCopyMigration.IsBackfillCompleted(connection));

        using (var transaction = connection.BeginTransaction())
        {
            BookCopyMigration.MarkBackfillCompleted(connection, transaction);
            transaction.Commit();
        }

        Assert.Equal(1, ScalarInt(connection, @"
            SELECT COUNT(*) FROM dbo.BookCopyMigrationState WHERE MigrationKey = @MigrationKey;",
            ("@MigrationKey", CompletionKey)));
        Assert.Equal(firstAppliedAt, ReadAppliedAt(connection));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void MarkBackfillCompleted_ParticipatesInCallerTransaction()
    {
        using var connection = OpenConnection();
        BookCopyMigration.EnsureMigrationStateTable(connection);
        DeleteCompletionMarker(connection);

        using (var transaction = connection.BeginTransaction())
        {
            BookCopyMigration.MarkBackfillCompleted(connection, transaction);
            Assert.True(BookCopyMigration.IsBackfillCompleted(connection, transaction));
            transaction.Rollback();
        }

        Assert.False(BookCopyMigration.IsBackfillCompleted(connection));

        using (var transaction = connection.BeginTransaction())
        {
            BookCopyMigration.MarkBackfillCompleted(connection, transaction);
            transaction.Commit();
        }

        Assert.True(BookCopyMigration.IsBackfillCompleted(connection));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Apply_FirstSuccessfulRunMarksCompletion_AndSecondRunReportsNoWork()
    {
        using (var connection = OpenConnection())
        {
            BookCopyMigration.EnsureMigrationStateTable(connection);
            DeleteCompletionMarker(connection);
        }

        int bookId = AddBookWithoutCopies("Backfill completion " + Guid.NewGuid().ToString("N"));
        try
        {
            SetBookInventory(bookId, quantity: 2, available: 2);

            var firstResult = BookCopyMigration.Apply();
            Assert.True(IsBackfillCompleted());
            Assert.Equal(2, CountCopiesForBook(bookId));
            Assert.True(firstResult.CopiesCreated >= 2);
            DateTime firstAppliedAt = ReadAppliedAt();
            int totalCopiesAfterFirstRun = CountAllCopies();

            var secondResult = BookCopyMigration.Apply();

            Assert.Equal(new BookCopyMigrationResult(0, 0, 0), secondResult);
            Assert.Equal(totalCopiesAfterFirstRun, CountAllCopies());
            Assert.Equal(firstAppliedAt, ReadAppliedAt());
        }
        finally
        {
            DeleteBookAndCopies(bookId);
        }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Apply_CompletedMarkerSkipsAReconciliationThatWouldFailValidation()
    {
        BookCopyMigration.Apply();
        Assert.True(IsBackfillCompleted());

        int bookId = AddBookWithoutCopies("Skip reconciliation " + Guid.NewGuid().ToString("N"));
        try
        {
            int copyId = AddCopyWithNullBarcode(bookId);
            SetBookInventory(bookId, quantity: 2, available: 1);

            using (var dropIndex = OpenConnection())
            using (var command = new SqlCommand(
                "DROP INDEX IX_BookCopies_BookId_Status ON dbo.BookCopies;", dropIndex))
                command.ExecuteNonQuery();

            // The existing 1/2 mismatch is valid SQL data, but the old reconciliation loop must reject it.
            var result = BookCopyMigration.Apply();

            Assert.Equal(new BookCopyMigrationResult(0, 0, 0), result);
            Assert.Equal(1, CountCopiesForBook(bookId));
            Assert.True(IsBackfillCompleted());
            using var verifyConnection = OpenConnection();
            Assert.Equal(1, ScalarInt(verifyConnection, @"
                SELECT COUNT(*) FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.BookCopies')
                  AND name = N'IX_BookCopies_BookId_Status';"));
            Assert.Equal(BookCopyBarcode.Format(copyId), ReadCopyBarcode(copyId));
        }
        finally
        {
            DeleteBookAndCopies(bookId);
        }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Apply_CompletedMarkerStillRunsSchemaCompatibilityGuards()
    {
        BookCopyMigration.Apply();
        using var connection = OpenConnection();
        Assert.True(BookCopyMigration.IsBackfillCompleted(connection));
        int copyId = ScalarInt(connection, @"
            SELECT TOP (1) CopyId FROM dbo.BookCopies WHERE Status = 'Available' ORDER BY CopyId;");

        using (var command = new SqlCommand(@"
            DROP INDEX UX_BorrowRecords_ActiveCopy ON dbo.BorrowRecords;
            ALTER TABLE dbo.BookCopies ADD IsLegacyUnverified BIT NOT NULL
                CONSTRAINT DF_BookCopies_IsLegacyUnverified_MarkerTest DEFAULT (0);
            UPDATE dbo.BookCopies SET Barcode = N'legacy-marker-test', IsLegacyUnverified = 1
            WHERE CopyId = @CopyId;", connection))
        {
            command.Parameters.AddWithValue("@CopyId", copyId);
            command.ExecuteNonQuery();
        }

        var result = BookCopyMigration.Apply();

        Assert.Equal(new BookCopyMigrationResult(0, 0, 0), result);
        Assert.Equal(1, ScalarInt(connection, @"
            SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
                AND name = 'UX_BorrowRecords_ActiveCopy';"));
        Assert.Equal(0, ScalarInt(connection, @"
            SELECT CASE WHEN COL_LENGTH('dbo.BookCopies', 'IsLegacyUnverified') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal("LegacyUnverified", ReadCopyCondition(connection, copyId));
        Assert.Equal(BookCopyBarcode.Format(copyId), ReadCopyBarcode(connection, copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Apply_ReconciliationFailureRollsBackWorkAndLeavesMarkerAbsent()
    {
        BookCopyMigration.Apply();
        using (var connection = OpenConnection())
        {
            BookCopyMigration.EnsureMigrationStateTable(connection);
            DeleteCompletionMarker(connection);
        }

        int bookToBackfill = AddBookWithoutCopies("Rollback backfill " + Guid.NewGuid().ToString("N"));
        int inconsistentBook = AddBookWithoutCopies("Rollback failure " + Guid.NewGuid().ToString("N"));
        try
        {
            SetBookInventory(bookToBackfill, quantity: 2, available: 2);
            AddCopyWithNullBarcode(inconsistentBook);
            SetBookInventory(inconsistentBook, quantity: 2, available: 1);

            Assert.Throws<InvalidOperationException>(() => BookCopyMigration.Apply());

            Assert.False(IsBackfillCompleted());
            Assert.Equal(0, CountCopiesForBook(bookToBackfill));
            Assert.Equal(1, CountCopiesForBook(inconsistentBook));
        }
        finally
        {
            DeleteBookAndCopies(bookToBackfill, inconsistentBook);
        }
    }

    private static SqlConnection OpenConnection()
    {
        var connection = Database.GetConnection();
        connection.Open();
        return connection;
    }

    private static void DeleteCompletionMarker(SqlConnection connection)
    {
        using var command = new SqlCommand(@"
            DELETE FROM dbo.BookCopyMigrationState WHERE MigrationKey = @MigrationKey;", connection);
        command.Parameters.Add("@MigrationKey", System.Data.SqlDbType.NVarChar, 100).Value = CompletionKey;
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqlConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static DateTime ReadAppliedAt(SqlConnection connection)
    {
        using var command = new SqlCommand(@"
            SELECT AppliedAt FROM dbo.BookCopyMigrationState WHERE MigrationKey = @MigrationKey;", connection);
        command.Parameters.Add("@MigrationKey", System.Data.SqlDbType.NVarChar, 100).Value = CompletionKey;
        return (DateTime)command.ExecuteScalar()!;
    }

    private static bool IsBackfillCompleted()
    {
        using var connection = OpenConnection();
        return BookCopyMigration.IsBackfillCompleted(connection);
    }

    private static DateTime ReadAppliedAt()
    {
        using var connection = OpenConnection();
        return ReadAppliedAt(connection);
    }

    private static int AddBookWithoutCopies(string title) => new BookService().AddBook(new Book
    {
        Title = title,
        Author = "Migration test",
        PublishYear = 2026,
        Quantity = 0
    });

    private static void SetBookInventory(int bookId, int quantity, int available)
    {
        using var connection = OpenConnection();
        using var command = new SqlCommand(@"
            UPDATE dbo.Books SET Quantity = @Quantity, AvailableQuantity = @AvailableQuantity
            WHERE BookId = @BookId;", connection);
        command.Parameters.AddWithValue("@Quantity", quantity);
        command.Parameters.AddWithValue("@AvailableQuantity", available);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.ExecuteNonQuery();
    }

    private static int AddCopyWithNullBarcode(int bookId)
    {
        using var connection = OpenConnection();
        using var command = new SqlCommand(@"
            INSERT INTO dbo.BookCopies (BookId, Barcode, Status, Condition)
            OUTPUT INSERTED.CopyId
            VALUES (@BookId, NULL, 'Available', 'Good');", connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountCopiesForBook(int bookId)
    {
        using var connection = OpenConnection();
        return ScalarInt(connection, "SELECT COUNT(*) FROM dbo.BookCopies WHERE BookId = @BookId;",
            ("@BookId", bookId));
    }

    private static int CountAllCopies()
    {
        using var connection = OpenConnection();
        return ScalarInt(connection, "SELECT COUNT(*) FROM dbo.BookCopies;");
    }

    private static string ReadCopyBarcode(int copyId)
    {
        using var connection = OpenConnection();
        return ReadCopyBarcode(connection, copyId);
    }

    private static string ReadCopyBarcode(SqlConnection connection, int copyId)
    {
        using var command = new SqlCommand("SELECT Barcode FROM dbo.BookCopies WHERE CopyId = @CopyId;", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }

    private static string ReadCopyCondition(SqlConnection connection, int copyId)
    {
        using var command = new SqlCommand("SELECT Condition FROM dbo.BookCopies WHERE CopyId = @CopyId;", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }

    private static void DeleteBookAndCopies(params int[] bookIds)
    {
        using var connection = OpenConnection();
        var parameterNames = bookIds.Select((_, index) => $"@BookId{index}").ToArray();
        string ids = string.Join(",", parameterNames);
        using var command = new SqlCommand($@"
            DELETE FROM dbo.BookCopies WHERE BookId IN ({ids});
            DELETE FROM dbo.Books WHERE BookId IN ({ids});", connection);
        for (int index = 0; index < bookIds.Length; index++)
            command.Parameters.AddWithValue(parameterNames[index], bookIds[index]);
        command.ExecuteNonQuery();
    }
}
