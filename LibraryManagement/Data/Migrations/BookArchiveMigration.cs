using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class BookArchiveMigration
{
    public static bool HasSchema(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'Status') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using (var status = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'Status') IS NULL
                ALTER TABLE dbo.Books ADD Status NVARCHAR(20) NOT NULL
                    CONSTRAINT DF_Books_Status DEFAULT 'Active';", connection, transaction))
            status.ExecuteNonQuery();
        using (var archivedAt = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'ArchivedAt') IS NULL
                ALTER TABLE dbo.Books ADD ArchivedAt DATETIME2 NULL;", connection, transaction))
            archivedAt.ExecuteNonQuery();
        using (var check = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Books_Status')
                ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_Status
                    CHECK (Status IN ('Active', 'Archived'));", connection, transaction))
            check.ExecuteNonQuery();
    }
}
