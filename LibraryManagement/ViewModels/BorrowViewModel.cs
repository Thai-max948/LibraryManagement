using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Helpers;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class BorrowViewModel : BaseViewModel
    {
        private const int DefaultRecommendationLimit = 8;

        private readonly BorrowService _borrowService = new BorrowService();
        private readonly BookService _bookService = new BookService();
        private readonly BookCopyService _copyService = new BookCopyService();
        private readonly ReaderService _readerService = new ReaderService();
        private readonly IUserDialogService _dialogService;

        public ObservableCollection<Reader> Readers { get; set; } = new ObservableCollection<Reader>();
        public ObservableCollection<Book> Books { get; set; } = new ObservableCollection<Book>();
        public ObservableCollection<BorrowRow> CurrentBorrowings { get; set; } = new ObservableCollection<BorrowRow>();

        public ObservableCollection<Reader> RecommendedReaders { get; } = new ObservableCollection<Reader>();
        public ObservableCollection<Book> RecommendedBooks { get; } = new ObservableCollection<Book>();
        public ObservableCollection<BookCopy> AvailableCopies { get; } = new ObservableCollection<BookCopy>();

        private string _readerSearchQuery = string.Empty;
        public string ReaderSearchQuery
        {
            get => _readerSearchQuery;
            set
            {
                if (SetProperty(ref _readerSearchQuery, value))
                {
                    UpdateReaderRecommendations();
                }
            }
        }

        private string _bookSearchQuery = string.Empty;
        public string BookSearchQuery
        {
            get => _bookSearchQuery;
            set
            {
                if (SetProperty(ref _bookSearchQuery, value))
                {
                    UpdateBookRecommendations();
                }
            }
        }

        private string _readerRecommendationHeader = "💡 Gợi ý độc giả:";
        public string ReaderRecommendationHeader
        {
            get => _readerRecommendationHeader;
            set => SetProperty(ref _readerRecommendationHeader, value);
        }

        private string _bookRecommendationHeader = "💡 Gợi ý sách có sẵn:";
        public string BookRecommendationHeader
        {
            get => _bookRecommendationHeader;
            set => SetProperty(ref _bookRecommendationHeader, value);
        }

        private Reader? _selectedReader;
        public Reader? SelectedReader
        {
            get => _selectedReader;
            set
            {
                if (SetProperty(ref _selectedReader, value))
                {
                    OnPropertyChanged(nameof(HasSelectedReader));
                    OnPropertyChanged(nameof(HasNoSelectedReader));
                    RefreshPolicyPreview();
                    RefreshEligibilityPreview();
                }
            }
        }

        private Book? _selectedBook;
        public Book? SelectedBook
        {
            get => _selectedBook;
            set
            {
                if (SetProperty(ref _selectedBook, value))
                {
                    OnPropertyChanged(nameof(HasSelectedBook));
                    OnPropertyChanged(nameof(HasNoSelectedBook));
                    LoadAvailableCopies(value);
                }
            }
        }

        private BookCopy? _selectedCopy;
        public BookCopy? SelectedCopy
        {
            get => _selectedCopy;
            set => SetProperty(ref _selectedCopy, value);
        }

        public bool HasSelectedReader => SelectedReader != null;
        public bool HasNoSelectedReader => SelectedReader == null;

        public bool HasSelectedBook => SelectedBook != null;
        public bool HasNoSelectedBook => SelectedBook == null;

        public bool HasReaderResults => RecommendedReaders.Count > 0;
        public bool HasBookResults => RecommendedBooks.Count > 0;

        private string _policyPreview = "Chọn độc giả để xem hạn trả dự kiến.";
        public string PolicyPreview
        {
            get => _policyPreview;
            private set => SetProperty(ref _policyPreview, value);
        }

        private string _eligibilityPreview = string.Empty;
        public string EligibilityPreview
        {
            get => _eligibilityPreview;
            private set => SetProperty(ref _eligibilityPreview, value);
        }

        private void RefreshEligibilityPreview()
        {
            if (SelectedReader == null)
            {
                EligibilityPreview = string.Empty;
                return;
            }
            try
            {
                var result = _borrowService.GetReaderEligibility(SelectedReader.ReaderId);
                EligibilityPreview = result.IsEligible
                    ? "Đủ điều kiện mượn sách."
                    : "Chưa đủ điều kiện: " + result.ReasonSummary;
            }
            catch (Exception ex)
            {
                EligibilityPreview = "Không thể kiểm tra điều kiện mượn: " + ex.Message;
            }
        }

        private void RefreshPolicyPreview()
        {
            if (SelectedReader == null)
            {
                PolicyPreview = "Chọn độc giả để xem hạn trả dự kiến.";
                return;
            }
            try
            {
                var policy = new LoanPolicyService().GetPolicyFor(SelectedReader);
                var dueDate = LoanPolicyService.CalculateDueDate(policy, DateTime.Now);
                PolicyPreview = $"Ngày mượn: {DateTime.Today:dd/MM/yyyy}  •  {SelectedReader.ReaderType}: {policy.LoanPeriodDays} ngày  •  Hạn trả dự kiến: {dueDate:dd/MM/yyyy}";
            }
            catch (Exception ex)
            {
                PolicyPreview = ex.Message;
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand BorrowCommand { get; }
        public ICommand SelectReaderCommand { get; }
        public ICommand ClearReaderSelectionCommand { get; }
        public ICommand SelectBookCommand { get; }
        public ICommand ClearBookSelectionCommand { get; }
        public ICommand ResolveBarcodeCommand { get; }

        private string _barcodeInput = string.Empty;
        public string BarcodeInput
        {
            get => _barcodeInput;
            set
            {
                if (SetProperty(ref _barcodeInput, value)) SelectedCopy = null;
            }
        }
        private string _barcodeFeedback = string.Empty;
        public string BarcodeFeedback
        {
            get => _barcodeFeedback;
            private set => SetProperty(ref _barcodeFeedback, value);
        }

        private void ResolveBarcode()
        {
            SelectedCopy = null;
            try
            {
                var copy = _copyService.GetByBarcode(BarcodeInput);
                if (copy == null) throw new BusinessRuleException("Không tìm thấy bản sách có barcode này.");
                var reason = BookCopyService.GetBorrowBlockReason(copy);
                if (reason != null) throw new BusinessRuleException(reason);
                var book = _bookService.GetBookById(copy.BookId);
                if (book == null || book.Status == BookStatuses.Archived)
                    throw new BusinessRuleException("Đầu sách không tồn tại hoặc đã được lưu trữ.");
                // Exact lookup does not load the entire available-copy list.
                _selectedBook = book;
                OnPropertyChanged(nameof(SelectedBook));
                OnPropertyChanged(nameof(HasSelectedBook));
                OnPropertyChanged(nameof(HasNoSelectedBook));
                AvailableCopies.Clear();
                AvailableCopies.Add(copy);
                SelectedCopy = copy;
                BarcodeFeedback = $"{copy.Barcode} · {book.Title} · Available";
            }
            catch (BusinessRuleException exception) { BarcodeFeedback = exception.Message; }
            catch (Exception) { BarcodeFeedback = "Không thể tra barcode. Vui lòng thử lại."; }
        }

        public BorrowViewModel(IUserDialogService dialogService)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            BorrowCommand = new RelayCommand(DoBorrow);
            SelectReaderCommand = new RelayCommand(param => SelectReader(param as Reader));
            ClearReaderSelectionCommand = new RelayCommand(_ => ClearReaderSelection());
            SelectBookCommand = new RelayCommand(param => SelectBook(param as Book));
            ClearBookSelectionCommand = new RelayCommand(_ => ClearBookSelection());
            ResolveBarcodeCommand = new RelayCommand(ResolveBarcode);

            LoadDropdowns();
            LoadCurrentBorrowings();
        }

        public void SelectReader(Reader? reader)
        {
            if (reader == null)
            {
                return;
            }

            SelectedReader = reader;
            _readerSearchQuery = reader.FullName;
            OnPropertyChanged(nameof(ReaderSearchQuery));
        }

        public void ClearReaderSelection()
        {
            SelectedReader = null;
            _readerSearchQuery = string.Empty;
            OnPropertyChanged(nameof(ReaderSearchQuery));
            UpdateReaderRecommendations();
        }

        public void SelectBook(Book? book)
        {
            if (book == null)
            {
                return;
            }

            SelectedBook = book;
            if (AvailableCopies.Count == 0)
            {
                _dialogService.ShowWarning(
                    $"Đầu sách \"{book.Title}\" hiện không có bản nào ở trạng thái Available.",
                    "Sách đã hết");
                ClearBookSelection();
                return;
            }
            _bookSearchQuery = book.Title;
            OnPropertyChanged(nameof(BookSearchQuery));
        }

        public void ClearBookSelection()
        {
            SelectedBook = null;
            AvailableCopies.Clear();
            SelectedCopy = null;
            _bookSearchQuery = string.Empty;
            OnPropertyChanged(nameof(BookSearchQuery));
            UpdateBookRecommendations();
        }

        private void LoadAvailableCopies(Book? book)
        {
            AvailableCopies.Clear();
            SelectedCopy = null;
            if (book == null) return;
            try
            {
                foreach (var copy in _copyService.GetAvailableCopies(book.BookId))
                    AvailableCopies.Add(copy);
                SelectedCopy = AvailableCopies.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Không thể tải danh sách bản sách: " + ex.Message, "Lỗi");
            }
        }

        private void UpdateReaderRecommendations()
        {
            RecommendedReaders.Clear();
            var activeReaders = Readers.Where(r => !r.IsDeleted);

            if (string.IsNullOrWhiteSpace(ReaderSearchQuery))
            {
                ReaderRecommendationHeader = "💡 Gợi ý độc giả:";
                foreach (var reader in activeReaders.Take(DefaultRecommendationLimit))
                {
                    RecommendedReaders.Add(reader);
                }
            }
            else
            {
                string query = ReaderSearchQuery.Trim();
                var matches = activeReaders.Where(r =>
                    StringHelper.ContainsNormalized(r.FullName, query) ||
                    StringHelper.ContainsNormalized(r.Phone, query) ||
                    StringHelper.ContainsNormalized(r.Email, query) ||
                    r.ReaderId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.FormattedId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    StringHelper.ContainsNormalized(r.StudentId ?? string.Empty, query) ||
                    StringHelper.ContainsNormalized(r.IdentityNumber ?? string.Empty, query))
                    .Take(DefaultRecommendationLimit)
                    .ToList();

                ReaderRecommendationHeader = matches.Count > 0
                    ? $"🔍 Gợi ý phù hợp ({matches.Count}):"
                    : $"⚠️ Không tìm thấy độc giả khớp với \"{query}\"";

                foreach (var reader in matches)
                {
                    RecommendedReaders.Add(reader);
                }
            }

            OnPropertyChanged(nameof(ReaderRecommendationHeader));
            OnPropertyChanged(nameof(HasReaderResults));
        }

        private void UpdateBookRecommendations()
        {
            RecommendedBooks.Clear();

            if (string.IsNullOrWhiteSpace(BookSearchQuery))
            {
                BookRecommendationHeader = "💡 Gợi ý sách có sẵn:";
                foreach (var book in Books.Where(b => b.AvailableQuantity > 0).Take(DefaultRecommendationLimit))
                {
                    RecommendedBooks.Add(book);
                }
            }
            else
            {
                string query = BookSearchQuery.Trim();
                var matches = Books.Where(b =>
                    StringHelper.ContainsNormalized(b.Title, query) ||
                    StringHelper.ContainsNormalized(b.Author, query) ||
                    StringHelper.ContainsNormalized(b.Category, query) ||
                    b.BookId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.AvailableQuantity > 0)
                    .Take(DefaultRecommendationLimit)
                    .ToList();

                BookRecommendationHeader = matches.Count > 0
                    ? $"🔍 Gợi ý phù hợp ({matches.Count}):"
                    : $"⚠️ Không tìm thấy sách khớp với \"{query}\"";

                foreach (var book in matches)
                {
                    RecommendedBooks.Add(book);
                }
            }

            OnPropertyChanged(nameof(BookRecommendationHeader));
            OnPropertyChanged(nameof(HasBookResults));
        }

        private void LoadDropdowns()
        {
            try
            {
                Readers.Clear();
                foreach (var r in _readerService.GetAllReaders())
                {
                    Readers.Add(r);
                }

                Books.Clear();
                foreach (var b in _bookService.GetAllBooks())
                {
                    Books.Add(b);
                }

                UpdateReaderRecommendations();
                UpdateBookRecommendations();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Không thể tải danh sách Reader/Book: " + ex.Message, "Lỗi");
            }
        }

        private void LoadCurrentBorrowings()
        {
            try
            {
                CurrentBorrowings.Clear();
                var records = _borrowService.GetBorrowingBooks();
                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders(includeDeleted: true);

                foreach (var r in records.OrderByDescending(x => x.BorrowId))
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    CurrentBorrowings.Add(new BorrowRow
                    {
                        ReaderName = reader != null
                            ? (reader.IsDeleted ? $"{reader.FullName} (Đã xóa)" : reader.FullName)
                            : "?",
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = r.DueDate.ToString("dd/MM/yyyy")
                    });
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Không thể tải danh sách đang mượn: " + ex.Message, "Lỗi");
            }
        }

        private void DoBorrow()
        {
            if (IsBusy) return;
            if (SelectedReader == null || SelectedBook == null || SelectedCopy == null)
            {
                _dialogService.ShowInfo("Vui lòng chọn độc giả, đầu sách và bản sách (barcode).", "Thiếu thông tin");
                return;
            }

            IsBusy = true;
            try
            {
                _borrowService.BorrowBook(SelectedReader.ReaderId, SelectedCopy.CopyId);
                _dialogService.ShowInfo("Mượn sách thành công.", "Thành công");

                LoadDropdowns();
                LoadCurrentBorrowings();
                ClearReaderSelection();
                ClearBookSelection();
                BarcodeInput = string.Empty;
                BarcodeFeedback = string.Empty;
            }
            catch (BusinessRuleException ex)
            {
                if (SelectedBook != null)
                    LoadAvailableCopies(SelectedBook);
                _dialogService.ShowError(ex.Message, "Không thể mượn sách");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public class BorrowRow
    {
        public string ReaderName { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string BorrowDate { get; set; } = string.Empty;
        public string DueDate { get; set; } = string.Empty;
    }
}
