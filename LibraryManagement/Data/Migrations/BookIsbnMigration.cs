using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class BookIsbnMigration
{
    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using (var column = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'ISBN') IS NULL
                ALTER TABLE dbo.Books ADD ISBN NVARCHAR(13) NULL;", connection, transaction))
            column.ExecuteNonQuery();
        using (var index = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Books')
                AND name = 'UX_Books_ISBN')
                CREATE UNIQUE INDEX UX_Books_ISBN ON dbo.Books(ISBN) WHERE ISBN IS NOT NULL;", connection, transaction))
            index.ExecuteNonQuery();
    }

    public static bool HasSchema(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'ISBN') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
