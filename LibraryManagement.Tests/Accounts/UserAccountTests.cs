using System;
using System.Collections.Generic;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class UserAccountTests : IDisposable
    {
        public UserAccountTests()
        {
            // Default: logged in as Administrator
            AuthService.CurrentUser = new User
            {
                Id = 1,
                Username = "admin",
                FullName = "System Admin",
                Role = "Administrator"
            };
        }

        public void Dispose()
        {
            AuthService.CurrentUser = null;
        }

        [Fact]
        public void EnsureAdmin_NonAdminUser_ThrowsBusinessRuleException()
        {
            // Arrange (TC-ACCOUNT-01)
            AuthService.CurrentUser = new User { Id = 2, Role = "Librarian" };

            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => userService.GetAllUsers());
            Assert.Equal("Bạn không có quyền thực hiện thao tác này.", ex.Message);
        }

        [Fact]
        public void CreateAccount_AdminCreatesLibrarian_Succeeds()
        {
            // Arrange (TC-ACCOUNT-02)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.ExistsByEmail("lib@library.com", 0)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("lib", 0)).Returns(false);
            mockUserRepo.Setup(r => r.Add(It.IsAny<User>(), "lib123"))
                .Returns((true, "Account created successfully.", new User { Id = 5, Role = "Librarian" }));

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount("Librarian User", "lib", "lib@library.com", "lib123", "Librarian");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal("Librarian", result.User.Role);
        }

        [Fact]
        public void CreateAccount_AdminCreatesAdministrator_Succeeds()
        {
            // Arrange (TC-ACCOUNT-03)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.ExistsByEmail("admin2@library.com", 0)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("admin2", 0)).Returns(false);
            mockUserRepo.Setup(r => r.Add(It.IsAny<User>(), "admin123"))
                .Returns((true, "Account created successfully.", new User { Id = 6, Role = "Administrator" }));

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount("Second Admin", "admin2", "admin2@library.com", "admin123", "Administrator");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal("Administrator", result.User.Role);
        }

        [Fact]
        public void CreateAccount_DuplicateUsername_ReturnsFailure()
        {
            // Arrange (TC-ACCOUNT-04)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.ExistsByEmail("new@mail.com", 0)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("admin", 0)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount("Duplicate User", "admin", "new@mail.com", "pass123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Username này đã được sử dụng!", result.Message);
        }

        [Fact]
        public void CreateAccount_DuplicateEmail_ReturnsFailure()
        {
            // Arrange (TC-ACCOUNT-05)
            var mockUserRepo = new Mock<UserRepository>();
            mockUserRepo.Setup(r => r.ExistsByEmail("admin@library.com", 0)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount("Duplicate User", "newuser", "admin@library.com", "pass123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("email này đã được sử dụng!", result.Message);
        }

        [Fact]
        public void CreateAccount_Password5Chars_ReturnsFailure()
        {
            // Arrange (TC-ACCOUNT-06: 5 chars)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount("User", "user5", "user5@mail.com", "12345");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Password must be at least 6 characters.", result.Message);
        }

        [Theory]
        [InlineData("", "user", "user@mail.com", "Please enter the full name.")]
        [InlineData("Name", "", "user@mail.com", "Please enter a username.")]
        [InlineData("Name", "user", "", "Please enter a valid email address.")]
        [InlineData("Name", "user", "invalidemail", "Please enter a valid email address.")]
        public void CreateAccount_EmptyOrInvalidFields_ReturnsValidationFailure(string name, string username, string email, string expectedMsg)
        {
            // Arrange (TC-ACCOUNT-07)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.CreateAccount(name, username, email, "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal(expectedMsg, result.Message);
        }

        [Fact]
        public void UpdateAccount_ValidChanges_UpdatesSuccessfully()
        {
            // Arrange (TC-ACCOUNT-08)
            var mockUserRepo = new Mock<UserRepository>();
            var existing = new User { Id = 3, Username = "user3", Email = "user3@mail.com", Role = "Librarian", FullName = "Old Name" };

            mockUserRepo.Setup(r => r.GetById(3)).Returns(existing);
            mockUserRepo.Setup(r => r.ExistsByEmail("user3_new@mail.com", 3)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("user3_new", 3)).Returns(false);
            mockUserRepo.Setup(r => r.Update(existing, null)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            var result = userService.UpdateAccount(3, "New Name", "user3_new", "user3_new@mail.com", "Librarian");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Account updated successfully.", result.Message);
            Assert.Equal("New Name", existing.FullName);
        }

        [Fact]
        public void UpdateAccount_DemoteLastAdministrator_ReturnsFailure()
        {
            // Arrange (TC-ACCOUNT-11)
            var mockUserRepo = new Mock<UserRepository>();
            var singleAdmin = new User { Id = 1, Username = "admin", Email = "admin@mail.com", Role = "Administrator" };

            mockUserRepo.Setup(r => r.GetById(1)).Returns(singleAdmin);
            mockUserRepo.Setup(r => r.ExistsByEmail("admin@mail.com", 1)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("admin", 1)).Returns(false);
            // Only 1 admin in system
            mockUserRepo.Setup(r => r.GetAll()).Returns(new List<User> { singleAdmin });

            var userService = new UserService(mockUserRepo.Object);

            // Act: Demoting the only admin to Librarian
            var result = userService.UpdateAccount(1, "Admin", "admin", "admin@mail.com", "Librarian");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Không thể hạ quyền Administrator cuối cùng trong hệ thống.", result.Message);
        }

        [Fact]
        public void UpdateAccount_DemoteAdminWhenMultipleAdminsExist_Succeeds()
        {
            // Arrange (TC-ACCOUNT-12)
            var mockUserRepo = new Mock<UserRepository>();
            var admin1 = new User { Id = 1, Username = "admin1", Email = "admin1@mail.com", Role = "Administrator" };
            var admin2 = new User { Id = 2, Username = "admin2", Email = "admin2@mail.com", Role = "Administrator" };

            mockUserRepo.Setup(r => r.GetById(2)).Returns(admin2);
            mockUserRepo.Setup(r => r.ExistsByEmail("admin2@mail.com", 2)).Returns(false);
            mockUserRepo.Setup(r => r.ExistsByUsername("admin2", 2)).Returns(false);
            mockUserRepo.Setup(r => r.GetAll()).Returns(new List<User> { admin1, admin2 });
            mockUserRepo.Setup(r => r.Update(admin2, null)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act: Demoting admin2 when admin1 also exists
            var result = userService.UpdateAccount(2, "Admin Two", "admin2", "admin2@mail.com", "Librarian");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Account updated successfully.", result.Message);
        }

        [Fact]
        public void DeleteUser_SelfDeletion_ThrowsBusinessRuleException()
        {
            // Arrange (TC-ACCOUNT-13)
            var mockUserRepo = new Mock<UserRepository>();
            var userService = new UserService(mockUserRepo.Object);

            // Act & Assert (AuthService.CurrentUser.Id is 1)
            var ex = Assert.Throws<BusinessRuleException>(() => userService.DeleteUser(1));
            Assert.Equal("Không thể tự xóa tài khoản đang đăng nhập.", ex.Message);
        }

        [Fact]
        public void DeleteUser_LastAdministrator_ThrowsBusinessRuleException()
        {
            // Arrange (TC-ACCOUNT-14)
            // Logged in as admin 2, trying to delete admin 1 who is the ONLY Administrator in DB
            AuthService.CurrentUser = new User { Id = 2, Role = "Administrator" };

            var mockUserRepo = new Mock<UserRepository>();
            var targetAdmin = new User { Id = 1, Role = "Administrator" };
            mockUserRepo.Setup(r => r.GetById(1)).Returns(targetAdmin);
            mockUserRepo.Setup(r => r.GetAll()).Returns(new List<User> { targetAdmin }); // count = 1

            var userService = new UserService(mockUserRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => userService.DeleteUser(1));
            Assert.Equal("Không thể xóa Administrator cuối cùng trong hệ thống.", ex.Message);
        }

        [Fact]
        public void DeleteUser_LibrarianUser_Succeeds()
        {
            // Arrange (TC-ACCOUNT-15)
            var mockUserRepo = new Mock<UserRepository>();
            var librarian = new User { Id = 3, Role = "Librarian" };
            mockUserRepo.Setup(r => r.GetById(3)).Returns(librarian);
            mockUserRepo.Setup(r => r.Delete(3)).Returns(true);

            var userService = new UserService(mockUserRepo.Object);

            // Act
            bool deleted = userService.DeleteUser(3);

            // Assert
            Assert.True(deleted);
            mockUserRepo.Verify(r => r.Delete(3), Times.Once);
        }

        [Fact]
        public void SearchUsers_FiltersAcrossMultipleFieldsCaseInsensitively()
        {
            // Arrange (TC-ACCOUNT-16)
            var mockUserRepo = new Mock<UserRepository>();
            var users = new List<User>
            {
                new User { Id = 1, FullName = "Alice Admin", Username = "alice", Email = "alice@library.com", Role = "Administrator" },
                new User { Id = 2, FullName = "Bob Librarian", Username = "bob", Email = "bob@library.com", Role = "Librarian" }
            };
            mockUserRepo.Setup(r => r.GetAll()).Returns(users);

            var userService = new UserService(mockUserRepo.Object);

            // Act & Assert
            var byName = userService.SearchUsers("alice");
            Assert.Single(byName);

            var byRole = userService.SearchUsers("librarian");
            Assert.Single(byRole);

            var emptyQuery = userService.SearchUsers("");
            Assert.Equal(2, emptyQuery.Count);
        }
    }
}
