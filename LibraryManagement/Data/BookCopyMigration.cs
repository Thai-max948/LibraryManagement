using System.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public sealed record BookCopyMigrationResult(int CopiesCreated, int CopiesNeedingReview, int UnresolvedLoans);

public static class BookCopyMigration
{
    public static BookCopyMigrationResult Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            Execute(connection, transaction, @"
                IF OBJECT_ID('dbo.BookCopies', 'U') IS NULL
                    CREATE TABLE dbo.BookCopies (
                        CopyId INT IDENTITY(1,1) PRIMARY KEY,
                        BookId INT NOT NULL,
                        Barcode NVARCHAR(100) NULL,
                        Status NVARCHAR(30) NOT NULL DEFAULT 'Available',
                        Condition NVARCHAR(50) NOT NULL DEFAULT 'Good',
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_BookCopies_Books FOREIGN KEY (BookId) REFERENCES dbo.Books(BookId),
                        CONSTRAINT CK_BookCopies_Status CHECK (Status IN ('Available','Borrowed','Lost','Damaged','UnderRepair','Retired'))
                    );");
            Execute(connection, transaction, @"
                IF COL_LENGTH('dbo.BookCopies', 'Condition') IS NULL
                    ALTER TABLE dbo.BookCopies ADD Condition NVARCHAR(50) NOT NULL
                        CONSTRAINT DF_BookCopies_Condition DEFAULT ('Good');");
            Execute(connection, transaction, @"
                IF COL_LENGTH('dbo.BookCopies', 'IsLegacyUnverified') IS NOT NULL
                BEGIN
                    EXEC sys.sp_executesql N'
                        UPDATE dbo.BookCopies
                        SET Condition = N''LegacyUnverified''
                        WHERE IsLegacyUnverified = 1;';

                    DECLARE @LegacyFlagDefaultConstraint sysname;
                    SELECT @LegacyFlagDefaultConstraint = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = OBJECT_ID('dbo.BookCopies') AND c.name = 'IsLegacyUnverified';
                    IF @LegacyFlagDefaultConstraint IS NOT NULL
                    BEGIN
                        DECLARE @DropLegacyFlagDefaultSql nvarchar(max) =
                            N'ALTER TABLE dbo.BookCopies DROP CONSTRAINT ' + QUOTENAME(@LegacyFlagDefaultConstraint);
                        EXEC sys.sp_executesql @DropLegacyFlagDefaultSql;
                    END;

                    ALTER TABLE dbo.BookCopies DROP COLUMN IsLegacyUnverified;
                END;");
            Execute(connection, transaction, @"
                IF EXISTS (SELECT 1 FROM sys.check_constraints
                    WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'CK_BookCopies_Status'
                    AND (definition NOT LIKE '%Available%' OR definition NOT LIKE '%Borrowed%'
                        OR definition NOT LIKE '%Lost%' OR definition NOT LIKE '%Damaged%'
                        OR definition NOT LIKE '%UnderRepair%' OR definition NOT LIKE '%Retired%'))
                    ALTER TABLE dbo.BookCopies DROP CONSTRAINT CK_BookCopies_Status;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                    WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'CK_BookCopies_Status')
                    ALTER TABLE dbo.BookCopies ADD CONSTRAINT CK_BookCopies_Status
                        CHECK (Status IN ('Available','Borrowed','Lost','Damaged','UnderRepair','Retired'));");
            BookCopyBarcodeMigration.Apply(connection, transaction);
            Execute(connection, transaction, @"
                IF COL_LENGTH('dbo.BorrowRecords', 'CopyId') IS NULL
                    ALTER TABLE dbo.BorrowRecords ADD CopyId INT NULL;");
            Execute(connection, transaction, @"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BorrowRecords_BookCopies')
                    ALTER TABLE dbo.BorrowRecords ADD CONSTRAINT FK_BorrowRecords_BookCopies
                        FOREIGN KEY (CopyId) REFERENCES dbo.BookCopies(CopyId);");

            var books = new List<(int Id, int Quantity, int Available)>();
            using (var command = new SqlCommand("SELECT BookId, Quantity, AvailableQuantity FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) ORDER BY BookId", connection, transaction))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) books.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));

            int copiesCreated = 0, copiesNeedingReview = 0, unresolvedLoans = 0;
            foreach (var book in books)
            {
                var openLoanIds = ReadOpenLoanIds(connection, transaction, book.Id);
                int existing = Count(connection, transaction, "SELECT COUNT(*) FROM dbo.BookCopies WHERE BookId = @BookId", book.Id);
                unresolvedLoans += openLoanIds.Count;
                if (openLoanIds.Count > book.Quantity)
                    throw new InvalidOperationException($"Sách #{book.Id} có nhiều phiếu mượn chưa đối chiếu hơn số bản vật lý. Cần kiểm tra dữ liệu trước khi chuyển đổi.");

                if (existing == 0)
                {
                    int unavailable = Math.Max(openLoanIds.Count, Math.Clamp(book.Quantity - book.Available, 0, book.Quantity));
                    for (int index = 0; index < book.Quantity; index++)
                    {
                        string status = index < unavailable ? BookCopyStatuses.UnderRepair
                            : BookCopyStatuses.Available;
                        string condition = status == BookCopyStatuses.UnderRepair ? "LegacyUnverified" : "Good";
                        int copyId = InsertCopy(connection, transaction, book.Id, status, condition);
                        copiesCreated++;
                        if (status == BookCopyStatuses.UnderRepair) copiesNeedingReview++;
                    }
                }
                else
                {
                    if (existing < book.Quantity)
                        throw new InvalidOperationException($"Sách #{book.Id} mới có {existing}/{book.Quantity} bản. Cần đối chiếu dữ liệu trước khi chuyển đổi.");
                    int reservedForLegacyLoans = Count(connection, transaction,
                        "SELECT COUNT(*) FROM dbo.BookCopies WHERE BookId = @BookId AND Status = 'UnderRepair' AND Condition = 'LegacyUnverified'", book.Id);
                    int missingQuarantine = Math.Max(0, openLoanIds.Count - reservedForLegacyLoans);
                    if (missingQuarantine > 0)
                    {
                        using var quarantine = new SqlCommand(@"WITH Candidates AS (
                            SELECT TOP (@Count) CopyId FROM dbo.BookCopies
                            WHERE BookId = @BookId AND Status = 'Available' ORDER BY CopyId
                        )
                        UPDATE dbo.BookCopies SET Status = 'UnderRepair', Condition = 'LegacyUnverified'
                        WHERE CopyId IN (SELECT CopyId FROM Candidates)", connection, transaction);
                        quarantine.Parameters.AddWithValue("@Count", missingQuarantine);
                        quarantine.Parameters.AddWithValue("@BookId", book.Id);
                        if (quarantine.ExecuteNonQuery() != missingQuarantine)
                            throw new InvalidOperationException($"Không thể giữ đủ bản sách #{book.Id} để đối chiếu phiếu mượn cũ.");
                        copiesNeedingReview += missingQuarantine;
                    }
                }

            }

            Execute(connection, transaction, @"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
                    AND name = 'UX_BorrowRecords_ActiveCopy')
                    CREATE UNIQUE INDEX UX_BorrowRecords_ActiveCopy ON dbo.BorrowRecords(CopyId)
                    WHERE CopyId IS NOT NULL AND Status = 'Borrowing';");

            transaction.Commit();
            return new BookCopyMigrationResult(copiesCreated, copiesNeedingReview, unresolvedLoans);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void Execute(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private static int Count(SqlConnection connection, SqlTransaction transaction, string sql, int bookId)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<int> ReadOpenLoanIds(SqlConnection connection, SqlTransaction transaction, int bookId)
    {
        var ids = new List<int>();
        using var command = new SqlCommand(@"
            SELECT BorrowId FROM dbo.BorrowRecords WITH (UPDLOCK, HOLDLOCK)
            WHERE BookId = @BookId AND Status = 'Borrowing' AND CopyId IS NULL ORDER BY BorrowId", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    private static int InsertCopy(SqlConnection connection, SqlTransaction transaction, int bookId, string status, string condition)
    {
        return new LibraryManagement.Repositories.BookCopyRepository()
            .InsertGeneratedCopy(connection, transaction, bookId, status, condition);
    }

}
