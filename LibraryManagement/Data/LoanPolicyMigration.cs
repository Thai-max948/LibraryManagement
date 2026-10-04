using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class LoanPolicyMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var migrationLock = new SqlCommand(@"
            DECLARE @Result int;
            EXEC @Result = sp_getapplock @Resource = 'LibraryManagement.LoanPolicyMigration',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @Result;", connection, transaction);
        if (System.Convert.ToInt32(migrationLock.ExecuteScalar()) < 0)
            throw new System.InvalidOperationException("Không thể khóa cập nhật chính sách mượn.");
        using var command = new SqlCommand(@"
            IF OBJECT_ID('dbo.LoanPolicies', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.LoanPolicies (
                    LoanPolicyId INT IDENTITY(1,1) PRIMARY KEY,
                    ReaderType NVARCHAR(50) NOT NULL,
                    LoanPeriodDays INT NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_LoanPolicies_IsActive DEFAULT 1,
                    CONSTRAINT UQ_LoanPolicies_ReaderType UNIQUE (ReaderType),
                    CONSTRAINT CK_LoanPolicies_LoanPeriodDays CHECK (LoanPeriodDays > 0)
                );
                INSERT INTO dbo.LoanPolicies (ReaderType, LoanPeriodDays)
                VALUES ('Student', 14), ('External', 7);
            END", connection, transaction);
        command.ExecuteNonQuery();
        using var column = new SqlCommand(@"
            IF COL_LENGTH('dbo.BorrowRecords', 'LoanPeriodDaysApplied') IS NULL
                ALTER TABLE dbo.BorrowRecords ADD LoanPeriodDaysApplied INT NULL", connection, transaction);
        column.ExecuteNonQuery();
        transaction.Commit();
    }
}
