using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace LibraryManagement.Repositories
{
    public class ReaderRepository
    {
        private readonly Database _db;

        public ReaderRepository()
        {
            _db = new Database();
        }

        public List<Reader> GetAll()
        {
            var readers = new List<Reader>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = "SELECT ReaderId, FullName, Phone, Email FROM Readers";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        readers.Add(MapToReader(reader));
                }
            }
            return readers;
        }

        public Reader GetById(int id)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = "SELECT ReaderId, FullName, Phone, Email FROM Readers WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            return MapToReader(reader);
                    }
                }
            }
            return null;
        }

        // Trả về ReaderId vừa insert.
        public int Add(Reader r)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"INSERT INTO Readers (FullName, Phone, Email)
                               OUTPUT INSERTED.ReaderId
                               VALUES (@FullName, @Phone, @Email)";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", r.FullName);
                    cmd.Parameters.AddWithValue("@Phone", (object)r.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object)r.Email ?? DBNull.Value);
                    return (int)cmd.ExecuteScalar();
                }
            }
        }

        // Trả về false nếu ReaderId không tồn tại.
        public bool Update(Reader r)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"UPDATE Readers SET
                                   FullName = @FullName,
                                   Phone = @Phone,
                                   Email = @Email
                               WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", r.ReaderId);
                    cmd.Parameters.AddWithValue("@FullName", r.FullName);
                    cmd.Parameters.AddWithValue("@Phone", (object)r.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object)r.Email ?? DBNull.Value);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        // Chỉ xóa thẳng. Chặn xóa khi còn sách chưa trả -> ReaderService.DeleteReader() (Bước 4).
        // Trả về false nếu ReaderId không tồn tại.
        public bool Delete(int id)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = "DELETE FROM Readers WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", id);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public List<Reader> Search(string keyword)
        {
            var readers = new List<Reader>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"SELECT ReaderId, FullName, Phone, Email
                               FROM Readers
                               WHERE FullName LIKE @Keyword OR Phone LIKE @Keyword OR Email LIKE @Keyword";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            readers.Add(MapToReader(reader));
                    }
                }
            }
            return readers;
        }

        private Reader MapToReader(SqlDataReader reader)
        {
            int phoneOrdinal = reader.GetOrdinal("Phone");
            int emailOrdinal = reader.GetOrdinal("Email");
            return new Reader
            {
                ReaderId = reader.GetInt32(reader.GetOrdinal("ReaderId")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Phone = reader.IsDBNull(phoneOrdinal) ? null : reader.GetString(phoneOrdinal),
                Email = reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal)
            };
        }
    }
}