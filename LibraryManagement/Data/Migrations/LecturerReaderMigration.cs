using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

/// <summary>Adds lecturer-only reader fields without rewriting existing reader rows.</summary>
public static class LecturerReaderMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var migrationLock = new SqlCommand(@"
            DECLARE @Result int;
            EXEC @Result = sp_getapplock @Resource = 'LibraryManagement.LecturerReaderMigration',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @Result;", connection, transaction);
        if (System.Convert.ToInt32(migrationLock.ExecuteScalar()) < 0)
            throw new System.InvalidOperationException("Không thể khóa cập nhật cấu trúc độc giả.");

        using (var ensureReaderTable = new SqlCommand(@"
            IF OBJECT_ID('dbo.Readers', 'U') IS NULL
                THROW 51020, 'Lecturer reader migration requires dbo.Readers.', 1;",
            connection, transaction))
        {
            ensureReaderTable.ExecuteNonQuery();
        }

        using (var addLecturerCode = new SqlCommand(@"
            IF COL_LENGTH('dbo.Readers', 'LecturerCode') IS NULL
                ALTER TABLE dbo.Readers ADD LecturerCode NVARCHAR(50) COLLATE Latin1_General_100_CI_AS NULL;",
            connection, transaction))
        {
            addLecturerCode.ExecuteNonQuery();
        }

        using (var addDepartment = new SqlCommand(@"
            IF COL_LENGTH('dbo.Readers', 'Department') IS NULL
                ALTER TABLE dbo.Readers ADD Department NVARCHAR(150) NULL;",
            connection, transaction))
        {
            addDepartment.ExecuteNonQuery();
        }

        using (var ensureUniqueIndex = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID('dbo.Readers') AND name = 'UX_Readers_LecturerCode_Active')
                CREATE UNIQUE INDEX UX_Readers_LecturerCode_Active
                    ON dbo.Readers(LecturerCode)
                    WHERE ReaderType = N'Lecturer' AND LecturerCode IS NOT NULL AND IsDeleted = 0;",
            connection, transaction))
        {
            ensureUniqueIndex.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
