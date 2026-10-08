using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

// Book owns the current value. Existing books remain unknown rather than receiving an invented price.
public static class BookValueMigration
{
    public static void Apply()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
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

    public static void Apply(SqlConnection connection, SqlTransaction? transaction = null)
    {
        if (transaction is null)
        {
            using var ownedTransaction = connection.BeginTransaction();
            try
            {
                Apply(connection, ownedTransaction);
                ownedTransaction.Commit();
                return;
            }
            catch
            {
                ownedTransaction.Rollback();
                throw;
            }
        }

        using (var migrationLock = new SqlCommand(@"
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = 'LibraryManagement.BookValueMigration',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @lockResult < 0 THROW 51020, 'Book value migration lock unavailable', 1;", connection, transaction))
            migrationLock.ExecuteNonQuery();

        // SQL Server compiles a whole batch before executing it. Keep the ADD COLUMN
        // and the CHECK that references that column in separate commands for legacy DBs.
        using (var column = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'ReplacementValue') IS NULL
                ALTER TABLE dbo.Books ADD ReplacementValue DECIMAL(18,2) NULL;", connection, transaction))
            column.ExecuteNonQuery();
        using var check = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                           WHERE parent_object_id = OBJECT_ID('dbo.Books') AND name = 'CK_Books_ReplacementValue')
                ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_ReplacementValue
                    CHECK (ReplacementValue IS NULL OR ReplacementValue >= 0);", connection, transaction);
        check.ExecuteNonQuery();

        using (var rentalColumn = new SqlCommand(@"
            IF COL_LENGTH('dbo.Books', 'RentalPrice') IS NULL
                ALTER TABLE dbo.Books ADD RentalPrice DECIMAL(18,2) NULL;", connection, transaction))
            rentalColumn.ExecuteNonQuery();
        using var rentalCheck = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                           WHERE parent_object_id = OBJECT_ID('dbo.Books') AND name = 'CK_Books_RentalPrice')
                ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_RentalPrice
                    CHECK (RentalPrice IS NULL OR RentalPrice >= 0);", connection, transaction);
        rentalCheck.ExecuteNonQuery();
    }

    public static bool HasSchema(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'ReplacementValue') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public static bool HasRentalPriceSchema(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Books', 'RentalPrice') IS NULL THEN 0 ELSE 1 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
