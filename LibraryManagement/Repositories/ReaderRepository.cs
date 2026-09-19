using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class ReaderRepository
    {
        public List<Reader> GetAll()
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = "SELECT ReaderId, FullName, Phone, Email FROM Readers";
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

        public Reader? GetById(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = "SELECT ReaderId, FullName, Phone, Email FROM Readers WHERE ReaderId = @ReaderId";
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

        public int Add(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = @"INSERT INTO Readers (FullName, Phone, Email)
                               OUTPUT INSERTED.ReaderId
                               VALUES (@FullName, @Phone, @Email)";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@Phone", (object)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object)reader.Email ?? DBNull.Value);
                    return (int)cmd.ExecuteScalar();
                }
            }
        }

        public bool Update(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                string sql = @"UPDATE Readers SET
                                   FullName = @FullName,
                                   Phone = @Phone,
                                   Email = @Email
                               WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", reader.ReaderId);
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@Phone", (object)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object)reader.Email ?? DBNull.Value);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool Delete(int id)
        {
            using (var conn = Database.GetConnection())
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
            using (var conn = Database.GetConnection())
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
            int phoneOrdinal = reader.GetOrdinal("Phone");
            int emailOrdinal = reader.GetOrdinal("Email");
            return new Reader
            {
                ReaderId = reader.GetInt32(reader.GetOrdinal("ReaderId")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Phone = reader.IsDBNull(phoneOrdinal) ? string.Empty : reader.GetString(phoneOrdinal),
                Email = reader.IsDBNull(emailOrdinal) ? string.Empty : reader.GetString(emailOrdinal)
            };
        }
    }
}