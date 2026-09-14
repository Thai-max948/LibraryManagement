using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;
using BCrypt.Net;

namespace LibraryManagement.Repositories
{
    public class UserRepository
    {
        private readonly Database _db;

        public UserRepository()
        {
            _db = new Database();
            EnsureTableExists();
        }

        private void EnsureTableExists()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = @"
        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
        BEGIN
            CREATE TABLE Users (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Username NVARCHAR(100) NOT NULL,
                Email NVARCHAR(150) NOT NULL,
                FullName NVARCHAR(150) NOT NULL,
                PasswordHash NVARCHAR(255) NOT NULL,
                Role NVARCHAR(50) NOT NULL DEFAULT 'Librarian',
                CreatedAt DATETIME DEFAULT GETDATE(),
                CONSTRAINT UQ_Users_Email UNIQUE (Email),
                CONSTRAINT UQ_Users_Username UNIQUE (Username)
            );
        END";
            using var cmd = new SqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public List<User> GetAll()
        {
            var list = new List<User>();
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "SELECT Id, Username, Email, FullName, Role, CreatedAt FROM Users ORDER BY Id DESC";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapToUser(reader));
            return list;
        }

        public User? GetById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "SELECT Id, Username, Email, FullName, Role, CreatedAt FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapToUser(reader) : null;
        }

        // Password được hash bằng BCrypt trước khi lưu — KHÔNG bao giờ lưu plain text.
        public (bool Success, string Message, User? User) Add(User user, string password)
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            using var conn = _db.GetConnection();
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
                    user.Id = id;

                return (true, "Account created successfully.", user);
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                // Dùng tên constraint để biết chính xác trường nào bị trùng.
                if (ex.Message.Contains("UQ_Users_Email"))
                    return (false, "email này đã được sử dụng!", null);
                if (ex.Message.Contains("UQ_Users_Username"))
                    return (false, "Username này đã được sử dụng!", null);
                return (false, "Tài khoản đã tồn tại.", null);
            }
        }

        public bool Update(User user, string? newPassword = null)
        {
            using var conn = _db.GetConnection();
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
                cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(newPassword));

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool Delete(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "DELETE FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            return cmd.ExecuteNonQuery() > 0;
        }

        public (bool Success, string Message, User? User) Authenticate(string usernameOrEmail, string password)
        {
            string trimmed = usernameOrEmail.Trim();

            using var conn = _db.GetConnection();
            conn.Open();
            string sql = @"SELECT TOP 1 Id, Username, Email, FullName, Role, PasswordHash, CreatedAt
                           FROM Users WHERE Email = @Input OR Username = @Input";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Input", trimmed);
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return (false, "Invalid username/email or password.", null);

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

            bool passwordMatches;
            bool isLegacyPlainText = !IsBCryptHash(storedHash);

            if (isLegacyPlainText)
            {
                // Migration path: tài khoản tạo trước khi có BCrypt, password đang lưu plain text.
                passwordMatches = storedHash == password;
                if (passwordMatches)
                {
                    // Tự động nâng cấp sang hash ngay khi đăng nhập thành công lần đầu sau migration.
                    UpgradeToHashedPassword(userId, password, conn);
                }
            }
            else
            {
                passwordMatches = BCrypt.Net.BCrypt.Verify(password, storedHash);
            }

            return passwordMatches
                ? (true, "Login successful.", user)
                : (false, "Invalid username/email or password.", null);
        }

        public bool VerifyPassword(int userId, string password)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "SELECT TOP 1 PasswordHash FROM Users WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", userId);
            var stored = cmd.ExecuteScalar()?.ToString();
            if (stored == null) return false;

            if (IsBCryptHash(stored))
                return BCrypt.Net.BCrypt.Verify(password, stored);

            bool matches = stored == password;
            if (matches)
                UpgradeToHashedPassword(userId, password, conn);
            return matches;
        }

        public bool ChangePassword(int userId, string newPassword)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", userId);
            cmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            return cmd.ExecuteNonQuery() > 0;
        }

        // BCrypt hash luôn bắt đầu bằng $2a$/$2b$/$2y$ — dùng để phân biệt hash cũ (plain text) và mới.
        private bool IsBCryptHash(string value)
            => value.StartsWith("$2a$") || value.StartsWith("$2b$") || value.StartsWith("$2y$");

        private void UpgradeToHashedPassword(int userId, string plainPassword, SqlConnection conn)
        {
            string newHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
            string sql = "UPDATE Users SET PasswordHash = @Hash WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", userId);
            cmd.Parameters.AddWithValue("@Hash", newHash);
            cmd.ExecuteNonQuery();
        }

        private User MapToUser(SqlDataReader reader) => new User
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