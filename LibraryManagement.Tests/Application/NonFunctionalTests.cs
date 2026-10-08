using System;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Xunit;

namespace LibraryManagement.Tests
{
    public class NonFunctionalTests
    {
        [Fact]
        public void DatabaseConnectionString_HasValidDefaultFallback()
        {
            // Arrange & Act (TC-NFR-03)
            string connStr = Database.ConnectionString;

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(connStr));
            Assert.Contains("Database=LibraryDB", connStr, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Models_VietnameseUnicodeHandling_PreservesSpecialCharacters()
        {
            // Arrange (TC-NFR-05)
            const string vnTitle = "Nghệ thuật lập trình: Hướng dẫn từ cơ bản đến nâng cao";
            const string vnAuthor = "Nguyễn Đặng Hoàng Trí";
            const string vnReader = "Trần Thị Ánh Tuyết";

            // Act
            var book = new Book { Title = vnTitle, Author = vnAuthor, Category = "Khoa học" };
            var reader = new Reader { FullName = vnReader, Phone = "0912345678", Email = "tuyet@domain.vn" };

            // Assert
            Assert.Equal(vnTitle, book.Title);
            Assert.Equal(vnAuthor, book.Author);
            Assert.Equal(vnReader, reader.FullName);
        }

        [Fact]
        public void PasswordSecurity_BCryptHash_IsNotPlainTextAndVerifiesSuccessfully()
        {
            // Arrange (TC-NFR-06)
            const string plainPassword = "SuperSecretPassword123!";

            // Act
            string hash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

            // Assert
            // 1. Must NOT be equal to plain text
            Assert.NotEqual(plainPassword, hash);
            // 2. Must start with standard BCrypt salt identifier ($2a$ or $2b$)
            Assert.StartsWith("$2", hash);
            // 3. Verifies successfully with original password
            Assert.True(BCrypt.Net.BCrypt.Verify(plainPassword, hash));
            // 4. Rejects wrong password
            Assert.False(BCrypt.Net.BCrypt.Verify("WrongPassword", hash));
        }
    }
}
