using LibraryManagement.Commands;
using System.Windows.Input;
using LibraryManagement.Views;
using System.Linq.Expressions;
using LibraryManagement.Views.Books;
using LibraryManagement.Views.Readers;

namespace LibraryManagement.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
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

        public MainViewModel()
        {
            ShowDashboardCommand = new RelayCommand(ShowDashboard);
            ShowBooksCommand = new RelayCommand(ShowBooks);
            ShowReadersCommand = new RelayCommand(ShowReaders);
            ShowBorrowCommand = new RelayCommand(ShowBorrow);
            ShowReturnCommand = new RelayCommand(ShowReturn);
            ShowHistoryCommand = new RelayCommand(ShowHistory);

            ShowDashboard(); // view mặc định khi mở app
        }

        // Bước 5 đang làm khung -> tạm để placeholder text, Bước sau thay bằng ViewModel/View thật.
        private void ShowDashboard() { CurrentViewName = "Dashboard"; CurrentView = new DashboardView(); }
        private void ShowBooks() { CurrentViewName = "Books"; CurrentView = new BooksView(); }
        private void ShowReaders() { CurrentViewName = "Readers"; CurrentView = new ReadersView(); }
        private void ShowBorrow() { CurrentViewName = "Borrow"; CurrentView = new BorrowView(); }
        private void ShowReturn() { CurrentViewName = "Return"; CurrentView = new ReturnView(); }
        private void ShowHistory() { CurrentViewName = "History"; CurrentView = new HistoryView(); }
    }
}