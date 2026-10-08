using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class BookMetadataMigration
{
    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using (var publisher = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'Publisher') IS NULL
                ALTER TABLE dbo.Books ADD Publisher NVARCHAR(150) NULL;", connection, transaction))
            publisher.ExecuteNonQuery();
        using (var language = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'Language') IS NULL
                ALTER TABLE dbo.Books ADD Language NVARCHAR(50) NULL;", connection, transaction))
            language.ExecuteNonQuery();
    }

    public static bool HasPublisher(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'Publisher') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public static bool HasLanguage(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'Language') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
