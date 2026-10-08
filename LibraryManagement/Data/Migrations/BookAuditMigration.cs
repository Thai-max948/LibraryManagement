using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class BookAuditMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        Apply(connection);
    }

    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using (var created = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'CreatedAt') IS NULL
                ALTER TABLE dbo.Books ADD CreatedAt DATETIME2 NOT NULL
                    CONSTRAINT DF_Books_CreatedAt DEFAULT SYSUTCDATETIME();", connection, transaction))
            created.ExecuteNonQuery();
        using (var updated = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'UpdatedAt') IS NULL
                ALTER TABLE dbo.Books ADD UpdatedAt DATETIME2 NULL;", connection, transaction))
            updated.ExecuteNonQuery();
    }

    public static bool HasCreatedAt(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'CreatedAt') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public static bool HasUpdatedAt(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'UpdatedAt') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
