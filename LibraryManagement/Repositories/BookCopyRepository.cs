using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class BookCopyRepository
{
    public virtual IReadOnlyList<int> AddGeneratedCopies(int bookId, int quantity, string status, string condition)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            EnsureActiveBook(connection, transaction, bookId);
            BookCopyBarcodeMigration.Apply(connection, transaction);
            var ids = new List<int>(quantity);
            for (int index = 0; index < quantity; index++)
                ids.Add(InsertGeneratedCopy(connection, transaction, bookId, status, condition));
            transaction.Commit();
            return ids;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public virtual int InsertGeneratedCopy(SqlConnection connection, SqlTransaction transaction, int bookId,
        string status = BookCopyStatuses.Available, string condition = "Good")
    {
        using var insert = new SqlCommand(@"INSERT INTO dbo.BookCopies (BookId, Barcode, Status, Condition)
            OUTPUT INSERTED.CopyId VALUES (@BookId, NULL, @Status, @Condition)", connection, transaction);
        insert.Parameters.AddWithValue("@BookId", bookId);
        insert.Parameters.AddWithValue("@Status", status);
        insert.Parameters.AddWithValue("@Condition", condition);
        int copyId = Convert.ToInt32(insert.ExecuteScalar());
        using var update = new SqlCommand(@"UPDATE dbo.BookCopies SET Barcode = @Barcode
            WHERE CopyId = @CopyId AND Barcode IS NULL", connection, transaction);
        update.Parameters.AddWithValue("@CopyId", copyId);
        update.Parameters.AddWithValue("@Barcode", BookCopyBarcode.Format(copyId));
        if (update.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Không thể tạo barcode cho bản sách.");
        return copyId;
    }

    public virtual bool HasSchema(SqlConnection connection, SqlTransaction? transaction)
    {
        using var command = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID('dbo.BookCopies', 'U') IS NOT NULL
            AND COL_LENGTH('dbo.BorrowRecords', 'CopyId') IS NOT NULL THEN 1 ELSE 0 END", connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public virtual bool SetStatus(int copyId, string status)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            int? parentBookId;
            using (var parent = new SqlCommand("SELECT BookId FROM dbo.BookCopies WHERE CopyId = @CopyId", connection, transaction))
            {
                parent.Parameters.AddWithValue("@CopyId", copyId);
                var value = parent.ExecuteScalar();
                parentBookId = value == null ? null : Convert.ToInt32(value);
            }
            if (!parentBookId.HasValue) { transaction.Rollback(); return false; }
            EnsureActiveBook(connection, transaction, parentBookId.Value);
            int bookId;
            string currentStatus;
            string condition;
            using (var read = new SqlCommand("SELECT BookId, Status, Condition FROM BookCopies WITH (UPDLOCK, ROWLOCK) WHERE CopyId = @CopyId", connection, transaction))
            {
                read.Parameters.AddWithValue("@CopyId", copyId);
                using var reader = read.ExecuteReader();
                if (!reader.Read()) { transaction.Rollback(); return false; }
                bookId = reader.GetInt32(0);
                currentStatus = reader.GetString(1);
                condition = reader.GetString(2);
            }
            if (status == BookCopyStatuses.Available && condition == "LegacyUnverified")
                throw new InvalidOperationException("Cần đối chiếu barcode và gắn phiếu cũ trước khi đưa bản LegacyUnverified vào lưu thông.");
            if (!BookCopyStatusRules.CanChangeManually(currentStatus, status))
                throw new InvalidOperationException($"Không thể chuyển bản sách từ {currentStatus} sang {status}.");
            if (currentStatus == status) { transaction.Commit(); return true; }

            using (var update = new SqlCommand("UPDATE BookCopies SET Status = @Status WHERE CopyId = @CopyId", connection, transaction))
            {
                update.Parameters.AddWithValue("@CopyId", copyId);
                update.Parameters.AddWithValue("@Status", status);
                if (update.ExecuteNonQuery() != 1) { transaction.Rollback(); return false; }
            }

            transaction.Commit();
            return true;
        }
        catch { transaction.Rollback(); throw; }
    }

    public virtual List<BookCopy> GetByBookId(int bookId)
    {
        var copies = new List<BookCopy>();
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT CopyId, BookId, Barcode, Status, Condition, CreatedAt FROM BookCopies WHERE BookId = @BookId ORDER BY CopyId", connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) copies.Add(Map(reader));
        return copies;
    }

    public virtual List<BookCopy> GetAvailableByBookId(int bookId)
    {
        var copies = new List<BookCopy>();
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"SELECT CopyId, BookId, Barcode, Status, Condition, CreatedAt
            FROM BookCopies WHERE BookId = @BookId AND Status = @Status AND Condition <> 'LegacyUnverified' ORDER BY Barcode", connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Status", BookCopyStatuses.Available);
        using var reader = command.ExecuteReader();
        while (reader.Read()) copies.Add(Map(reader));
        return copies;
    }

    public virtual BookCopy? GetById(SqlConnection connection, SqlTransaction? transaction, int copyId)
    {
        using var command = new SqlCommand(@"SELECT CopyId, BookId, Barcode, Status, Condition, CreatedAt
            FROM dbo.BookCopies WHERE CopyId = @CopyId", connection, transaction);
        command.Parameters.AddWithValue("@CopyId", copyId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public virtual BookCopy? GetById(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        return GetById(connection, null, copyId);
    }

    public virtual BookCopy? GetByIdForReturn(SqlConnection connection, SqlTransaction transaction, int copyId)
    {
        using var command = new SqlCommand(@"SELECT CopyId, BookId, Barcode, Status, Condition, CreatedAt
            FROM dbo.BookCopies WITH (UPDLOCK, ROWLOCK) WHERE CopyId = @CopyId", connection, transaction);
        command.Parameters.Add("@CopyId", System.Data.SqlDbType.Int).Value = copyId;
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public virtual BookCopy? GetByBarcode(string barcode)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        return GetByBarcode(connection, null, barcode, lockForUpdate: false);
    }

    public virtual BookCopy? GetByBarcode(SqlConnection connection, SqlTransaction? transaction,
        string barcode, bool lockForUpdate)
    {
        string lockHint = lockForUpdate ? " WITH (UPDLOCK, ROWLOCK)" : string.Empty;
        using var command = new SqlCommand($@"SELECT CopyId, BookId, Barcode, Status, Condition, CreatedAt
            FROM dbo.BookCopies{lockHint}
            WHERE Barcode = @Barcode
              AND Barcode COLLATE Latin1_General_100_BIN2 = @Barcode COLLATE Latin1_General_100_BIN2
              AND DATALENGTH(Barcode) = DATALENGTH(@Barcode)", connection, transaction);
        command.Parameters.Add("@Barcode", System.Data.SqlDbType.NVarChar, 100).Value = barcode;
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public virtual int? GetAvailableCopyBookId(SqlConnection connection, SqlTransaction? transaction, int copyId)
    {
        using var command = new SqlCommand(@"SELECT BookId FROM dbo.BookCopies WITH (UPDLOCK, ROWLOCK)
            WHERE CopyId = @CopyId AND Status = @Status", connection, transaction);
        command.Parameters.AddWithValue("@CopyId", copyId);
        command.Parameters.AddWithValue("@Status", BookCopyStatuses.Available);
        var result = command.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public virtual int CountByBookId(SqlConnection connection, SqlTransaction? transaction, int bookId)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM BookCopies WHERE BookId = @BookId", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public virtual int CountActiveByBookId(SqlConnection connection, SqlTransaction? transaction, int bookId)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM BookCopies WHERE BookId = @BookId AND Status <> @Retired", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Retired", BookCopyStatuses.Retired);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public virtual int RetireAvailableCopies(SqlConnection connection, SqlTransaction? transaction, int bookId, int count)
    {
        using var command = new SqlCommand(@"WITH AvailableCopies AS (
                SELECT TOP (@Count) CopyId FROM BookCopies
                WHERE BookId = @BookId AND Status = @Available ORDER BY CopyId DESC
            )
            UPDATE BookCopies SET Status = @Retired WHERE CopyId IN (SELECT CopyId FROM AvailableCopies)", connection, transaction);
        command.Parameters.AddWithValue("@Count", count);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Available", BookCopyStatuses.Available);
        command.Parameters.AddWithValue("@Retired", BookCopyStatuses.Retired);
        return command.ExecuteNonQuery();
    }

    public virtual int CountAvailableByBookId(SqlConnection connection, SqlTransaction? transaction, int bookId)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM BookCopies WHERE BookId = @BookId AND Status = @Status", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Status", BookCopyStatuses.Available);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public virtual void AddCopies(SqlConnection connection, SqlTransaction? transaction, int bookId, int count)
    {
        if (transaction == null) throw new InvalidOperationException("Transaction required for copy creation.");
        BookCopyBarcodeMigration.Apply(connection, transaction);
        for (int i = 0; i < count; i++)
            InsertGeneratedCopy(connection, transaction, bookId);
    }

    public virtual int? GetFirstAvailableCopyId(SqlConnection connection, SqlTransaction? transaction, int bookId)
    {
        using var command = new SqlCommand("SELECT TOP (1) CopyId FROM BookCopies WITH (UPDLOCK, ROWLOCK) WHERE BookId = @BookId AND Status = @Status ORDER BY CopyId", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Status", BookCopyStatuses.Available);
        var result = command.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public virtual bool UpdateStatus(SqlConnection connection, SqlTransaction? transaction, int copyId, string expectedStatus, string newStatus)
    {
        using var command = new SqlCommand("UPDATE BookCopies SET Status = @NewStatus WHERE CopyId = @CopyId AND Status = @ExpectedStatus", connection, transaction);
        command.Parameters.AddWithValue("@CopyId", copyId);
        command.Parameters.AddWithValue("@ExpectedStatus", expectedStatus);
        command.Parameters.AddWithValue("@NewStatus", newStatus);
        return command.ExecuteNonQuery() == 1;
    }

    public virtual bool TryClaimForBorrow(SqlConnection connection, SqlTransaction transaction, int copyId)
    {
        using var command = new SqlCommand(@"UPDATE dbo.BookCopies SET Status = 'Borrowed'
            WHERE CopyId = @CopyId AND Status = 'Available' AND Condition <> 'LegacyUnverified'", connection, transaction);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return command.ExecuteNonQuery() == 1;
    }

    public virtual void ConfirmLegacyCopy(SqlConnection connection, SqlTransaction transaction, int copyId)
    {
        using var command = new SqlCommand(@"UPDATE dbo.BookCopies SET Condition = 'Good'
            WHERE CopyId = @CopyId AND Status = 'Borrowed' AND Condition = 'LegacyUnverified'", connection, transaction);
        command.Parameters.AddWithValue("@CopyId", copyId);
        command.ExecuteNonQuery();
    }

    private static void EnsureActiveBook(SqlConnection connection, SqlTransaction transaction, int bookId)
    {
        bool hasStatus = BookArchiveMigration.HasSchema(connection, transaction);
        using var command = new SqlCommand(hasStatus
            ? "SELECT Status FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) WHERE BookId = @BookId"
            : "SELECT CAST('Active' AS NVARCHAR(20)) FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) WHERE BookId = @BookId", connection, transaction);
        command.Parameters.AddWithValue("@BookId", bookId);
        string? status = command.ExecuteScalar() as string;
        if (status == null) throw new InvalidOperationException("Book does not exist.");
        if (status != BookStatuses.Active) throw new InvalidOperationException("Đầu sách đã được lưu trữ; cần khôi phục trước khi quản lý bản sách.");
    }

    private static BookCopy Map(SqlDataReader reader) => new()
    {
        CopyId = reader.GetInt32(reader.GetOrdinal("CopyId")),
        BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
        Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
        Status = reader.GetString(reader.GetOrdinal("Status")),
        Condition = reader.GetString(reader.GetOrdinal("Condition")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
    };
}
