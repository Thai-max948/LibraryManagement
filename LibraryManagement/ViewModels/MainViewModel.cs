using System;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Services;
using LibraryManagement.Views;
using LibraryManagement.Views.Accounts;
using LibraryManagement.Views.Books;
using LibraryManagement.Views.Readers;

namespace LibraryManagement.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly Func<string, object> _createView;

        private static object CreateDefaultView(string name) => name switch
        {
            "Dashboard" => new DashboardView(),
            "Books" => new BooksView(),
            "Readers" => new ReadersView(),
            "Borrow" => new BorrowView(),
            "Return" => new ReturnView(),
            "History" => new HistoryView(),
            "Fees" => new FeesView(),
            "MyAccount" => new MyAccountView(),
            "Accounts" => new AccountsView(),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
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

        private object? _currentView;
        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _currentViewName = string.Empty;
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
        public ICommand ShowFeesCommand { get; }
        public ICommand ShowMyAccountCommand { get; }
        public ICommand ShowAccountsCommand { get; }
        public NotificationViewModel Notifications { get; } = new();

        public MainViewModel() : this(CreateDefaultView)
        {
        }

        public MainViewModel(Func<string, object> createView)
        {
            _createView = createView ?? throw new ArgumentNullException(nameof(createView));
            ShowDashboardCommand = new RelayCommand(ShowDashboard);
            ShowBooksCommand = new RelayCommand(ShowBooks);
            ShowReadersCommand = new RelayCommand(ShowReaders);
            ShowBorrowCommand = new RelayCommand(ShowBorrow);
            ShowReturnCommand = new RelayCommand(ShowReturn);
            ShowHistoryCommand = new RelayCommand(ShowHistory);
            ShowFeesCommand = new RelayCommand(ShowFees);
            ShowMyAccountCommand = new RelayCommand(ShowMyAccount);
            ShowAccountsCommand = new RelayCommand(_ => ShowAccounts(), _ => IsAccountsVisible);

            ShowDashboard();
        }

        private void ShowDashboard()
        {
            CurrentViewName = "Dashboard";
            CurrentView = _createView("Dashboard");
        }

        private void ShowBooks()
        {
            CurrentViewName = "Books";
            CurrentView = _createView("Books");
        }

        private void ShowReaders()
        {
            CurrentViewName = "Readers";
            CurrentView = _createView("Readers");
        }

        private void ShowBorrow()
        {
            CurrentViewName = "Borrow";
            CurrentView = _createView("Borrow");
        }

        private void ShowReturn()
        {
            CurrentViewName = "Return";
            CurrentView = _createView("Return");
        }

        private void ShowHistory()
        {
            CurrentViewName = "History";
            CurrentView = _createView("History");
        }

        private void ShowFees()
        {
            CurrentViewName = "Fees";
            CurrentView = _createView("Fees");
        }

        private void ShowMyAccount()
        {
            CurrentViewName = "MyAccount";
            CurrentView = _createView("MyAccount");
            OnPropertyChanged(nameof(CurrentUserName));
            OnPropertyChanged(nameof(CurrentUserRole));
        }

        private void ShowAccounts()
        {
            if (!IsAccountsVisible)
            {
                return;
            }
            CurrentViewName = "Accounts";
            CurrentView = _createView("Accounts");
        }
    }
}
