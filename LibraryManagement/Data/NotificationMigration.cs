using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class NotificationMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        Apply(connection);
    }

    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand(@"
            IF OBJECT_ID(N'dbo.Notifications', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Notifications (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
                    Title NVARCHAR(200) NOT NULL,
                    Message NVARCHAR(1000) NOT NULL,
                    Type INT NOT NULL,
                    SourceModule NVARCHAR(40) NOT NULL,
                    SourceEntityType NVARCHAR(80) NULL,
                    SourceEntityId NVARCHAR(100) NULL,
                    IdempotencyKey NVARCHAR(200) NULL,
                    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
                    IsRead BIT NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT 0
                );
            END;", connection, transaction);
        command.ExecuteNonQuery();
        // Keep the additive column in its own completed batch before creating an index that uses it.
        using var column = new SqlCommand(@"
            IF COL_LENGTH('dbo.Notifications', 'IdempotencyKey') IS NULL
                ALTER TABLE dbo.Notifications ADD IdempotencyKey NVARCHAR(200) NULL;", connection, transaction);
        column.ExecuteNonQuery();
        using var index = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_IsRead_CreatedAt')
                CREATE INDEX IX_Notifications_IsRead_CreatedAt ON dbo.Notifications (IsRead, CreatedAt DESC);", connection, transaction);
        index.ExecuteNonQuery();
        using var idempotencyIndex = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'UX_Notifications_IdempotencyKey')
                CREATE UNIQUE INDEX UX_Notifications_IdempotencyKey ON dbo.Notifications (IdempotencyKey)
                    WHERE IdempotencyKey IS NOT NULL;", connection, transaction);
        idempotencyIndex.ExecuteNonQuery();
    }
}
