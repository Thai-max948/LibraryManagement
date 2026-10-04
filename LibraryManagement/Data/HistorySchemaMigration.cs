using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

/// <summary>Stores immutable circulation display snapshots and backfills only events provable from old rows.</summary>
public static class HistorySchemaMigration
{
    private const string BackfillKey = "HistoryCirculationFactsV1";

    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Execute(connection, transaction, @"
                DECLARE @LockResult int;
                EXEC @LockResult = sp_getapplock @Resource = 'LibraryManagement.HistorySchemaMigration',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @LockResult < 0 THROW 50003, 'Unable to acquire History migration lock.', 1;

                IF OBJECT_ID('dbo.HistoryMigrationState', 'U') IS NULL
                    CREATE TABLE dbo.HistoryMigrationState (
                        MigrationKey NVARCHAR(100) NOT NULL PRIMARY KEY,
                        AppliedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                IF COL_LENGTH('dbo.BorrowRecords', 'ReaderNameSnapshot') IS NULL
                    ALTER TABLE dbo.BorrowRecords ADD ReaderNameSnapshot NVARCHAR(150) NULL;
                IF COL_LENGTH('dbo.BorrowRecords', 'BookTitleSnapshot') IS NULL
                    ALTER TABLE dbo.BorrowRecords ADD BookTitleSnapshot NVARCHAR(255) NULL;
                IF COL_LENGTH('dbo.BorrowRecords', 'BarcodeSnapshot') IS NULL
                    ALTER TABLE dbo.BorrowRecords ADD BarcodeSnapshot NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.BorrowRecords', 'CopyId') IS NULL
                    ALTER TABLE dbo.BorrowRecords ADD CopyId INT NULL;
            ");

            using (var backfill = new SqlCommand(@"
                IF NOT EXISTS (SELECT 1 FROM dbo.HistoryMigrationState WHERE MigrationKey = @MigrationKey)
                BEGIN
                    INSERT INTO dbo.CirculationAuditEvents
                        (BorrowId, EventType, ActorUserId, ActorNameSnapshot, OccurredAt, BookCopyId, Note)
                    SELECT br.BorrowId, 'BorrowCreated', NULL, 'Legacy / Unknown', br.BorrowDate, br.CopyId, NULL
                    FROM dbo.BorrowRecords br
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.CirculationAuditEvents e
                        WHERE e.BorrowId = br.BorrowId AND e.EventType = 'BorrowCreated');

                    INSERT INTO dbo.CirculationAuditEvents
                        (BorrowId, EventType, ActorUserId, ActorNameSnapshot, OccurredAt, BookCopyId, Note)
                    SELECT br.BorrowId,
                        CASE br.ReturnCondition WHEN 'Damaged' THEN 'ReturnedDamaged'
                            WHEN 'NeedsRepair' THEN 'ReturnedNeedsRepair'
                            WHEN 'Normal' THEN 'ReturnedNormal' ELSE 'Returned' END,
                        NULL, 'Legacy / Unknown', br.ReturnDate, br.CopyId, br.ConditionNote
                    FROM dbo.BorrowRecords br
                    WHERE br.Status = 'Returned' AND br.ReturnDate IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM dbo.CirculationAuditEvents e
                        WHERE e.BorrowId = br.BorrowId AND e.EventType IN
                            ('Returned','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair'));

                    INSERT INTO dbo.CirculationAuditEvents
                        (BorrowId, EventType, ActorUserId, ActorNameSnapshot, OccurredAt, BookCopyId, Note)
                    SELECT br.BorrowId, 'MarkedLost', NULL, 'Legacy / Unknown', br.LostDate, br.CopyId, br.LostNote
                    FROM dbo.BorrowRecords br
                    WHERE br.Status = 'Lost' AND br.LostDate IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM dbo.CirculationAuditEvents e
                        WHERE e.BorrowId = br.BorrowId AND e.EventType = 'MarkedLost');

                    INSERT INTO dbo.HistoryMigrationState (MigrationKey) VALUES (@MigrationKey);
                END;", connection, transaction))
            {
                backfill.Parameters.AddWithValue("@MigrationKey", BackfillKey);
                backfill.ExecuteNonQuery();
            }

            Execute(connection, transaction, @"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
                    AND name = 'IX_BorrowRecords_HistoryBorrowDate')
                    CREATE INDEX IX_BorrowRecords_HistoryBorrowDate
                        ON dbo.BorrowRecords(BorrowDate DESC, BorrowId DESC)
                        INCLUDE (ReaderId, BookId, CopyId, DueDate, ReturnDate, Status, LostDate);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
                    AND name = 'IX_BorrowRecords_HistoryReturnDate')
                    CREATE INDEX IX_BorrowRecords_HistoryReturnDate
                        ON dbo.BorrowRecords(ReturnDate DESC, BorrowId DESC)
                        INCLUDE (ReaderId, BookId, CopyId, DueDate, Status)
                        WHERE ReturnDate IS NOT NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
                    AND name = 'IX_BorrowRecords_ActiveDueDate')
                    CREATE INDEX IX_BorrowRecords_ActiveDueDate
                        ON dbo.BorrowRecords(DueDate) INCLUDE (BorrowId) WHERE Status = 'Borrowing';
            ");
            transaction.Commit();
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
}
