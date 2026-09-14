using LibraryManagement.Commands;
using System.Windows.Input;
using LibraryManagement.Views;
using System.Linq.Expressions;
using LibraryManagement.Views.Books;
using LibraryManagement.Views.Readers;
using LibraryManagement.Views.Accounts;

using System;
using System.Windows;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private static bool HasAccountManagementRole()
        {
            return string.Equals(
                AuthService.CurrentUser?.Role?.Trim(),
                "Administrator",
                StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAccountsVisible => HasAccountManagementRole();

        public Visibility AccountsVisibility =>
            IsAccountsVisible ? Visibility.Visible : Visibility.Collapsed;

        public string CurrentUserName => AuthService.CurrentUser?.FullName ?? "SYSTEM ONLINE";

        public string CurrentUserRole => AuthService.CurrentUser != null
            ? $"{AuthService.CurrentUser.Role} • Active Node"
            : "v2.4 • Active Node";

        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _currentViewName;
        public string CurrentViewName
        {
            get => _currentViewName;
            set => SetProperty(ref _currentViewName, value);
        }

        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowBooksCommand { get; }
        public ICommand ShowReadersCommand { get; }
        public ICommand ShowBorrowCommand { get; }
        public ICommand ShowReturnCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand ShowMyAccountCommand { get; }
        public ICommand ShowAccountsCommand { get; }

        public MainViewModel()
        {
            ShowDashboardCommand = new RelayCommand(ShowDashboard);
            ShowBooksCommand = new RelayCommand(ShowBooks);
            ShowReadersCommand = new RelayCommand(ShowReaders);
            ShowBorrowCommand = new RelayCommand(ShowBorrow);
            ShowReturnCommand = new RelayCommand(ShowReturn);
            ShowHistoryCommand = new RelayCommand(ShowHistory);
            ShowMyAccountCommand = new RelayCommand(ShowMyAccount);
            ShowAccountsCommand = new RelayCommand(_ => ShowAccounts(), _ => IsAccountsVisible);

            ShowDashboard(); // view mặc định khi mở app
        }

        private void ShowDashboard() { CurrentViewName = "Dashboard"; CurrentView = new DashboardView(); }
        private void ShowBooks() { CurrentViewName = "Books"; CurrentView = new BooksView(); }
        private void ShowReaders() { CurrentViewName = "Readers"; CurrentView = new ReadersView(); }
        private void ShowBorrow() { CurrentViewName = "Borrow"; CurrentView = new BorrowView(); }
        private void ShowReturn() { CurrentViewName = "Return"; CurrentView = new ReturnView(); }
        private void ShowHistory() { CurrentViewName = "History"; CurrentView = new HistoryView(); }
        private void ShowMyAccount() 
        { 
            CurrentViewName = "MyAccount"; 
            CurrentView = new MyAccountView(); 
            OnPropertyChanged(nameof(CurrentUserName));
            OnPropertyChanged(nameof(CurrentUserRole));
        }
        private void ShowAccounts()
        {
            if (!IsAccountsVisible) return;
            CurrentViewName = "Accounts";
            CurrentView = new AccountsView();
        }
    }
}
