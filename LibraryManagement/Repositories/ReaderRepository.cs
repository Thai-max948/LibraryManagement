using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class ReaderRepository
    {
        public virtual List<Reader> GetAll(bool includeDeleted = false)
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = includeDeleted
                    ? "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted FROM Readers"
                    : "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted FROM Readers WHERE IsDeleted = 0";
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
                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted FROM Readers WHERE ReaderId = @ReaderId";
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

        public virtual int Add(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = @"INSERT INTO Readers (FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted)
                               OUTPUT INSERTED.ReaderId
                               VALUES (@FullName, @ReaderType, @StudentId, @IdentityNumber, @Phone, @Email, @Address, @RegistrationDate, @Status, 0)";
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
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
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
                string sql = @"UPDATE Readers SET
                                   FullName = @FullName,
                                   ReaderType = @ReaderType,
                                   StudentId = @StudentId,
                                   IdentityNumber = @IdentityNumber,
                                   Phone = @Phone,
                                   Email = @Email,
                                   Address = @Address,
                                   Status = @Status
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
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public virtual bool Delete(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
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

                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted FROM Readers WHERE "
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
            int statusOrdinal = GetOrdinalOrDefault(reader, "Status");
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
                Status = statusOrdinal >= 0 && !reader.IsDBNull(statusOrdinal) ? reader.GetString(statusOrdinal) : "Active",
                IsDeleted = isDeletedOrdinal >= 0 && !reader.IsDBNull(isDeletedOrdinal) && reader.GetBoolean(isDeletedOrdinal)
            };
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