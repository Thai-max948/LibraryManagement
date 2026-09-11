using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace LibraryManagement.Repositories
{
    public class BorrowRepository
    {
        private readonly Database _db;

        public BorrowRepository()
        {
            _db = new Database();
        }

        public List<BorrowRecord> GetAll()
        {
            var records = new List<BorrowRecord>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"SELECT BorrowId, BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status
                               FROM BorrowRecords";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        records.Add(MapToBorrowRecord(reader));
                }
            }
            return records;
        }

        public BorrowRecord GetById(int borrowId)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return GetById(conn, null, borrowId);
            }
        }

        // Overload dùng trong transaction (ReturnBook Bước 4) — check Status atomically trước khi update.
        public BorrowRecord GetById(SqlConnection conn, SqlTransaction tran, int borrowId)
        {
            string sql = @"SELECT BorrowId, BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status
                           FROM BorrowRecords WHERE BorrowId = @BorrowId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BorrowId", borrowId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return MapToBorrowRecord(reader);
                }
            }
            return null;
        }

        // Trả về BorrowId vừa insert.
        public int Add(BorrowRecord record)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return Add(conn, null, record);
            }
        }

        public int Add(SqlConnection conn, SqlTransaction tran, BorrowRecord record)
        {
            string sql = @"INSERT INTO BorrowRecords (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status)
                           OUTPUT INSERTED.BorrowId
                           VALUES (@BookId, @ReaderId, @BorrowDate, @DueDate, @ReturnDate, @Status)";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", record.BookId);
                cmd.Parameters.AddWithValue("@ReaderId", record.ReaderId);
                cmd.Parameters.AddWithValue("@BorrowDate", record.BorrowDate);
                cmd.Parameters.AddWithValue("@DueDate", record.DueDate);
                cmd.Parameters.AddWithValue("@ReturnDate", (object)record.ReturnDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", record.Status);
                return (int)cmd.ExecuteScalar();
            }
        }

        // Trả về false nếu BorrowId không tồn tại.
        public bool Update(BorrowRecord record)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return Update(conn, null, record);
            }
        }

        public bool Update(SqlConnection conn, SqlTransaction tran, BorrowRecord record)
        {
            string sql = @"UPDATE BorrowRecords SET
                               BookId = @BookId,
                               ReaderId = @ReaderId,
                               BorrowDate = @BorrowDate,
                               DueDate = @DueDate,
                               ReturnDate = @ReturnDate,
                               Status = @Status
                           WHERE BorrowId = @BorrowId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BorrowId", record.BorrowId);
                cmd.Parameters.AddWithValue("@BookId", record.BookId);
                cmd.Parameters.AddWithValue("@ReaderId", record.ReaderId);
                cmd.Parameters.AddWithValue("@BorrowDate", record.BorrowDate);
                cmd.Parameters.AddWithValue("@DueDate", record.DueDate);
                cmd.Parameters.AddWithValue("@ReturnDate", (object)record.ReturnDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", record.Status);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public List<BorrowRecord> GetBorrowingRecords()
        {
            var records = new List<BorrowRecord>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"SELECT BorrowId, BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status
                               FROM BorrowRecords
                               WHERE Status = @Status";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", "Borrowing");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            records.Add(MapToBorrowRecord(reader));
                    }
                }
            }
            return records;
        }

        public List<BorrowRecord> GetHistory(
            int? readerId = null,
            int? bookId = null,
            string status = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var records = new List<BorrowRecord>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"SELECT BorrowId, BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status
                               FROM BorrowRecords
                               WHERE 1 = 1";

                if (readerId.HasValue) sql += " AND ReaderId = @ReaderId";
                if (bookId.HasValue) sql += " AND BookId = @BookId";
                if (!string.IsNullOrEmpty(status)) sql += " AND Status = @Status";
                if (fromDate.HasValue) sql += " AND BorrowDate >= @FromDate";
                if (toDate.HasValue) sql += " AND BorrowDate <= @ToDate";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    if (readerId.HasValue) cmd.Parameters.AddWithValue("@ReaderId", readerId.Value);
                    if (bookId.HasValue) cmd.Parameters.AddWithValue("@BookId", bookId.Value);
                    if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@Status", status);
                    if (fromDate.HasValue) cmd.Parameters.AddWithValue("@FromDate", fromDate.Value);
                    if (toDate.HasValue) cmd.Parameters.AddWithValue("@ToDate", toDate.Value);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            records.Add(MapToBorrowRecord(reader));
                    }
                }
            }
            return records;
        }

        private BorrowRecord MapToBorrowRecord(SqlDataReader reader)
        {
            int returnDateOrdinal = reader.GetOrdinal("ReturnDate");
            return new BorrowRecord
            {
                BorrowId = reader.GetInt32(reader.GetOrdinal("BorrowId")),
                BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
                ReaderId = reader.GetInt32(reader.GetOrdinal("ReaderId")),
                BorrowDate = reader.GetDateTime(reader.GetOrdinal("BorrowDate")),
                DueDate = reader.GetDateTime(reader.GetOrdinal("DueDate")),
                ReturnDate = reader.IsDBNull(returnDateOrdinal) ? (DateTime?)null : reader.GetDateTime(returnDateOrdinal),
                Status = reader.GetString(reader.GetOrdinal("Status"))
            };
        }
        // Thêm vào BorrowRepository.cs

        // Đếm số borrow "Borrowing" của reader — dùng trong transaction lúc BorrowBook().
        public int CountActiveBorrowsByReader(SqlConnection conn, SqlTransaction tran, int readerId)
        {
            string sql = "SELECT COUNT(*) FROM BorrowRecords WHERE ReaderId = @ReaderId AND Status = @Status";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@ReaderId", readerId);
                cmd.Parameters.AddWithValue("@Status", "Borrowing");
                return (int)cmd.ExecuteScalar();
            }
        }

        // Update atomic: chỉ đổi Status khi đang là "Borrowing" -> chống double-return.
        // Trả về false nếu record không tồn tại hoặc đã Returned từ trước.
        public bool MarkAsReturned(SqlConnection conn, SqlTransaction tran, int borrowId, DateTime returnDate)
        {
            string sql = @"UPDATE BorrowRecords SET Status = @StatusReturned, ReturnDate = @ReturnDate
                   WHERE BorrowId = @BorrowId AND Status = @StatusBorrowing";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BorrowId", borrowId);
                cmd.Parameters.AddWithValue("@ReturnDate", returnDate);
                cmd.Parameters.AddWithValue("@StatusReturned", "Returned");
                cmd.Parameters.AddWithValue("@StatusBorrowing", "Borrowing");
                return cmd.ExecuteNonQuery() > 0;
            }
        }

    }

}
