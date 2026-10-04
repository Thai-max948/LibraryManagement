using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Views.Books;

namespace LibraryManagement.ViewModels
{
    public class BooksViewModel : BaseViewModel
    {
        private readonly BookService _bookService;
        private readonly BorrowService? _borrowService;
        private int _totalBorrowed;

        public ObservableCollection<Book> Books { get; set; } = new();

        private string _searchText = string.Empty;
        private string _languageFilter = LanguageCatalog.AllFilterCode;
        public string LanguageFilter
        {
            get => _languageFilter;
            set
            {
                if (SetProperty(ref _languageFilter, value)) Search();
            }
        }
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                Search();
            }
        }

        private Book? _selectedBook;
        public Book? SelectedBook
        {
            get => _selectedBook;
            set
            {
                SetProperty(ref _selectedBook, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _showArchived;
        public bool ShowArchived
        {
            get => _showArchived;
            set
            {
                if (SetProperty(ref _showArchived, value))
                {
                    SelectedBook = null;
                    Search();
                }
            }
        }

        public int TotalBooks => Books.Sum(b => b.Quantity);
        public int TotalAvailable => Books.Sum(b => b.AvailableQuantity);
        public int TotalBorrowed => _totalBorrowed;

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ArchiveCommand { get; }
        public ICommand RestoreCommand { get; }

        public BooksViewModel() : this(new BookService(), new BorrowService())
        {
        }

        public BooksViewModel(BookService bookService, BorrowService? borrowService = null)
        {
            _bookService = bookService;
            _borrowService = borrowService;
            AddCommand = new RelayCommand(AddBook);
            EditCommand = new RelayCommand(EditBook, () => SelectedBook?.Status == BookStatuses.Active);
            ArchiveCommand = new RelayCommand(ArchiveBook, () => SelectedBook?.Status == BookStatuses.Active);
            RestoreCommand = new RelayCommand(RestoreBook, () => SelectedBook?.Status == BookStatuses.Archived);
            Load();
        }

        public void Load()
        {
            try
            {
                Books.Clear();
                foreach (var b in FilterLanguage(ShowArchived ? _bookService.GetArchivedBooks() : _bookService.GetAllBooks()))
                {
                    Books.Add(b);
                }
                RaiseStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách sách: " + ex.Message, "Lỗi");
            }
        }

        private void Search()
        {
            try
            {
                Books.Clear();
                foreach (var b in FilterLanguage(ShowArchived ? _bookService.SearchArchivedBooks(SearchText) : _bookService.SearchBook(SearchText)))
                {
                    Books.Add(b);
                }
                RaiseStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tìm kiếm sách: " + ex.Message, "Lỗi");
            }
        }

        private void AddBook()
        {
            var dialog = new AddBookDialog(_bookService.PricingPolicy);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _bookService.AddBook(dialog.ResultBook);
                    Load();
                }
                catch (DuplicateBookIsbnException ex)
                {
                    try
                    {
                        var existing = _bookService.GetAllBooksIncludingArchived().FirstOrDefault(book => book.BookId == ex.ExistingBookId);
                        if (existing == null) throw new BusinessRuleException("Không tìm thấy đầu sách có ISBN này.");
                        string prompt = existing.Status == BookStatuses.Archived
                            ? "\nĐầu sách đang lưu trữ. Khôi phục và mở quản lý bản sách?"
                            : "\nMở quản lý bản sách để thêm bản mới?";
                        if (MessageBox.Show(ex.Message + prompt, "Đầu sách đã tồn tại", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            if (existing.Status == BookStatuses.Archived)
                            {
                                _bookService.RestoreBook(existing.BookId);
                                existing.Status = BookStatuses.Active;
                            }
                            new BookCopiesDialog(existing).ShowDialog();
                            Load();
                        }
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(error.Message, "Không thể quản lý bản sách");
                    }
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể thêm sách");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        private void EditBook()
        {
            if (SelectedBook == null)
            {
                return;
            }

            var dialog = new EditBookDialog(SelectedBook, _bookService.PricingPolicy);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _bookService.UpdateBook(dialog.ResultBook);
                    Load();
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể cập nhật sách");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        private void ArchiveBook()
        {
            if (SelectedBook == null)
            {
                return;
            }

            var confirm = MessageBox.Show($"Lưu trữ đầu sách \"{SelectedBook.Title}\"? Sách sẽ rời danh mục hoạt động; lịch sử mượn được giữ lại.", "Xác nhận lưu trữ", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _bookService.ArchiveBook(SelectedBook.BookId);
                Load();
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể lưu trữ sách");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
            }
        }

        private void RestoreBook()
        {
            if (SelectedBook == null) return;
            try
            {
                _bookService.RestoreBook(SelectedBook.BookId);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Không thể khôi phục sách");
            }
        }

        private void RaiseStats()
        {
            _totalBorrowed = Books.All(book => book.BorrowedCopies.HasValue)
                ? Books.Sum(book => book.BorrowedCopies!.Value)
                : _borrowService == null
                    ? Books.Sum(b => b.Quantity - b.AvailableQuantity)
                    : _borrowService.GetBorrowingBooks().Count(record => Books.Any(book => book.BookId == record.BookId));
            OnPropertyChanged(nameof(TotalBooks));
            OnPropertyChanged(nameof(TotalAvailable));
            OnPropertyChanged(nameof(TotalBorrowed));
        }

        private System.Collections.Generic.IEnumerable<Book> FilterLanguage(System.Collections.Generic.IEnumerable<Book> books)
        {
            var code = LanguageFilter?.Trim();
            if (string.IsNullOrEmpty(code) || code.Equals(LanguageCatalog.AllFilterCode, StringComparison.OrdinalIgnoreCase))
                return books;
            if (code.Equals(LanguageCatalog.UnknownFilterCode, StringComparison.OrdinalIgnoreCase))
                return books.Where(book => string.IsNullOrWhiteSpace(book.Language));
            return books.Where(book => string.Equals(book.Language, code, StringComparison.OrdinalIgnoreCase));
        }
    }
}
