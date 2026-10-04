using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class ReturnOutcomeMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var migrationLock = new SqlCommand(@"
            DECLARE @Result int;
            EXEC @Result = sp_getapplock @Resource = 'LibraryManagement.ReturnOutcomeMigration',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @Result;", connection, transaction);
        if (System.Convert.ToInt32(migrationLock.ExecuteScalar()) < 0)
            throw new System.InvalidOperationException("Không thể khóa cập nhật kết quả trả sách.");

        using (var addColumns = new SqlCommand(@"
            IF COL_LENGTH('dbo.BorrowRecords', 'ReturnCondition') IS NULL
                ALTER TABLE dbo.BorrowRecords ADD ReturnCondition NVARCHAR(20) NULL;
            IF COL_LENGTH('dbo.BorrowRecords', 'ConditionNote') IS NULL
                ALTER TABLE dbo.BorrowRecords ADD ConditionNote NVARCHAR(500) NULL;
            IF COL_LENGTH('dbo.BorrowRecords', 'LostDate') IS NULL
                ALTER TABLE dbo.BorrowRecords ADD LostDate DATETIME NULL;
            IF COL_LENGTH('dbo.BorrowRecords', 'LostNote') IS NULL
                ALTER TABLE dbo.BorrowRecords ADD LostNote NVARCHAR(500) NULL;", connection, transaction))
        {
            addColumns.ExecuteNonQuery();
        }

        // Older databases may constrain BorrowRecords.Status without the Lost outcome.
        using (var updateStatusConstraint = new SqlCommand(@"
            IF EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID('dbo.BorrowRecords')
                  AND name = 'CHK_BorrowRecords_Status'
                  AND definition NOT LIKE '%Lost%')
                ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CHK_BorrowRecords_Status;
            IF NOT EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID('dbo.BorrowRecords')
                  AND name = 'CHK_BorrowRecords_Status')
                ALTER TABLE dbo.BorrowRecords ADD CONSTRAINT CHK_BorrowRecords_Status
                    CHECK (Status IN ('Borrowing', 'Returned', 'Overdue', 'Lost'));", connection, transaction))
        {
            updateStatusConstraint.ExecuteNonQuery();
        }

        // Compile the constraint batch only after ReturnCondition exists on older schemas.
        using (var updateConstraint = new SqlCommand(@"
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BorrowRecords_ReturnCondition' AND definition NOT LIKE '%NeedsRepair%')
                ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CK_BorrowRecords_ReturnCondition;
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BorrowRecords_ReturnCondition')
                ALTER TABLE dbo.BorrowRecords ADD CONSTRAINT CK_BorrowRecords_ReturnCondition
                    CHECK (ReturnCondition IS NULL OR ReturnCondition IN ('Normal', 'Damaged', 'NeedsRepair'));", connection, transaction))
        {
            updateConstraint.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
