using System;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Xunit;

namespace LibraryManagement.Tests
{
    public class NavigationTests : IDisposable
    {
        public NavigationTests()
        {
            AuthService.CurrentUser = new User
            {
                Id = 1,
                Username = "admin",
                FullName = "Admin User",
                Role = "Administrator"
            };
        }

        public void Dispose()
        {
            AuthService.CurrentUser = null;
        }

        [Fact]
        public void SidebarNavigation_AllModules_SwitchesCurrentViewAndName()
        {
            // Arrange & Act & Assert (TC-NAV-01)
            AuthService.CurrentUser = new User
            {
                Id = 1,
                Username = "admin",
                FullName = "Admin User",
                Role = "Administrator"
            };

            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();

                // 1. Dashboard
                mainVm.ShowDashboardCommand.Execute(null);
                Assert.Equal("Dashboard", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 2. Books
                mainVm.ShowBooksCommand.Execute(null);
                Assert.Equal("Books", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 3. Readers
                mainVm.ShowReadersCommand.Execute(null);
                Assert.Equal("Readers", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 4. Borrow
                mainVm.ShowBorrowCommand.Execute(null);
                Assert.Equal("Borrow", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 5. Return
                mainVm.ShowReturnCommand.Execute(null);
                Assert.Equal("Return", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 6. History
                mainVm.ShowHistoryCommand.Execute(null);
                Assert.Equal("History", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 7. My Account
                mainVm.ShowMyAccountCommand.Execute(null);
                Assert.Equal("MyAccount", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);

                // 8. User Accounts (Admin)
                Assert.True(mainVm.ShowAccountsCommand.CanExecute(null));
                mainVm.ShowAccountsCommand.Execute(null);
                Assert.Equal("Accounts", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);
            });
        }

        [Fact]
        public void SidebarNavigation_SwitchBackAndForth_UpdatesViewCorrectly()
        {
            // Arrange & Act & Assert (TC-NAV-02)
            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();

                mainVm.ShowBooksCommand.Execute(null);
                Assert.Equal("Books", mainVm.CurrentViewName);

                mainVm.ShowReadersCommand.Execute(null);
                Assert.Equal("Readers", mainVm.CurrentViewName);

                mainVm.ShowBooksCommand.Execute(null);
                Assert.Equal("Books", mainVm.CurrentViewName);
            });
        }

        [Fact]
        public void SidebarNavigation_RapidSwitching10Times_DoesNotCrashAndEndsOnCorrectView()
        {
            // Arrange & Act & Assert (TC-NAV-03)
            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();

                for (int i = 0; i < 10; i++)
                {
                    mainVm.ShowBooksCommand.Execute(null);
                    mainVm.ShowReadersCommand.Execute(null);
                    mainVm.ShowBorrowCommand.Execute(null);
                }

                Assert.Equal("Borrow", mainVm.CurrentViewName);
                Assert.NotNull(mainVm.CurrentView);
            });
        }

        [Fact]
        public void SidebarNavigation_LibrarianRole_CannotExecuteShowAccounts()
        {
            // Arrange & Act & Assert (TC-NAV-04 & Role permission)
            AuthService.CurrentUser = new User
            {
                Id = 2,
                Username = "librarian",
                FullName = "Librarian",
                Role = "Librarian"
            };

            StaHelper.RunInSta(() =>
            {
                var mainVm = new MainViewModel();

                Assert.False(mainVm.IsAccountsVisible);
                Assert.Equal(System.Windows.Visibility.Collapsed, mainVm.AccountsVisibility);
                Assert.False(mainVm.ShowAccountsCommand.CanExecute(null));
            });
        }
    }
}
