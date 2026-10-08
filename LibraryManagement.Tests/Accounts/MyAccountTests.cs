using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using LibraryManagement.Views.Accounts;
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

        public void Dispose() => AuthService.CurrentUser = null;

        [Fact]
        public void GetMyProfile_AuthenticatedUser_ReturnsDatabaseProfile()
        {
            var user = StoredUser();
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.GetById(10)).Returns(user);

            var result = CreateService(repo).GetMyProfile();

            Assert.Equal(10, result.UserId);
            Assert.Equal("Database Name", result.FullName);
            Assert.Equal("dbuser", result.Username);
            Assert.Equal("dbuser@library.com", result.Email);
            Assert.Equal("Librarian", result.Role);
            Assert.Equal(new DateTime(2024, 1, 2), result.CreatedAt);
            repo.Verify(r => r.GetById(10), Times.Once);
        }

        [Fact]
        public void GetMyProfile_NoCurrentUser_RejectsRequest()
        {
            AuthService.CurrentUser = null;
            var repo = new Mock<UserRepository>();

            var error = Assert.Throws<BusinessRuleException>(() => CreateService(repo).GetMyProfile());

            Assert.Contains("authenticated", error.Message, StringComparison.OrdinalIgnoreCase);
            repo.Verify(r => r.GetById(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UpdateMyProfile_ValidData_UpdatesOnlyProfileFieldsAndSession()
        {
            var user = StoredUser();
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.ExistsByEmail("newemail@library.com", 10)).Returns(false);
            repo.Setup(r => r.ExistsByUsername("newusername", 10)).Returns(false);
            repo.Setup(r => r.GetById(10)).Returns(user);
            repo.Setup(r => r.UpdateProfile(10, "New Full Name", "newusername", "newemail@library.com")).Returns(true);
            int notifications = 0;
            Action<User?> handler = _ => notifications++;
            AuthService.CurrentUserChanged += handler;

            try
            {
                var result = CreateService(repo).UpdateMyProfile(
                    " New Full Name ", " NewUserName ", " NEWEMAIL@LIBRARY.COM ");

                Assert.True(result.Success);
                Assert.Equal("New Full Name", result.Profile?.FullName);
                Assert.Equal("newusername", result.Profile?.Username);
                Assert.Equal("newemail@library.com", result.Profile?.Email);
                Assert.Equal("Librarian", user.Role);
                Assert.Equal(10, AuthService.CurrentUser?.Id);
                Assert.Equal("New Full Name", AuthService.CurrentUser?.FullName);
                Assert.Equal(1, notifications);
                repo.Verify(r => r.UpdateProfile(10, "New Full Name", "newusername", "newemail@library.com"), Times.Once);
                repo.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<string?>()), Times.Never);
            }
            finally
            {
                AuthService.CurrentUserChanged -= handler;
            }
        }

        [Fact]
        public void UpdateMyProfile_AlwaysUsesAuthenticatedAccountId()
        {
            var repo = new Mock<UserRepository>();
            var user = StoredUser();
            repo.Setup(r => r.ExistsByEmail("same@library.com", 10)).Returns(false);
            repo.Setup(r => r.ExistsByUsername("dbuser", 10)).Returns(false);
            repo.Setup(r => r.GetById(10)).Returns(user);
            repo.Setup(r => r.UpdateProfile(10, "Database Name", "dbuser", "same@library.com")).Returns(true);

            var result = CreateService(repo).UpdateMyProfile("Database Name", "dbuser", "same@library.com");

            Assert.True(result.Success);
            repo.Verify(r => r.GetById(10), Times.Once);
            repo.Verify(r => r.UpdateProfile(10, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            repo.Verify(r => r.GetById(20), Times.Never);
        }

        [Fact]
        public void UpdateMyProfile_DuplicateEmail_IsRejected()
        {
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.ExistsByEmail("existing@library.com", 10)).Returns(true);

            var result = CreateService(repo).UpdateMyProfile("Name", "uniqueuser", "Existing@Library.com");

            Assert.False(result.Success);
            Assert.Equal("email này đã được sử dụng!", result.Message);
            repo.Verify(r => r.GetById(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UpdateMyProfile_DuplicateUsername_IsRejected()
        {
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.ExistsByEmail("unique@library.com", 10)).Returns(false);
            repo.Setup(r => r.ExistsByUsername("existinguser", 10)).Returns(true);

            var result = CreateService(repo).UpdateMyProfile("Name", "ExistingUser", "unique@library.com");

            Assert.False(result.Success);
            Assert.Equal("An account with this username already exists.", result.Message);
        }

        [Theory]
        [InlineData("", "user", "u@mail.com", "Please enter your full name.")]
        [InlineData("Name", "", "u@mail.com", "Please enter your username.")]
        [InlineData("Name", "user", "", "Please enter a valid email address.")]
        [InlineData("Name", "user", "invalidemail", "Please enter a valid email address.")]
        public void UpdateMyProfile_InvalidFields_ReturnValidationFailure(
            string fullName, string username, string email, string expectedMessage)
        {
            var repo = new Mock<UserRepository>();

            var result = CreateService(repo).UpdateMyProfile(fullName, username, email);

            Assert.False(result.Success);
            Assert.Equal(expectedMessage, result.Message);
            repo.Verify(r => r.UpdateProfile(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void UpdateMyProfile_FailedUpdate_DoesNotChangeSessionOrNotify()
        {
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.ExistsByEmail("email@library.com", 10)).Returns(true);
            int notifications = 0;
            Action<User?> handler = _ => notifications++;
            AuthService.CurrentUserChanged += handler;

            try
            {
                var result = CreateService(repo).UpdateMyProfile("Name", "username", "email@library.com");

                Assert.False(result.Success);
                Assert.Equal("My User", AuthService.CurrentUser?.FullName);
                Assert.Equal(0, notifications);
            }
            finally
            {
                AuthService.CurrentUserChanged -= handler;
            }
        }

        [Fact]
        public void ChangeMyPassword_ValidCurrentPassword_UsesAuthenticatedUserId()
        {
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.VerifyPassword(10, "oldPass123")).Returns(true);
            repo.Setup(r => r.ChangePassword(10, "newPass123")).Returns(true);

            var result = CreateService(repo).ChangeMyPassword("oldPass123", "newPass123", "newPass123");

            Assert.True(result.Success);
            Assert.Equal("Password changed successfully!", result.Message);
            repo.Verify(r => r.ChangePassword(10, "newPass123"), Times.Once);
            repo.Verify(r => r.ChangePassword(20, It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("Administrator")]
        [InlineData("Librarian")]
        public void ChangeMyPassword_BothSupportedRolesCanChangeOwnPassword(string role)
        {
            AuthService.CurrentUser = new User { Id = 10, Role = role };
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.VerifyPassword(10, "oldPass123")).Returns(true);
            repo.Setup(r => r.ChangePassword(10, "newPass123")).Returns(true);

            var result = CreateService(repo).ChangeMyPassword("oldPass123", "newPass123", "newPass123");

            Assert.True(result.Success);
            repo.Verify(r => r.ChangePassword(10, "newPass123"), Times.Once);
        }

        [Fact]
        public void ChangeMyPassword_WrongCurrentPassword_IsRejected()
        {
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.VerifyPassword(10, "wrongPass")).Returns(false);

            var result = CreateService(repo).ChangeMyPassword("wrongPass", "newPass123", "newPass123");

            Assert.False(result.Success);
            Assert.Equal("Current password is incorrect.", result.Message);
            repo.Verify(r => r.ChangePassword(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("", "newPass123", "newPass123", "Please enter your current password.")]
        [InlineData("oldPass", "", "", "Please enter a new password.")]
        [InlineData("oldPass", "12345", "12345", "New password must be at least 6 characters long.")]
        [InlineData("oldPass", "newPass123", "different", "New passwords do not match.")]
        public void ChangeMyPassword_InvalidFields_AreRejected(
            string current, string next, string confirm, string expectedMessage)
        {
            var repo = new Mock<UserRepository>();

            var result = CreateService(repo).ChangeMyPassword(current, next, confirm);

            Assert.False(result.Success);
            Assert.Equal(expectedMessage, result.Message);
            repo.Verify(r => r.VerifyPassword(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void ChangeMyPassword_NoCurrentUser_IsRejected()
        {
            AuthService.CurrentUser = null;

            var error = Assert.Throws<BusinessRuleException>(
                () => CreateService(new Mock<UserRepository>()).ChangeMyPassword("old", "newpass", "newpass"));

            Assert.Contains("authenticated", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Administrator", true, "Full system access")]
        [InlineData("Librarian", false, "Library operations")]
        public void GetMyAccess_UsesDatabaseRoleAndActualUserPermissions(
            string databaseRole, bool canManageUsers, string expectedAccessLevel)
        {
            AuthService.CurrentUser = new User { Id = 10, Role = "stale role" };
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.GetById(10)).Returns(new User { Id = 10, Role = databaseRole });

            var access = CreateService(repo).GetMyAccess();

            Assert.Equal(databaseRole, access.Role);
            Assert.Equal(expectedAccessLevel, access.AccessLevel);
            Assert.True(access.CanManageCatalog);
            Assert.True(access.CanManageReaders);
            Assert.True(access.CanUseCirculation);
            Assert.True(access.CanViewFeesAndHistory);
            Assert.Equal(canManageUsers, access.CanManageUsers);
            Assert.Equal(canManageUsers, UserPermissions.CanManageUsers(databaseRole));
        }

        [Fact]
        public void MyAccountViewModel_NoSession_ShowsPlaceholdersInsteadOfInventedUser()
        {
            AuthService.CurrentUser = null;
            var viewModel = new MyAccountViewModel(Mock.Of<IUserDialogService>(), new UserService(new UserRepository()));

            Assert.Equal("—", viewModel.FullName);
            Assert.Equal("—", viewModel.Username);
            Assert.Equal("—", viewModel.Email);
            Assert.Equal("—", viewModel.Role);
            Assert.Equal("—", viewModel.MemberSinceFormatted);
            Assert.Equal("—", viewModel.AccessLevel);
            Assert.Equal("—", viewModel.Initials);
            Assert.True(viewModel.IsProfileError);
        }

        [Fact]
        public void AccountSections_DefaultToProfile_NavigateAndCancelProfileEdit()
        {
            var user = StoredUser();
            var repo = new Mock<UserRepository>();
            repo.Setup(r => r.GetById(10)).Returns(user);
            var service = CreateService(repo);
            var viewModel = new MyAccountViewModel(Mock.Of<IUserDialogService>(), service);

            Assert.Equal(MyAccountViewModel.AccountSection.Profile, viewModel.SelectedSection);
            Assert.Equal(Visibility.Visible, viewModel.ProfileSummaryVisibility);

            viewModel.ToggleEditProfileCommand.Execute(null);
            Assert.Equal(Visibility.Visible, viewModel.EditProfileVisibility);
            viewModel.EditFullName = "Unsaved name";
            viewModel.CloseSidePanelCommand.Execute(null);
            Assert.Equal("Database Name", viewModel.FullName);
            Assert.Equal(Visibility.Visible, viewModel.ProfileSummaryVisibility);

            viewModel.SelectSecurityCommand.Execute(null);
            Assert.Equal(MyAccountViewModel.AccountSection.Security, viewModel.SelectedSection);
            Assert.Equal(Visibility.Visible, viewModel.SecuritySectionVisibility);
            Assert.Equal("Library operations", viewModel.AccessLevel);

            viewModel.SelectAccessRoleCommand.Execute(null);
            Assert.Equal(MyAccountViewModel.AccountSection.AccessRole, viewModel.SelectedSection);
            Assert.Equal(Visibility.Visible, viewModel.AccessRoleSectionVisibility);

            viewModel.SelectProfileCommand.Execute(null);
            Assert.Equal(MyAccountViewModel.AccountSection.Profile, viewModel.SelectedSection);
            Assert.Equal(Visibility.Visible, viewModel.ProfileSummaryVisibility);
        }

        [Fact]
        public void MyAccountView_PasswordEyeTogglesCurrentPasswordVisibility()
        {
            StaHelper.RunInSta(() =>
            {
                var repo = new Mock<UserRepository>();
                repo.Setup(r => r.GetById(10)).Returns(StoredUser());
                var viewModel = new MyAccountViewModel(
                    Mock.Of<IUserDialogService>(),
                    CreateService(repo));
                var view = new MyAccountView(viewModel);
                var passwordBox = GetField<PasswordBox>(view, "PwdCurrent");
                var visiblePasswordBox = GetField<TextBox>(view, "PwdCurrentVisible");
                var toggle = GetField<Button>(view, "BtnTogglePwdCurrent");

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, passwordBox.Visibility);
                Assert.Equal(Visibility.Visible, visiblePasswordBox.Visibility);

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, passwordBox.Visibility);
                Assert.Equal(Visibility.Collapsed, visiblePasswordBox.Visibility);
            });
        }

        private static UserService CreateService(Mock<UserRepository> repository) =>
            new(repository.Object, new AuthServiceCurrentUserContext());

        private static User StoredUser() => new()
        {
            Id = 10,
            Username = "dbuser",
            FullName = "Database Name",
            Email = "dbuser@library.com",
            Role = "Librarian",
            CreatedAt = new DateTime(2024, 1, 2)
        };

        private static T GetField<T>(object target, string name) where T : class
            => (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
                ?? throw new InvalidOperationException($"Could not find field '{name}'."));
    }
}
