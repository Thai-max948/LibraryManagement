using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class UserRepository
    {
        public UserRepository()
        {
        }

        public List<User> GetAll()
        {
            var list = new List<User>();
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "SELECT Id, Username, Email, FullName, Role, CreatedAt FROM Users ORDER BY Id DESC";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapToUser(reader));
            }
            return list;
        }

        public User? GetById(int id)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "SELECT Id, Username, Email, FullName, Role, CreatedAt FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapToUser(reader) : null;
        }

        public bool ExistsByEmail(string email, int excludeUserId = 0)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "SELECT 1 FROM Users WHERE LOWER(Email) = LOWER(@Email)" + (excludeUserId > 0 ? " AND Id <> @ExcludeId" : "");
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Email", email.Trim());
            if (excludeUserId > 0)
            {
                cmd.Parameters.AddWithValue("@ExcludeId", excludeUserId);
            }
            return cmd.ExecuteScalar() != null;
        }

        public bool ExistsByUsername(string username, int excludeUserId = 0)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "SELECT 1 FROM Users WHERE LOWER(Username) = LOWER(@Username)" + (excludeUserId > 0 ? " AND Id <> @ExcludeId" : "");
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", username.Trim());
            if (excludeUserId > 0)
            {
                cmd.Parameters.AddWithValue("@ExcludeId", excludeUserId);
            }
            return cmd.ExecuteScalar() != null;
        }

        public (bool Success, string Message, User? User) Add(User user, string password)
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            using var conn = Database.GetConnection();
            conn.Open();
            string sql = @"
                INSERT INTO Users (Username, Email, FullName, PasswordHash, Role, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@Username, @Email, @FullName, @PasswordHash, @Role, @CreatedAt)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", user.Username);
            cmd.Parameters.AddWithValue("@Email", user.Email);
            cmd.Parameters.AddWithValue("@FullName", user.FullName);
            cmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
            cmd.Parameters.AddWithValue("@Role", user.Role);
            cmd.Parameters.AddWithValue("@CreatedAt", user.CreatedAt);

            try
            {
                object? newId = cmd.ExecuteScalar();
                if (newId != null && int.TryParse(newId.ToString(), out int id))
                {
                    user.Id = id;
                }

                return (true, "Account created successfully.", user);
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                if (ex.Message.Contains("UQ_Users_Email", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "email này đã được sử dụng!", null);
                }
                if (ex.Message.Contains("UQ_Users_Username", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Username này đã được sử dụng!", null);
                }
                return (false, "Tài khoản đã tồn tại.", null);
            }
        }

        public bool Update(User user, string? newPassword = null)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = string.IsNullOrWhiteSpace(newPassword)
                ? @"UPDATE Users SET Username = @Username, Email = @Email, FullName = @FullName, Role = @Role WHERE Id = @Id"
                : @"UPDATE Users SET Username = @Username, Email = @Email, FullName = @FullName, Role = @Role, PasswordHash = @PasswordHash WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", user.Id);
            cmd.Parameters.AddWithValue("@Username", user.Username);
            cmd.Parameters.AddWithValue("@Email", user.Email);
            cmd.Parameters.AddWithValue("@FullName", user.FullName);
            cmd.Parameters.AddWithValue("@Role", user.Role);
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            }

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool Delete(int id)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "DELETE FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            return cmd.ExecuteNonQuery() > 0;
        }

        public (bool Success, string Message, User? User) Authenticate(string usernameOrEmail, string password)
        {
            string trimmed = usernameOrEmail.Trim();

            using var conn = Database.GetConnection();
            conn.Open();
            string sql = @"SELECT TOP 1 Id, Username, Email, FullName, Role, PasswordHash, CreatedAt
                           FROM Users WHERE LOWER(Email) = LOWER(@Input) OR LOWER(Username) = LOWER(@Input)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Input", trimmed);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                return (false, "Invalid username/email or password.", null);
            }

            string storedHash = reader["PasswordHash"]?.ToString() ?? "";
            int userId = Convert.ToInt32(reader["Id"]);
            var user = new User
            {
                Id = userId,
                Username = reader["Username"]?.ToString() ?? "",
                Email = reader["Email"]?.ToString() ?? "",
                FullName = reader["FullName"]?.ToString() ?? "",
                Role = reader["Role"]?.ToString() ?? "Librarian",
                CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
            };
            reader.Close();

            bool passwordMatches = false;
            try
            {
                passwordMatches = BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch
            {
                passwordMatches = false;
            }

            return passwordMatches
                ? (true, "Login successful.", user)
                : (false, "Invalid username/email or password.", null);
        }

        public bool VerifyPassword(int userId, string password)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "SELECT TOP 1 PasswordHash FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", userId);
            var stored = cmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, stored);
            }
            catch
            {
                return false;
            }
        }

        public bool ChangePassword(int userId, string newPassword)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", userId);
            cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            return cmd.ExecuteNonQuery() > 0;
        }

        private static User MapToUser(SqlDataReader reader) => new User
        {
            Id = Convert.ToInt32(reader["Id"]),
            Username = reader["Username"]?.ToString() ?? "",
            Email = reader["Email"]?.ToString() ?? "",
            FullName = reader["FullName"]?.ToString() ?? "",
            Role = reader["Role"]?.ToString() ?? "Librarian",
            CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
        };
    }
}