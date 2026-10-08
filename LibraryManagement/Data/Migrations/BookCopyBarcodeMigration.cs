using Microsoft.Data.SqlClient;
using System.Data;

namespace LibraryManagement.Data;

public static class BookCopyBarcodeMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            Apply(connection, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Existing values are normalized from CopyId. New copies may be NULL only
    // between INSERT and the identity-based UPDATE inside the caller's transaction.
    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        if (ScalarInt(connection, transaction, "SELECT CASE WHEN OBJECT_ID('dbo.BookCopies', 'U') IS NULL THEN 0 ELSE 1 END") == 0)
            return;

        int hasLegacyConstraint = ScalarInt(connection, transaction, @"SELECT COUNT(*) FROM sys.key_constraints
            WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UQ_BookCopies_Barcode'");
        int hasFilteredIndex = ScalarInt(connection, transaction, @"SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UX_BookCopies_Barcode_NotNull'
                AND is_unique = 1 AND has_filter = 1 AND filter_definition LIKE '%Barcode%IS NOT NULL%'");
        int barcodeIsNullable = ScalarInt(connection, transaction, @"SELECT is_nullable FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'Barcode'");
        int hasNonCanonicalBarcode = ScalarInt(connection, transaction, @"
            SELECT COUNT(*) FROM dbo.BookCopies
            WHERE Barcode IS NULL OR Barcode <> CONCAT('BK-', RIGHT(CONCAT('000000', CONVERT(VARCHAR(20), CopyId)),
                CASE WHEN LEN(CONVERT(VARCHAR(20), CopyId)) > 6
                    THEN LEN(CONVERT(VARCHAR(20), CopyId)) ELSE 6 END))");
        if (hasLegacyConstraint == 0 && hasFilteredIndex == 1 && barcodeIsNullable == 1 && hasNonCanonicalBarcode == 0)
            return;

        // Drop the old unfiltered UNIQUE constraint/index before normalizing:
        // a legacy barcode can equal another copy's target during the update.
        Execute(connection, transaction, @"
            IF EXISTS (SELECT 1 FROM sys.key_constraints
                WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UQ_BookCopies_Barcode')
                ALTER TABLE dbo.BookCopies DROP CONSTRAINT UQ_BookCopies_Barcode;");
        Execute(connection, transaction, @"
            IF EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UX_BookCopies_Barcode_NotNull')
                DROP INDEX UX_BookCopies_Barcode_NotNull ON dbo.BookCopies;");

        if (ScalarInt(connection, transaction, @"SELECT is_nullable FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'Barcode'") == 0)
            Execute(connection, transaction, "ALTER TABLE dbo.BookCopies ALTER COLUMN Barcode NVARCHAR(100) NULL;");

        // Pad to at least six digits, retaining every digit for IDs > 999999.
        Execute(connection, transaction, @"
            UPDATE dbo.BookCopies
            SET Barcode = CONCAT('BK-', RIGHT(CONCAT('000000', CONVERT(VARCHAR(20), CopyId)),
                CASE WHEN LEN(CONVERT(VARCHAR(20), CopyId)) > 6
                    THEN LEN(CONVERT(VARCHAR(20), CopyId)) ELSE 6 END))
            WHERE Barcode IS NULL OR Barcode <> CONCAT('BK-', RIGHT(CONCAT('000000', CONVERT(VARCHAR(20), CopyId)),
                CASE WHEN LEN(CONVERT(VARCHAR(20), CopyId)) > 6
                    THEN LEN(CONVERT(VARCHAR(20), CopyId)) ELSE 6 END));");

        Execute(connection, transaction, @"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UX_BookCopies_Barcode_NotNull')
                CREATE UNIQUE INDEX UX_BookCopies_Barcode_NotNull
                    ON dbo.BookCopies(Barcode) WHERE Barcode IS NOT NULL;");
    }

    private static void Execute(SqlConnection connection, SqlTransaction? transaction, string sql)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqlConnection connection, SqlTransaction? transaction, string sql)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
