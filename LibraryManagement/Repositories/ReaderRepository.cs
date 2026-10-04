using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class ReaderRepository
    {
        // Borrow transactions acquire this lock before touching a copy or loan.
        public virtual bool LockForBorrow(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(
                "SELECT ReaderId FROM dbo.Readers WITH (UPDLOCK, HOLDLOCK) WHERE ReaderId = @ReaderId",
                connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            return command.ExecuteScalar() != null;
        }

        public virtual bool ExistsForCirculation(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(
                "SELECT ReaderId FROM dbo.Readers WITH (HOLDLOCK, ROWLOCK) WHERE ReaderId = @ReaderId",
                connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            return command.ExecuteScalar() != null;
        }

        private static readonly object SchemaLock = new object();
        private static bool _schemaChecked;

        public virtual List<Reader> GetAll(bool includeDeleted = false)
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = includeDeleted
                    ? "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers"
                    : "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE IsDeleted = 0";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        readers.Add(MapToReader(reader));
                    }
                }
            }
            return readers;
        }

        public virtual Reader? GetById(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapToReader(reader);
                        }
                    }
                }
            }
            return null;
        }

        public virtual Reader? GetById(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(@"
                SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email,
                    Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason,
                    SuspendedDate, IsDeleted
                FROM dbo.Readers WHERE ReaderId = @ReaderId", connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapToReader(reader) : null;
        }

        public virtual int Add(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = @"INSERT INTO Readers (FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted)
                               OUTPUT INSERTED.ReaderId
                               VALUES (@FullName, @ReaderType, @StudentId, @IdentityNumber, @Phone, @Email, @Address, @RegistrationDate, @MembershipExpiresOn, @Status, @SuspensionReason, @SuspendedDate, 0)";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@ReaderType", string.IsNullOrWhiteSpace(reader.ReaderType) ? "Student" : reader.ReaderType);
                    cmd.Parameters.AddWithValue("@StudentId", (object?)reader.StudentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdentityNumber", (object?)reader.IdentityNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object?)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object?)reader.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object?)reader.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RegistrationDate", reader.RegistrationDate == default ? DateTime.Now : reader.RegistrationDate);
                    cmd.Parameters.AddWithValue("@MembershipExpiresOn", (object?)reader.MembershipExpiresOn?.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
                    cmd.Parameters.AddWithValue("@SuspensionReason", (object?)reader.SuspensionReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SuspendedDate", (object?)reader.SuspendedDate ?? DBNull.Value);
                    int newId = (int)cmd.ExecuteScalar();
                    reader.ReaderId = newId;
                    return newId;
                }
            }
        }

        public virtual bool Update(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = @"UPDATE Readers SET
                                   FullName = @FullName,
                                   ReaderType = @ReaderType,
                                   StudentId = @StudentId,
                                   IdentityNumber = @IdentityNumber,
                                   Phone = @Phone,
                                   Email = @Email,
                                   Address = @Address,
                                   MembershipExpiresOn = @MembershipExpiresOn,
                                   Status = @Status,
                                   SuspensionReason = @SuspensionReason,
                                   SuspendedDate = @SuspendedDate
                               WHERE ReaderId = @ReaderId AND IsDeleted = 0";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", reader.ReaderId);
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@ReaderType", string.IsNullOrWhiteSpace(reader.ReaderType) ? "Student" : reader.ReaderType);
                    cmd.Parameters.AddWithValue("@StudentId", (object?)reader.StudentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdentityNumber", (object?)reader.IdentityNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object?)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object?)reader.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object?)reader.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@MembershipExpiresOn", (object?)reader.MembershipExpiresOn?.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
                    cmd.Parameters.AddWithValue("@SuspensionReason", (object?)reader.SuspensionReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SuspendedDate", (object?)reader.SuspendedDate ?? DBNull.Value);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public virtual bool Delete(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string checkSql = "SELECT IsDeleted FROM Readers WHERE ReaderId = @ReaderId";
                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@ReaderId", id);
                    var isDelObj = checkCmd.ExecuteScalar();
                    if (isDelObj == null)
                    {
                        return false;
                    }
                    if (Convert.ToBoolean(isDelObj))
                    {
                        return false;
                    }
                }

                string checkHistorySql = "SELECT COUNT(*) FROM BorrowRecords WHERE ReaderId = @ReaderId";
                using (var historyCmd = new SqlCommand(checkHistorySql, conn))
                {
                    historyCmd.Parameters.AddWithValue("@ReaderId", id);
                    int borrowCount = (int)historyCmd.ExecuteScalar();

                    if (borrowCount > 0)
                    {
                        // Đã có lịch sử mượn trả: Xóa mềm để bảo toàn lịch sử và ràng buộc khóa ngoại
                        string softDeleteSql = "UPDATE Readers SET IsDeleted = 1 WHERE ReaderId = @ReaderId";
                        using (var cmd = new SqlCommand(softDeleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ReaderId", id);
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }
                    else
                    {
                        // Chưa từng mượn sách: Xóa cứng khỏi CSDL
                        string hardDeleteSql = "DELETE FROM Readers WHERE ReaderId = @ReaderId";
                        using (var cmd = new SqlCommand(hardDeleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ReaderId", id);
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }
                }
            }
        }

        public virtual List<Reader> Search(string keyword)
        {
            return Search(keyword, null, null);
        }

        public virtual List<Reader> Search(string keyword, string? readerType, string? status)
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                var conditions = new List<string> { "IsDeleted = 0" };

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    conditions.Add(@"(
                        FullName LIKE @Keyword 
                        OR Phone LIKE @Keyword 
                        OR Email LIKE @Keyword 
                        OR StudentId LIKE @Keyword 
                        OR IdentityNumber LIKE @Keyword 
                        OR CAST(ReaderId AS NVARCHAR(20)) LIKE @Keyword
                        OR ('R' + RIGHT('000000' + CAST(ReaderId AS NVARCHAR(20)), 6)) LIKE @Keyword
                    )");
                }

                if (!string.IsNullOrWhiteSpace(readerType) && !string.Equals(readerType, "All", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("ReaderType = @ReaderType");
                }

                if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("Status = @Status");
                }

                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE "
                    + string.Join(" AND ", conditions);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        cmd.Parameters.AddWithValue("@Keyword", $"%{keyword.Trim()}%");
                    }
                    if (!string.IsNullOrWhiteSpace(readerType) && !string.Equals(readerType, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.Parameters.AddWithValue("@ReaderType", readerType.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.Parameters.AddWithValue("@Status", status.Trim());
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            readers.Add(MapToReader(reader));
                        }
                    }
                }
            }
            return readers;
        }

        public virtual bool IdentificationExists(string readerType, string identification, int excludeReaderId = 0)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            EnsureEnhancementColumns(conn);
            string column = string.Equals(readerType, "External", StringComparison.OrdinalIgnoreCase)
                ? "IdentityNumber" : "StudentId";
            string sql = $@"SELECT 1 FROM Readers
                            WHERE IsDeleted = 0 AND UPPER(LTRIM(RTRIM({column}))) = UPPER(@Identification)
                              AND (@ExcludeReaderId = 0 OR ReaderId <> @ExcludeReaderId)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Identification", identification.Trim());
            cmd.Parameters.AddWithValue("@ExcludeReaderId", excludeReaderId);
            return cmd.ExecuteScalar() != null;
        }

        private static Reader MapToReader(SqlDataReader reader)
        {
            int readerIdOrdinal = reader.GetOrdinal("ReaderId");
            int fullNameOrdinal = reader.GetOrdinal("FullName");
            int phoneOrdinal = GetOrdinalOrDefault(reader, "Phone");
            int emailOrdinal = GetOrdinalOrDefault(reader, "Email");
            int typeOrdinal = GetOrdinalOrDefault(reader, "ReaderType");
            int studentIdOrdinal = GetOrdinalOrDefault(reader, "StudentId");
            int idNumOrdinal = GetOrdinalOrDefault(reader, "IdentityNumber");
            int addressOrdinal = GetOrdinalOrDefault(reader, "Address");
            int regDateOrdinal = GetOrdinalOrDefault(reader, "RegistrationDate");
            int membershipOrdinal = GetOrdinalOrDefault(reader, "MembershipExpiresOn");
            int statusOrdinal = GetOrdinalOrDefault(reader, "Status");
            int suspensionReasonOrdinal = GetOrdinalOrDefault(reader, "SuspensionReason");
            int suspendedDateOrdinal = GetOrdinalOrDefault(reader, "SuspendedDate");
            int isDeletedOrdinal = GetOrdinalOrDefault(reader, "IsDeleted");

            return new Reader
            {
                ReaderId = reader.GetInt32(readerIdOrdinal),
                FullName = reader.GetString(fullNameOrdinal),
                ReaderType = typeOrdinal >= 0 && !reader.IsDBNull(typeOrdinal) ? reader.GetString(typeOrdinal) : "Student",
                StudentId = studentIdOrdinal >= 0 && !reader.IsDBNull(studentIdOrdinal) ? reader.GetString(studentIdOrdinal) : string.Empty,
                IdentityNumber = idNumOrdinal >= 0 && !reader.IsDBNull(idNumOrdinal) ? reader.GetString(idNumOrdinal) : string.Empty,
                Phone = phoneOrdinal >= 0 && !reader.IsDBNull(phoneOrdinal) ? reader.GetString(phoneOrdinal) : string.Empty,
                Email = emailOrdinal >= 0 && !reader.IsDBNull(emailOrdinal) ? reader.GetString(emailOrdinal) : string.Empty,
                Address = addressOrdinal >= 0 && !reader.IsDBNull(addressOrdinal) ? reader.GetString(addressOrdinal) : string.Empty,
                RegistrationDate = regDateOrdinal >= 0 && !reader.IsDBNull(regDateOrdinal) ? reader.GetDateTime(regDateOrdinal) : DateTime.Now,
                MembershipExpiresOn = membershipOrdinal >= 0 && !reader.IsDBNull(membershipOrdinal) ? reader.GetDateTime(membershipOrdinal) : null,
                Status = statusOrdinal >= 0 && !reader.IsDBNull(statusOrdinal) ? reader.GetString(statusOrdinal) : "Active",
                SuspensionReason = suspensionReasonOrdinal >= 0 && !reader.IsDBNull(suspensionReasonOrdinal) ? reader.GetString(suspensionReasonOrdinal) : string.Empty,
                SuspendedDate = suspendedDateOrdinal >= 0 && !reader.IsDBNull(suspendedDateOrdinal) ? reader.GetDateTime(suspendedDateOrdinal) : null,
                IsDeleted = isDeletedOrdinal >= 0 && !reader.IsDBNull(isDeletedOrdinal) && reader.GetBoolean(isDeletedOrdinal)
            };
        }

        private static void EnsureEnhancementColumns(SqlConnection connection)
        {
            if (_schemaChecked)
            {
                return;
            }

            lock (SchemaLock)
            {
                if (_schemaChecked)
                {
                    return;
                }

                const string sql = @"
                    IF COL_LENGTH('dbo.Readers', 'SuspensionReason') IS NULL
                        ALTER TABLE dbo.Readers ADD SuspensionReason NVARCHAR(500) NULL;

                    IF COL_LENGTH('dbo.Readers', 'SuspendedDate') IS NULL
                        ALTER TABLE dbo.Readers ADD SuspendedDate DATETIME NULL;
                    IF COL_LENGTH('dbo.Readers', 'MembershipExpiresOn') IS NULL
                        ALTER TABLE dbo.Readers ADD MembershipExpiresOn DATE NULL;";

                using var command = new SqlCommand(sql, connection);
                command.ExecuteNonQuery();
                _schemaChecked = true;
            }
        }

        private static int GetOrdinalOrDefault(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName);
            }
            catch (IndexOutOfRangeException)
            {
                return -1;
            }
        }
    }
}
