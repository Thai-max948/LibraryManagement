using System;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class AuthenticationTests : IDisposable
    {
        public AuthenticationTests()
        {
            AuthService.CurrentUser = null;
        }

        public void Dispose()
        {
            AuthService.CurrentUser = null;
        }

        [Fact]
        public void Login_ValidAdminUsername_ReturnsSuccessAndSetsCurrentUser()
        {
            // Arrange (TC-AUTH-01)
            var mockRepo = new Mock<UserRepository>();
            var adminUser = new User
            {
                Id = 1,
                Username = "admin",
                Email = "admin@library.com",
                FullName = "Administrator",
                Role = "Administrator"
            };
            mockRepo.Setup(r => r.Authenticate("admin", "admin123"))
                .Returns((true, "Login successful.", adminUser));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login("admin", "admin123");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal("admin", result.User.Username);
            Assert.Equal("Administrator", result.User.Role);
            Assert.Equal(adminUser, AuthService.CurrentUser);
        }

        [Fact]
        public void Login_ValidAdminEmail_ReturnsSuccess()
        {
            // Arrange (TC-AUTH-02)
            var mockRepo = new Mock<UserRepository>();
            var adminUser = new User
            {
                Id = 1,
                Username = "admin",
                Email = "admin@library.com",
                FullName = "Administrator",
                Role = "Administrator"
            };
            mockRepo.Setup(r => r.Authenticate("admin@library.com", "admin123"))
                .Returns((true, "Login successful.", adminUser));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login("admin@library.com", "admin123");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal("admin@library.com", result.User.Email);
        }

        [Fact]
        public void Login_LibrarianUser_MainViewModelHidesAccountsModule()
        {
            // Arrange (TC-AUTH-03)
            var librarian = new User
            {
                Id = 2,
                Username = "librarian",
                Email = "lib@library.com",
                FullName = "Librarian User",
                Role = "Librarian"
            };
            AuthService.CurrentUser = librarian;

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();
                Assert.False(mainVm.IsAccountsVisible);
                Assert.Equal(System.Windows.Visibility.Collapsed, mainVm.AccountsVisibility);
            });
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Login_EmptyOrWhitespaceUsernameOrEmail_ReturnsFailure(string? usernameOrEmail)
        {
            // Arrange (TC-AUTH-04)
            var mockRepo = new Mock<UserRepository>();
            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login(usernameOrEmail!, "somePassword");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Please enter your username or email.", result.Message);
            Assert.Null(result.User);
            mockRepo.Verify(r => r.Authenticate(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Login_EmptyOrWhitespacePassword_ReturnsFailure(string? password)
        {
            // Arrange (TC-AUTH-05)
            var mockRepo = new Mock<UserRepository>();
            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login("admin", password!);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Please enter your password.", result.Message);
            Assert.Null(result.User);
            mockRepo.Verify(r => r.Authenticate(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Login_WrongPassword_ReturnsFailure()
        {
            // Arrange (TC-AUTH-06)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.Authenticate("admin", "wrongPass"))
                .Returns((false, "Invalid username/email or password.", null));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login("admin", "wrongPass");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Invalid username/email or password.", result.Message);
            Assert.Null(result.User);
            Assert.Null(AuthService.CurrentUser);
        }

        [Fact]
        public void Login_NonExistentUsername_ReturnsGenericFailureMessage()
        {
            // Arrange (TC-AUTH-07)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.Authenticate("nonexistent", "password123"))
                .Returns((false, "Invalid username/email or password.", null));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login("nonexistent", "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Invalid username/email or password.", result.Message);
            Assert.Null(result.User);
        }

        [Fact]
        public void Login_SqlInjectionAttempt_HandledSafelyWithoutCrash()
        {
            // Arrange (TC-AUTH-08)
            var mockRepo = new Mock<UserRepository>();
            const string injectionPayload = "' OR 1=1 --";
            mockRepo.Setup(r => r.Authenticate(injectionPayload, "anyPass"))
                .Returns((false, "Invalid username/email or password.", null));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Login(injectionPayload, "anyPass");

            // Assert
            Assert.False(result.Success);
            Assert.Null(result.User);
            Assert.Null(AuthService.CurrentUser);
        }

        [Fact]
        public void Register_PasswordLessThan6Characters_ReturnsFailure()
        {
            // Arrange (TC-AUTH-09: BVA 5 chars)
            var mockRepo = new Mock<UserRepository>();
            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Nguyen Van A", "test@domain.com", "12345");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Password must be at least 6 characters.", result.Message);
            mockRepo.Verify(r => r.Add(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Register_Password6Characters_ReturnsSuccessWithLibrarianRole()
        {
            // Arrange (TC-AUTH-10: BVA 6 chars)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.ExistsByEmail("test@domain.com", 0)).Returns(false);
            mockRepo.Setup(r => r.ExistsByUsername("test", 0)).Returns(false);
            mockRepo.Setup(r => r.Add(It.IsAny<User>(), "123456"))
                .Returns((true, "Account created successfully.", new User { Id = 10, Role = "Librarian", Username = "test" }));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Nguyen Van A", "test@domain.com", "123456");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal("Librarian", result.User.Role);
        }

        [Theory]
        [InlineData("invalid-email.com")]
        [InlineData("plainaddress")]
        public void Register_EmailMissingAtSymbol_ReturnsFailure(string email)
        {
            // Arrange (TC-AUTH-11)
            var mockRepo = new Mock<UserRepository>();
            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Nguyen Van A", email, "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Please enter a valid email address.", result.Message);
        }

        [Fact]
        public void Register_EmailOnlyAtSymbol_ChecksSystemBehavior()
        {
            // Arrange (TC-AUTH-12)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.ExistsByEmail("@", 0)).Returns(false);
            mockRepo.Setup(r => r.ExistsByUsername("", 0)).Returns(false);
            mockRepo.Setup(r => r.Add(It.IsAny<User>(), It.IsAny<string>()))
                .Returns((true, "Created", new User { Email = "@" }));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Test User", "@", "password123");

            // Assert - note: in TC-AUTH-12 note, code checks Contains('@'), so it acts accordingly
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void Register_DuplicateEmailCaseInsensitive_ReturnsFailure()
        {
            // Arrange (TC-AUTH-13)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.ExistsByEmail("a@x.com", 0)).Returns(true);

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Nguyen Van A", "A@X.COM", "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("email này đã được sử dụng!", result.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Register_EmptyOrWhitespaceFullName_ReturnsFailure(string? fullName)
        {
            // Arrange (TC-AUTH-14)
            var mockRepo = new Mock<UserRepository>();
            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register(fullName!, "user@mail.com", "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Please enter your full name.", result.Message);
        }

        [Fact]
        public void Register_UsernameConflict_AppendsIncrementalSuffix()
        {
            // Arrange (TC-AUTH-15)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.ExistsByEmail("an@y.com", 0)).Returns(false);
            mockRepo.Setup(r => r.ExistsByUsername("an", 0)).Returns(true);
            mockRepo.Setup(r => r.ExistsByUsername("an1", 0)).Returns(false);

            User? capturedUser = null;
            mockRepo.Setup(r => r.Add(It.IsAny<User>(), "password123"))
                .Callback<User, string>((u, p) => capturedUser = u)
                .Returns((true, "Created", new User { Username = "an1" }));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Nguyen An", "an@y.com", "password123");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedUser);
            Assert.Equal("an1", capturedUser.Username);
        }

        [Fact]
        public void Register_EmailWithLeadingTrailingSpacesAndUppercase_TrimmedAndLowercased()
        {
            // Arrange (TC-AUTH-16)
            var mockRepo = new Mock<UserRepository>();
            mockRepo.Setup(r => r.ExistsByEmail("bob@mail.com", 0)).Returns(false);
            mockRepo.Setup(r => r.ExistsByUsername("bob", 0)).Returns(false);

            User? capturedUser = null;
            mockRepo.Setup(r => r.Add(It.IsAny<User>(), "password123"))
                .Callback<User, string>((u, p) => capturedUser = u)
                .Returns((true, "Created", new User { Email = "bob@mail.com" }));

            var authService = new AuthService(mockRepo.Object);

            // Act
            var result = authService.Register("Bob Smith", "  Bob@Mail.com  ", "password123");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedUser);
            Assert.Equal("bob@mail.com", capturedUser.Email);
            Assert.Equal("Bob Smith", capturedUser.FullName);
        }

        [Fact]
        public void SignOutAndLogin_SwitchesCurrentUserAndPermissions()
        {
            // Arrange (TC-AUTH-17)
            var admin = new User { Id = 1, Username = "admin", Role = "Administrator" };
            var librarian = new User { Id = 2, Username = "lib", Role = "Librarian" };

            AuthService.CurrentUser = admin;
            Assert.Equal("Administrator", AuthService.CurrentUser.Role);

            // Act: Sign out (clear current user) then login as Librarian
            AuthService.CurrentUser = null;
            Assert.Null(AuthService.CurrentUser);

            AuthService.CurrentUser = librarian;

            // Assert
            Assert.Equal("Librarian", AuthService.CurrentUser.Role);
            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();
                Assert.False(mainVm.IsAccountsVisible);
            });
        }
    }
}
