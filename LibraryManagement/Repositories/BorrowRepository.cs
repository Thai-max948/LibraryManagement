using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class BorrowRepository : IDueSoonLoanRepository
    {
        public async Task<IReadOnlyList<DueSoonLoan>> GetActiveLoansDueOnAsync(DateTime targetDate, CancellationToken cancellationToken = default)
        {
            await using var connection = Database.GetConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(@"SELECT br.BorrowId, br.ReaderId, b.Title, br.DueDate
                FROM dbo.BorrowRecords br
                INNER JOIN dbo.Books b ON b.BookId = br.BookId
                WHERE br.Status = N'Borrowing' AND br.ReturnDate IS NULL
                  AND br.DueDate >= @StartDate AND br.DueDate < @EndDate
                ORDER BY br.BorrowId", connection);
            var window = DueSoonDatePolicy.GetDayWindow(targetDate);
            command.Parameters.Add("@StartDate", System.Data.SqlDbType.DateTime2).Value = window.StartInclusive;
            command.Parameters.Add("@EndDate", System.Data.SqlDbType.DateTime2).Value = window.EndExclusive;
            var loans = new List<DueSoonLoan>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                loans.Add(new DueSoonLoan(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetDateTime(3)));
            return loans;
        }

        public virtual List<BorrowRecord> GetAll()
        {
            var records = new List<BorrowRecord>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = $@"SELECT {SelectColumns(conn, null)}
                               {FromClause(conn, null)}";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(BorrowRecordMapper.Map(reader));
                    }
                }
            }
            return records;
        }

        public virtual BorrowRecord? GetById(int borrowId)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                return GetById(conn, null, borrowId);
            }
        }

        public virtual BorrowRecord? GetById(SqlConnection conn, SqlTransaction? tran, int borrowId)
        {
            string sql = $@"SELECT {SelectColumns(conn, tran)}
                           {FromClause(conn, tran)} WHERE br.BorrowId = @BorrowId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BorrowId", borrowId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return BorrowRecordMapper.Map(reader);
                    }
                }
            }
            return null;
        }

        public virtual BorrowRecord? GetActiveByCopyId(int copyId)
        {
            using var connection = Database.GetConnection();
            connection.Open();
            using var command = new SqlCommand($@"SELECT {SelectColumns(connection, null)}
                {FromClause(connection, null)} WHERE br.CopyId = @CopyId AND br.Status = 'Borrowing'", connection);
            command.Parameters.Add("@CopyId", System.Data.SqlDbType.Int).Value = copyId;
            using var reader = command.ExecuteReader();
            return reader.Read() ? BorrowRecordMapper.Map(reader) : null;
        }

        public virtual BorrowRecord? GetByIdForReturn(SqlConnection conn, SqlTransaction tran, int borrowId)
        {
            using var command = new SqlCommand(@"SELECT br.BorrowId, br.BookId, br.CopyId AS BookCopyId,
                br.ReaderId, br.BorrowDate, br.DueDate, br.LoanPeriodDaysApplied,
                br.ReturnCondition, br.ConditionNote, br.LostDate, br.LostNote, br.ReturnDate, br.Status
                FROM dbo.BorrowRecords br WITH (UPDLOCK, ROWLOCK)
                WHERE br.BorrowId = @BorrowId", conn, tran);
            command.Parameters.Add("@BorrowId", System.Data.SqlDbType.Int).Value = borrowId;
            using var reader = command.ExecuteReader();
            return reader.Read() ? BorrowRecordMapper.Map(reader) : null;
        }

        public virtual BorrowRecord? GetActiveByCopyId(SqlConnection conn, SqlTransaction tran, int copyId)
        {
            using var command = new SqlCommand(@"SELECT TOP (1) br.BorrowId, br.BookId, br.CopyId AS BookCopyId,
                br.ReaderId, br.BorrowDate, br.DueDate, br.LoanPeriodDaysApplied,
                br.ReturnCondition, br.ConditionNote, br.LostDate, br.LostNote, br.ReturnDate, br.Status
                FROM dbo.BorrowRecords br WITH (READCOMMITTEDLOCK)
                WHERE br.CopyId = @CopyId AND br.Status = 'Borrowing'", conn, tran);
            command.Parameters.Add("@CopyId", System.Data.SqlDbType.Int).Value = copyId;
            using var reader = command.ExecuteReader();
            return reader.Read() ? BorrowRecordMapper.Map(reader) : null;
        }

        public virtual int Add(SqlConnection conn, SqlTransaction? tran, BorrowRecord record)
        {
            if (tran == null)
                throw new InvalidOperationException("New loans require the authoritative circulation transaction.");
            if (!HasCopyIdColumn(conn, tran) || !HasBookCopiesSchema(conn, tran) || !record.BookCopyId.HasValue)
                throw new InvalidOperationException("A new borrow record must reference a physical BookCopy.");
            const string sql = @"INSERT INTO dbo.BorrowRecords
                (BookId, CopyId, ReaderId, BorrowDate, DueDate, LoanPeriodDaysApplied, ReturnDate, Status,
                 ReaderNameSnapshot, BookTitleSnapshot, BarcodeSnapshot)
                OUTPUT INSERTED.BorrowId
                SELECT c.BookId, c.CopyId, r.ReaderId, @BorrowDate, @DueDate, @LoanPeriodDaysApplied, @ReturnDate, @Status,
                       r.FullName, b.Title, c.Barcode
                FROM dbo.BookCopies c
                INNER JOIN dbo.Books b ON b.BookId = c.BookId
                INNER JOIN dbo.Readers r ON r.ReaderId = @ReaderId
                WHERE c.CopyId = @CopyId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@CopyId", record.BookCopyId.Value);
                cmd.Parameters.AddWithValue("@ReaderId", record.ReaderId);
                cmd.Parameters.AddWithValue("@BorrowDate", record.BorrowDate);
                cmd.Parameters.AddWithValue("@DueDate", record.DueDate);
                cmd.Parameters.AddWithValue("@LoanPeriodDaysApplied", (object?)record.LoanPeriodDaysApplied ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ReturnDate", (object?)record.ReturnDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", record.Status);
                return cmd.ExecuteScalar() is int borrowId
                    ? borrowId
                    : throw new InvalidOperationException("The selected BookCopy no longer exists.");
            }
        }

        public virtual bool Update(BorrowRecord record)
        {
            throw new NotSupportedException("Circulation history cannot be rewritten through Update. Use the audited Borrow/Return operations.");
        }

        public virtual bool Update(SqlConnection conn, SqlTransaction? tran, BorrowRecord record)
        {
            throw new NotSupportedException("Circulation history cannot be rewritten through Update. Use the audited Borrow/Return operations.");
        }

        public virtual List<BorrowRecord> GetBorrowingRecords()
        {
            var records = new List<BorrowRecord>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = $@"SELECT {SelectColumns(conn, null)}
                               {FromClause(conn, null)}
                               WHERE br.Status = @Status";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", "Borrowing");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(BorrowRecordMapper.Map(reader));
                        }
                    }
                }
            }
            return records;
        }

        public virtual List<BorrowRecord> GetHistory(
            int? readerId = null,
            int? bookId = null,
            string? status = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var records = new List<BorrowRecord>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = $@"SELECT {SelectColumns(conn, null)}
                               {FromClause(conn, null)}
                               WHERE 1 = 1";

                if (readerId.HasValue)
                {
                    sql += " AND br.ReaderId = @ReaderId";
                }
                if (bookId.HasValue)
                {
                    sql += $" AND {BookIdExpression(conn, null)} = @BookId";
                }
                if (!string.IsNullOrEmpty(status))
                {
                    sql += " AND br.Status = @Status";
                }
                if (fromDate.HasValue)
                {
                    sql += " AND br.BorrowDate >= @FromDate";
                }
                if (toDate.HasValue)
                {
                    sql += " AND br.BorrowDate <= @ToDate";
                }

                sql += " ORDER BY ISNULL(br.ReturnDate, br.BorrowDate) DESC, br.BorrowId DESC";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    if (readerId.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@ReaderId", readerId.Value);
                    }
                    if (bookId.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@BookId", bookId.Value);
                    }
                    if (!string.IsNullOrEmpty(status))
                    {
                        cmd.Parameters.AddWithValue("@Status", status);
                    }
                    if (fromDate.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@FromDate", fromDate.Value);
                    }
                    if (toDate.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@ToDate", toDate.Value);
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(BorrowRecordMapper.Map(reader));
                        }
                    }
                }
            }
            return records;
        }

        public virtual int CountActiveBorrowsByReader(SqlConnection conn, SqlTransaction? tran, int readerId)
        {
            string sql = "SELECT COUNT(*) FROM BorrowRecords WHERE ReaderId = @ReaderId AND Status = @Status";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@ReaderId", readerId);
                cmd.Parameters.AddWithValue("@Status", "Borrowing");
                return (int)cmd.ExecuteScalar();
            }
        }

        public virtual List<BorrowRecord> GetEligibilityRecords(SqlConnection conn, SqlTransaction tran, int readerId)
        {
            var records = new List<BorrowRecord>();
            using var command = new SqlCommand(@"
                SELECT BorrowId, ReaderId, DueDate, Status FROM dbo.BorrowRecords
                WHERE ReaderId = @ReaderId AND Status = 'Borrowing'", conn, tran);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            using var reader = command.ExecuteReader();
            while (reader.Read()) records.Add(new BorrowRecord
            {
                BorrowId = reader.GetInt32(0), ReaderId = reader.GetInt32(1),
                DueDate = reader.GetDateTime(2), Status = reader.GetString(3)
            });
            return records;
        }

        public virtual List<BorrowRecord> GetEligibilityRecords(int readerId)
        {
            var records = new List<BorrowRecord>();
            using var connection = Database.GetConnection();
            connection.Open();
            using var command = new SqlCommand(@"
                SELECT BorrowId, ReaderId, DueDate, Status
                FROM dbo.BorrowRecords
                WHERE ReaderId = @ReaderId AND Status = 'Borrowing'", connection);
            command.Parameters.Add("@ReaderId", System.Data.SqlDbType.Int).Value = readerId;
            using var reader = command.ExecuteReader();
            while (reader.Read()) records.Add(new BorrowRecord
            {
                BorrowId = reader.GetInt32(0),
                ReaderId = reader.GetInt32(1),
                DueDate = reader.GetDateTime(2),
                Status = reader.GetString(3)
            });
            return records;
        }

        public virtual bool HasUnresolvedLostByReader(SqlConnection conn, SqlTransaction? tran, int readerId)
        {
            using var command = new SqlCommand(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.BorrowRecords WHERE ReaderId = @ReaderId AND Status = 'Lost') THEN 1 ELSE 0 END",
                conn, tran);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        public virtual bool MarkAsReturned(SqlConnection conn, SqlTransaction? tran, int borrowId, DateTime returnDate,
            ReturnCondition condition, string? note)
        {
            string sql = @"UPDATE BorrowRecords SET Status = @StatusReturned, ReturnDate = @ReturnDate,
                           ReturnCondition = @Condition, ConditionNote = @Note, LostDate = NULL, LostNote = NULL
                           WHERE BorrowId = @BorrowId AND Status = @StatusBorrowing";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BorrowId", borrowId);
                cmd.Parameters.AddWithValue("@ReturnDate", returnDate);
                cmd.Parameters.AddWithValue("@Condition", condition.ToString());
                cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StatusReturned", "Returned");
                cmd.Parameters.AddWithValue("@StatusBorrowing", "Borrowing");
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public virtual bool MarkAsLost(SqlConnection conn, SqlTransaction? tran, int borrowId, DateTime lostDate, string? note)
        {
            using var command = new SqlCommand(@"
                UPDATE dbo.BorrowRecords SET Status = 'Lost', LostDate = @LostDate, LostNote = @LostNote,
                    ReturnDate = NULL, ReturnCondition = NULL, ConditionNote = NULL
                WHERE BorrowId = @BorrowId AND Status = 'Borrowing'", conn, tran);
            command.Parameters.AddWithValue("@BorrowId", borrowId);
            command.Parameters.AddWithValue("@LostDate", lostDate);
            command.Parameters.AddWithValue("@LostNote", (object?)note ?? DBNull.Value);
            return command.ExecuteNonQuery() == 1;
        }

        public virtual bool LinkLegacyBorrowToCopy(SqlConnection conn, SqlTransaction tran, int borrowId, int bookId, int copyId)
        {
            const string sql = @"UPDATE dbo.BorrowRecords
                SET CopyId = @CopyId
                WHERE BorrowId = @BorrowId AND BookId = @BookId
                  AND Status = 'Borrowing' AND CopyId IS NULL
                  AND EXISTS (SELECT 1 FROM dbo.BookCopies c
                              WHERE c.CopyId = @CopyId AND c.BookId = @BookId AND c.Status = 'Borrowed')";
            using var command = new SqlCommand(sql, conn, tran);
            command.Parameters.AddWithValue("@BorrowId", borrowId);
            command.Parameters.AddWithValue("@BookId", bookId);
            command.Parameters.AddWithValue("@CopyId", copyId);
            return command.ExecuteNonQuery() == 1;
        }

        private static bool HasCopyIdColumn(SqlConnection conn, SqlTransaction? tran)
        {
            using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.BorrowRecords', 'CopyId') IS NULL THEN 0 ELSE 1 END", conn, tran);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static bool HasBookCopiesSchema(SqlConnection conn, SqlTransaction? tran)
        {
            using var command = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.BookCopies', 'U') IS NOT NULL THEN 1 ELSE 0 END", conn, tran);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static string SelectColumns(SqlConnection conn, SqlTransaction? tran)
        {
            bool hasCopySchema = HasCopyIdColumn(conn, tran) && HasBookCopiesSchema(conn, tran);
            string bookId = hasCopySchema ? "COALESCE(bc.BookId, br.BookId)" : "br.BookId";
            string copyId = HasCopyIdColumn(conn, tran) ? "br.CopyId" : "CAST(NULL AS INT)";
            string period = HasLoanPeriodColumn(conn, tran) ? "br.LoanPeriodDaysApplied" : "CAST(NULL AS INT)";
            bool hasOutcome = HasOutcomeColumns(conn, tran);
            string condition = hasOutcome ? "br.ReturnCondition" : "CAST(NULL AS NVARCHAR(20))";
            string note = hasOutcome ? "br.ConditionNote" : "CAST(NULL AS NVARCHAR(500))";
            string lostDate = hasOutcome ? "br.LostDate" : "CAST(NULL AS DATETIME)";
            string lostNote = hasOutcome ? "br.LostNote" : "CAST(NULL AS NVARCHAR(500))";
            return $"br.BorrowId, {bookId} AS BookId, {copyId} AS BookCopyId, br.ReaderId, br.BorrowDate, br.DueDate, {period} AS LoanPeriodDaysApplied, {condition} AS ReturnCondition, {note} AS ConditionNote, {lostDate} AS LostDate, {lostNote} AS LostNote, br.ReturnDate, br.Status";
        }

        private static bool HasOutcomeColumns(SqlConnection conn, SqlTransaction? tran)
        {
            using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.BorrowRecords', 'LostNote') IS NULL THEN 0 ELSE 1 END", conn, tran);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static bool HasColumn(SqlConnection connection, SqlTransaction? transaction, string columnName)
        {
            using var command = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.BorrowRecords', @ColumnName) IS NULL THEN 0 ELSE 1 END",
                connection, transaction);
            command.Parameters.Add("@ColumnName", System.Data.SqlDbType.NVarChar, 128).Value = columnName;
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static bool HasLoanPeriodColumn(SqlConnection conn, SqlTransaction? tran)
        {
            using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.BorrowRecords', 'LoanPeriodDaysApplied') IS NULL THEN 0 ELSE 1 END", conn, tran);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static string FromClause(SqlConnection conn, SqlTransaction? tran) =>
            HasCopyIdColumn(conn, tran) && HasBookCopiesSchema(conn, tran)
                ? "FROM dbo.BorrowRecords br LEFT JOIN dbo.BookCopies bc ON bc.CopyId = br.CopyId"
                : "FROM dbo.BorrowRecords br";

        private static string BookIdExpression(SqlConnection conn, SqlTransaction? tran) =>
            HasCopyIdColumn(conn, tran) && HasBookCopiesSchema(conn, tran)
                ? "COALESCE(bc.BookId, br.BookId)"
                : "br.BookId";
    }
}
