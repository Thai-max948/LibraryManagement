using System;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class MyAccountTests : IDisposable
    {
        public MyAccountTests()
        {
            AuthService.CurrentUser = new User
            {
                Id = 10,
                Username = "myuser",
                FullName = "My User",
                Email = "myuser@library.com",
                Role = "Librarian"
            };
        }

        public void Dispose()
        {
            AuthService.CurrentUser = null;
        }

        [Fact]
        public void UpdateSelfProfile_ValidData_UpdatesSuccessfully()
        {
            // Arrange (TC-MYACC-01)
            var mockUserRepo = new Mock<UserRepository>();
            var user = new User { Id = 10, Username = "myuser", FullName = "Old Name", Email = "myuser@library.com", Role = "Librarian" };

            mockUserRepo.Setup(r => r.GetById(10)).Returns(user);
            mockUserRepo.Setup(r => r.ExistsByEmail("newemail@library.com", 10)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("newusername", 10)).Returns(false);
            mockUserRepo.Setup(r => r.Update(user, null)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.UpdateSelfProfile(10, "New Full Name", "newusername", "newemail@library.com");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Profile updated successfully!", result.Message);
            Assert.Equal("New Full Name", user.FullName);
            Assert.Equal("newusername", user.Username);
            Assert.Equal("newemail@library.com", user.Email);
        }

        [Fact]
        public void UpdateSelfProfile_DuplicateEmailOrUsername_ReturnsFailure()
        {
            // Arrange (TC-MYACC-02)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.ExistsByEmail("existing@library.com", 10)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.UpdateSelfProfile(10, "Name", "uniqueuser", "existing@library.com");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("email này đã được sử dụng!", result.Message);
        }

        [Theory]
        [InlineData("", "user", "u@mail.com", "Please enter your full name.")]
        [InlineData("Name", "", "u@mail.com", "Please enter your username.")]
        [InlineData("Name", "user", "", "Please enter a valid email address.")]
        [InlineData("Name", "user", "invalidemail", "Please enter a valid email address.")]
        public void UpdateSelfProfile_EmptyOrInvalidFields_ReturnsValidationFailure(string name, string username, string email, string expectedMsg)
        {
            // Arrange (TC-MYACC-03)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.UpdateSelfProfile(10, name, username, email);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(expectedMsg, result.Message);
        }

        [Fact]
        public void UpdateSelfProfile_DoesNotModifyUserRole()
        {
            // Arrange (TC-MYACC-04: Librarian cannot elevate role via My Account)
            var mockUserRepo = new Mock<UserRepository>();
            var librarian = new User { Id = 10, Username = "myuser", Role = "Librarian" };
            mockUserRepo.Setup(r => r.GetById(10)).Returns(librarian);
            mockUserRepo.Setup(r => r.Update(librarian, null)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act: UpdateSelfProfile only accepts name, username, email
            userService.UpdateSelfProfile(10, "Updated Name", "myuser", "myuser@library.com");

            // Assert
            Assert.Equal("Librarian", librarian.Role);
        }

        [Fact]
        public void ChangePassword_ValidCredentials_ReturnsSuccess()
        {
            // Arrange (TC-MYACC-05)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.VerifyPassword(10, "oldPass123")).Returns(true);
            mockUserRepo.Setup(r => r.ChangePassword(10, "newPass123")).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.ChangePassword(10, "oldPass123", "newPass123", "newPass123");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Password changed successfully!", result.Message);
        }

        [Fact]
        public void ChangePassword_IncorrectOldPassword_ReturnsFailure()
        {
            // Arrange (TC-MYACC-06)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.VerifyPassword(10, "wrongOldPass")).Returns(false);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.ChangePassword(10, "wrongOldPass", "newPass123", "newPass123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Current password is incorrect.", result.Message);
            mockUserRepo.Verify(r => r.ChangePassword(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void ChangePassword_EmptyOldPassword_ReturnsFailure(string? oldPass)
        {
            // Arrange (TC-MYACC-07)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.ChangePassword(10, oldPass!, "newPass123", "newPass123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Please enter your current password.", result.Message);
        }

        [Theory]
        [InlineData("12345", false, "New password must be at least 6 characters long.")] // 5 chars (TC-MYACC-08 BVA)
        [InlineData("123456", true, "Password changed successfully!")]                    // 6 chars (TC-MYACC-08 BVA)
        public void ChangePassword_NewPasswordLengthBoundaries(string newPass, bool shouldSucceed, string expectedMsg)
        {
            // Arrange (TC-MYACC-08)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.VerifyPassword(10, "oldPass")).Returns(true);
            mockUserRepo.Setup(r => r.ChangePassword(10, newPass)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.ChangePassword(10, "oldPass", newPass, newPass);

            // Assert
            Assert.Equal(shouldSucceed, result.Success);
            Assert.Equal(expectedMsg, result.Message);
        }

        [Fact]
        public void ChangePassword_ConfirmPasswordMismatch_ReturnsFailure()
        {
            // Arrange (TC-MYACC-09)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.ChangePassword(10, "oldPass", "newPass123", "differentPass");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("New passwords do not match.", result.Message);
        }
    }
}
