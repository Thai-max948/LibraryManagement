using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class CirculationAuditMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var migrationLock = new SqlCommand(@"
            DECLARE @Result int;
            EXEC @Result = sp_getapplock @Resource = 'LibraryManagement.CirculationAuditMigration',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @Result;", connection, transaction);
        if (System.Convert.ToInt32(migrationLock.ExecuteScalar()) < 0)
            throw new System.InvalidOperationException("Không thể khóa cập nhật nhật ký mượn trả.");
        using var command = new SqlCommand(@"
            IF OBJECT_ID('dbo.CirculationAuditEvents', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CirculationAuditEvents (
                    AuditEventId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    BorrowId INT NOT NULL,
                    EventType NVARCHAR(40) NOT NULL,
                    ActorUserId INT NULL,
                    ActorNameSnapshot NVARCHAR(150) NOT NULL,
                    OccurredAt DATETIME2 NOT NULL,
                    BookCopyId INT NULL,
                    Note NVARCHAR(500) NULL,
                    CONSTRAINT FK_CirculationAudit_Borrow FOREIGN KEY (BorrowId)
                        REFERENCES dbo.BorrowRecords(BorrowId),
                    CONSTRAINT FK_CirculationAudit_User FOREIGN KEY (ActorUserId)
                        REFERENCES dbo.Users(Id),
                    CONSTRAINT CK_CirculationAudit_EventType CHECK (EventType IN
                        ('BorrowCreated','LegacyCopyMapped','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair','MarkedLost'))
                );
                CREATE INDEX IX_CirculationAudit_Borrow_OccurredAt
                    ON dbo.CirculationAuditEvents(BorrowId, OccurredAt, AuditEventId);
            END", connection, transaction);
        command.ExecuteNonQuery();
        using var copyForeignKey = new SqlCommand(@"
            IF OBJECT_ID('dbo.BookCopies', 'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CirculationAudit_BookCopy')
                ALTER TABLE dbo.CirculationAuditEvents ADD CONSTRAINT FK_CirculationAudit_BookCopy
                    FOREIGN KEY (BookCopyId) REFERENCES dbo.BookCopies(CopyId);", connection, transaction);
        copyForeignKey.ExecuteNonQuery();
        using var outcomeConstraint = new SqlCommand(@"
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CirculationAudit_EventType'
                AND (definition NOT LIKE '%ReturnedNeedsRepair%' OR definition NOT LIKE '%''Returned''%'
                    OR definition NOT LIKE '%MarkedLost%'))
                ALTER TABLE dbo.CirculationAuditEvents DROP CONSTRAINT CK_CirculationAudit_EventType;
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CirculationAudit_EventType')
                ALTER TABLE dbo.CirculationAuditEvents ADD CONSTRAINT CK_CirculationAudit_EventType CHECK (EventType IN
                ('BorrowCreated','LegacyCopyMapped','Returned','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair','MarkedLost'));", connection, transaction);
        outcomeConstraint.ExecuteNonQuery();
        transaction.Commit();
    }
}
