using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class BorrowViewModel : BaseViewModel
    {
        private const int RecommendationPageSize = 5;
        private const int SearchDebounceMilliseconds = 300;

        private readonly BorrowService _borrowService;
        private readonly BookService _bookService;
        private readonly BookCopyService _copyService;
        private readonly ReaderService _readerService;
        private readonly LoanPolicyService _loanPolicyService;
        private readonly IUserDialogService _dialogService;
        private readonly Func<int, IReadOnlyList<int>, IReadOnlyList<int>> _borrowBooks;
        private CancellationTokenSource? _readerSearchDebounceCts;
        private CancellationTokenSource? _bookSearchDebounceCts;
        private long _readerRecommendationGeneration;
        private long _bookRecommendationGeneration;

        private int _readerRecommendationPage = 1;
        public int ReaderRecommendationPage
        {
            get => _readerRecommendationPage;
            private set => SetProperty(ref _readerRecommendationPage, value);
        }

        private bool _readerRecommendationHasMore;
        public bool ReaderRecommendationHasMore
        {
            get => _readerRecommendationHasMore;
            private set => SetProperty(ref _readerRecommendationHasMore, value);
        }

        private bool _isLoadingMoreReaders;
        public bool IsLoadingMoreReaders
        {
            get => _isLoadingMoreReaders;
            private set => SetProperty(ref _isLoadingMoreReaders, value);
        }

        private int _bookRecommendationPage = 1;
        public int BookRecommendationPage
        {
            get => _bookRecommendationPage;
            private set => SetProperty(ref _bookRecommendationPage, value);
        }

        private bool _bookRecommendationHasMore;
        public bool BookRecommendationHasMore
        {
            get => _bookRecommendationHasMore;
            private set => SetProperty(ref _bookRecommendationHasMore, value);
        }

        private bool _isLoadingMoreBooks;
        public bool IsLoadingMoreBooks
        {
            get => _isLoadingMoreBooks;
            private set => SetProperty(ref _isLoadingMoreBooks, value);
        }

        internal Task InitialRecommendationsLoaded { get; private set; } = Task.CompletedTask;

        public ObservableCollection<BorrowRow> CurrentBorrowings { get; set; } = new ObservableCollection<BorrowRow>();

        private int _currentBorrowingsPageNumber = 1;
        public int CurrentBorrowingsPageNumber
        {
            get => _currentBorrowingsPageNumber;
            private set => SetProperty(ref _currentBorrowingsPageNumber, value);
        }

        public int CurrentBorrowingsPageSize => CurrentBorrowingPageQuery.FixedPageSize;

        private int _currentBorrowingsTotalCount;
        public int CurrentBorrowingsTotalCount
        {
            get => _currentBorrowingsTotalCount;
            private set => SetProperty(ref _currentBorrowingsTotalCount, value);
        }

        private int _currentBorrowingsTotalPages = 1;
        public int CurrentBorrowingsTotalPages
        {
            get => _currentBorrowingsTotalPages;
            private set => SetProperty(ref _currentBorrowingsTotalPages, value);
        }

        public bool CanGoPreviousBorrowings => CurrentBorrowingsPageNumber > 1;
        public bool CanGoNextBorrowings => CurrentBorrowingsPageNumber < CurrentBorrowingsTotalPages;
        public string ShowingBorrowingsText
        {
            get
            {
                if (CurrentBorrowingsTotalCount == 0) return "Showing 0 of 0";
                int first = (CurrentBorrowingsPageNumber - 1) * CurrentBorrowingsPageSize + 1;
                int last = Math.Min(CurrentBorrowingsPageNumber * CurrentBorrowingsPageSize, CurrentBorrowingsTotalCount);
                return $"Showing {first}-{last} of {CurrentBorrowingsTotalCount}";
            }
        }

        public string CurrentBorrowingsPageText =>
            $"Page {CurrentBorrowingsPageNumber} / {CurrentBorrowingsTotalPages}";

        public ObservableCollection<Reader> RecommendedReaders { get; } = new ObservableCollection<Reader>();
        public ObservableCollection<Book> RecommendedBooks { get; } = new ObservableCollection<Book>();
        public ObservableCollection<BookCopy> AvailableCopies { get; } = new ObservableCollection<BookCopy>();
        public ObservableCollection<SelectedBorrowCopy> SelectedBorrowCopies { get; } = new();

        private string _readerSearchQuery = string.Empty;
        public string ReaderSearchQuery
        {
            get => _readerSearchQuery;
            set
            {
                if (SetProperty(ref _readerSearchQuery, value))
                {
                    ScheduleReaderSearch();
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
                    BookSearchError = string.Empty;
                    if (BookCopyBarcode.LooksLikeBarcode(value ?? string.Empty))
                    {
                        CancelPendingBookSearch();
                        ResetBookRecommendationPaging();
                        RecommendedBooks.Clear();
                        BookRecommendationHeader = "Nhấn Enter để tra cứu chính xác barcode.";
                        OnPropertyChanged(nameof(HasBookResults));
                    }
                    else
                    {
                        ScheduleBookSearch();
                    }
                }
            }
        }

        private string _bookSearchError = string.Empty;
        public string BookSearchError
        {
            get => _bookSearchError;
            private set
            {
                if (SetProperty(ref _bookSearchError, value))
                    OnPropertyChanged(nameof(HasBookSearchError));
            }
        }
        public bool HasBookSearchError => !string.IsNullOrWhiteSpace(BookSearchError);

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
                int? previousReaderId = _selectedReader?.ReaderId;
                if (SetProperty(ref _selectedReader, value))
                {
                    if (previousReaderId != value?.ReaderId)
                    {
                        _readerEligibility = null;
                        ClearSelectedBorrowCopies();
                    }
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
                    RefreshBorrowSelectionState();
                }
            }
        }

        private BookCopy? _selectedCopy;
        public BookCopy? SelectedCopy
        {
            get => _selectedCopy;
            set
            {
                if (SetProperty(ref _selectedCopy, value))
                    RefreshBorrowSelectionState();
            }
        }

        public bool HasSelectedReader => SelectedReader != null;
        public bool HasNoSelectedReader => SelectedReader == null;

        public bool HasSelectedBook => SelectedBook != null;
        public bool HasNoSelectedBook => SelectedBook == null;
        public bool HasSelectedBorrowCopies => SelectedBorrowCopies.Count > 0;
        public int SelectedBorrowCount => SelectedBorrowCopies.Count;
        public int CurrentActiveLoans => _readerEligibility?.CurrentLoans ?? 0;
        public int RemainingBorrowSlots => SelectedReader == null || _readerEligibility == null
            ? 0
            : Math.Max(0, ReaderEligibilityService.BorrowLimit - CurrentActiveLoans);
        public int RemainingSelectionSlots => Math.Max(0, RemainingBorrowSlots - SelectedBorrowCount);
        public string ReaderLoanCapacityText => SelectedReader == null
            ? "Chọn độc giả để xem số lượt mượn còn lại."
            : $"Đang mượn {CurrentActiveLoans}/{ReaderEligibilityService.BorrowLimit} • Còn lượt mượn: {RemainingBorrowSlots}";
        public string ConfirmBorrowText => $"📖 Xác Nhận Mượn {SelectedBorrowCount} Sách";
        public bool CanAddAnotherCopy => !IsBusy && SelectedReader != null &&
            _readerEligibility?.IsEligible == true && SelectedBook != null && SelectedCopy != null &&
            SelectedBorrowCount < RemainingBorrowSlots &&
            !SelectedBorrowCopies.Any(item => item.CopyId == SelectedCopy.CopyId);
        public bool CanConfirmBorrow => !IsBusy && SelectedReader != null &&
            _readerEligibility?.IsEligible == true && SelectedBorrowCount > 0 &&
            SelectedBorrowCount <= RemainingBorrowSlots;

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

        private ReaderEligibilityResult? _readerEligibility;

        private void RefreshEligibilityPreview()
        {
            if (SelectedReader == null)
            {
                _readerEligibility = null;
                EligibilityPreview = string.Empty;
                RefreshBorrowSelectionState();
                return;
            }
            try
            {
                var result = _borrowService.GetReaderEligibility(SelectedReader.ReaderId);
                _readerEligibility = result;
                EligibilityPreview = result.IsEligible
                    ? "Đủ điều kiện mượn sách."
                    : "Chưa đủ điều kiện: " + result.ReasonSummary;
            }
            catch (Exception ex)
            {
                _readerEligibility = null;
                EligibilityPreview = "Không thể kiểm tra điều kiện mượn: " + ex.Message;
            }
            RefreshBorrowSelectionState();
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
                var policy = _loanPolicyService.GetPolicyFor(SelectedReader);
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
            set
            {
                if (SetProperty(ref _isBusy, value))
                    RefreshBorrowSelectionState();
            }
        }

        public ICommand BorrowCommand { get; }
        public ICommand AddSelectedCopyCommand { get; }
        public ICommand RemoveSelectedCopyCommand { get; }
        public ICommand SelectReaderCommand { get; }
        public ICommand ClearReaderSelectionCommand { get; }
        public ICommand SelectBookCommand { get; }
        public ICommand ClearBookSelectionCommand { get; }
        public ICommand SearchBookInputCommand { get; }
        public ICommand PreviousBorrowingsPageCommand { get; }
        public ICommand NextBorrowingsPageCommand { get; }

        private void HandleBookSearchEnter()
        {
            BookSearchError = string.Empty;
            string barcode = BookSearchQuery.Trim();
            if (!BookCopyBarcode.LooksLikeBarcode(barcode)) return;

            CancelPendingBookSearch();
            ResetBookRecommendationPaging();
            try
            {
                var copy = _copyService.GetByBarcode(barcode);
                if (copy == null)
                {
                    BookSearchError = $"Không tìm thấy barcode {barcode}.";
                    return;
                }

                var reason = BookCopyService.GetBorrowBlockReason(copy);
                if (reason != null)
                {
                    BookSearchError = $"Bản sách {barcode} hiện không thể mượn: {reason}";
                    return;
                }

                var book = _bookService.GetBookById(copy.BookId);
                if (book == null || book.Status == BookStatuses.Archived)
                {
                    BookSearchError = "Đầu sách không tồn tại hoặc đã được lưu trữ.";
                    return;
                }

                _selectedBook = book;
                OnPropertyChanged(nameof(SelectedBook));
                OnPropertyChanged(nameof(HasSelectedBook));
                OnPropertyChanged(nameof(HasNoSelectedBook));
                AvailableCopies.Clear();
                AvailableCopies.Add(copy);
                SelectedCopy = copy;
                _bookSearchQuery = book.Title;
                OnPropertyChanged(nameof(BookSearchQuery));
                BookRecommendationHeader = $"Đã chọn {book.Title} · {copy.Barcode}";
                if (!TryAddCopy(book, copy, out string addError))
                    BookSearchError = addError;
            }
            catch (BusinessRuleException exception) { BookSearchError = exception.Message; }
            catch (Exception) { BookSearchError = "Không thể tra barcode. Vui lòng thử lại."; }
        }

        public BorrowViewModel(IUserDialogService dialogService)
            : this(dialogService, new BorrowService(), new BookService(), new BookCopyService(),
                new ReaderService(), new LoanPolicyService())
        {
        }

        internal BorrowViewModel(IUserDialogService dialogService, BorrowService borrowService,
            BookService bookService, BookCopyService copyService, ReaderService readerService,
            LoanPolicyService loanPolicyService)
            : this(dialogService, borrowService, bookService, copyService, readerService, loanPolicyService, borrowBooks: null)
        {
        }

        internal BorrowViewModel(IUserDialogService dialogService, BorrowService borrowService,
            BookService bookService, BookCopyService copyService, ReaderService readerService,
            LoanPolicyService loanPolicyService,
            Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? borrowBooks)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _borrowService = borrowService ?? throw new ArgumentNullException(nameof(borrowService));
            _bookService = bookService ?? throw new ArgumentNullException(nameof(bookService));
            _copyService = copyService ?? throw new ArgumentNullException(nameof(copyService));
            _readerService = readerService ?? throw new ArgumentNullException(nameof(readerService));
            _loanPolicyService = loanPolicyService ?? throw new ArgumentNullException(nameof(loanPolicyService));
            _borrowBooks = borrowBooks ?? _borrowService.BorrowBooks;
            SelectedBorrowCopies.CollectionChanged += (_, _) => RefreshBorrowSelectionState();
            BorrowCommand = new RelayCommand(DoBorrow, () => CanConfirmBorrow);
            AddSelectedCopyCommand = new RelayCommand(AddSelectedCopy, () => CanAddAnotherCopy);
            RemoveSelectedCopyCommand = new RelayCommand(parameter => RemoveSelectedCopy(parameter as SelectedBorrowCopy));
            SelectReaderCommand = new RelayCommand(param => SelectReader(param as Reader));
            ClearReaderSelectionCommand = new RelayCommand(_ => ClearReaderSelection());
            SelectBookCommand = new RelayCommand(param => SelectBook(param as Book));
            ClearBookSelectionCommand = new RelayCommand(_ => ClearBookSelection());
            SearchBookInputCommand = new RelayCommand(HandleBookSearchEnter);
            PreviousBorrowingsPageCommand = new RelayCommand(PreviousBorrowingsPage, () => CanGoPreviousBorrowings);
            NextBorrowingsPageCommand = new RelayCommand(NextBorrowingsPage, () => CanGoNextBorrowings);

            long readerGeneration = ++_readerRecommendationGeneration;
            long bookGeneration = ++_bookRecommendationGeneration;
            InitialRecommendationsLoaded = Task.WhenAll(
                LoadReaderRecommendationsAsync(string.Empty, pageNumber: 1, generation: readerGeneration, append: false),
                LoadBookRecommendationsAsync(string.Empty, pageNumber: 1, generation: bookGeneration, append: false));
            LoadCurrentBorrowings();
        }

        public void SelectReader(Reader? reader)
        {
            if (reader == null)
            {
                return;
            }

            CancelPendingReaderSearch();
            ResetReaderRecommendationPaging();
            SelectedReader = reader;
            _readerSearchQuery = reader.FullName;
            OnPropertyChanged(nameof(ReaderSearchQuery));
        }

        public void ClearReaderSelection()
        {
            CancelPendingReaderSearch();
            SelectedReader = null;
            _readerSearchQuery = string.Empty;
            OnPropertyChanged(nameof(ReaderSearchQuery));
            ScheduleReaderSearch();
        }

        public void ClearSelectedBorrowCopies() => SelectedBorrowCopies.Clear();

        private void RefreshBorrowSelectionState()
        {
            OnPropertyChanged(nameof(HasSelectedBorrowCopies));
            OnPropertyChanged(nameof(SelectedBorrowCount));
            OnPropertyChanged(nameof(CurrentActiveLoans));
            OnPropertyChanged(nameof(RemainingBorrowSlots));
            OnPropertyChanged(nameof(RemainingSelectionSlots));
            OnPropertyChanged(nameof(ReaderLoanCapacityText));
            OnPropertyChanged(nameof(ConfirmBorrowText));
            OnPropertyChanged(nameof(CanAddAnotherCopy));
            OnPropertyChanged(nameof(CanConfirmBorrow));
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddSelectedCopy()
        {
            if (SelectedBook == null || SelectedCopy == null)
            {
                BookSearchError = "Hãy chọn đầu sách và barcode cần thêm vào danh sách.";
                return;
            }

            if (TryAddCopy(SelectedBook, SelectedCopy, out string error))
                BookSearchError = string.Empty;
            else
                BookSearchError = error;
        }

        private bool TryAddCopy(Book book, BookCopy copy, out string error)
        {
            if (SelectedReader == null)
            {
                error = "Vui lòng chọn độc giả trước khi thêm bản sách.";
                return false;
            }
            if (_readerEligibility?.IsEligible != true)
            {
                error = _readerEligibility?.ReasonSummary ?? "Chưa thể xác nhận điều kiện mượn của độc giả.";
                return false;
            }
            if (SelectedBorrowCopies.Any(item => item.CopyId == copy.CopyId))
            {
                error = $"Barcode {copy.Barcode} đã có trong danh sách chọn.";
                return false;
            }
            if (SelectedBorrowCount >= RemainingBorrowSlots)
            {
                error = $"Độc giả chỉ còn có thể mượn thêm {RemainingSelectionSlots} sách trong lượt này.";
                return false;
            }
            if (book.Status != BookStatuses.Active)
            {
                error = "Đầu sách đã được lưu trữ, không thể mượn.";
                return false;
            }
            string? blockReason = BookCopyService.GetBorrowBlockReason(copy);
            if (blockReason != null)
            {
                error = $"Bản sách {copy.Barcode} hiện không thể mượn: {blockReason}";
                return false;
            }

            SelectedBorrowCopies.Add(new SelectedBorrowCopy(copy.CopyId, copy.BookId, book.Title, copy.Barcode));
            error = string.Empty;
            return true;
        }

        private void RemoveSelectedCopy(SelectedBorrowCopy? selectedCopy)
        {
            if (selectedCopy != null)
                SelectedBorrowCopies.Remove(selectedCopy);
        }

        public void SelectBook(Book? book)
        {
            if (book == null)
            {
                return;
            }

            BookSearchError = string.Empty;
            SelectedBook = book;
            if (AvailableCopies.Count == 0)
            {
                _dialogService.ShowWarning(
                    $"Đầu sách \"{book.Title}\" hiện không có bản nào ở trạng thái Available.",
                    "Sách đã hết");
                ClearBookSelection();
                return;
            }
        }

        public void ClearBookSelection()
        {
            CancelPendingBookSearch();
            SelectedBook = null;
            AvailableCopies.Clear();
            SelectedCopy = null;
            _bookSearchQuery = string.Empty;
            OnPropertyChanged(nameof(BookSearchQuery));
            BookSearchError = string.Empty;
            ScheduleBookSearch();
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

        private void ResetReaderRecommendationPaging()
        {
            _readerRecommendationGeneration++;
            ReaderRecommendationPage = 1;
            ReaderRecommendationHasMore = false;
            IsLoadingMoreReaders = false;
        }

        private void ResetBookRecommendationPaging()
        {
            _bookRecommendationGeneration++;
            BookRecommendationPage = 1;
            BookRecommendationHasMore = false;
            IsLoadingMoreBooks = false;
        }

        private bool IsCurrentReaderRequest(long generation, string query) =>
            generation == _readerRecommendationGeneration &&
            string.Equals(query, ReaderSearchQuery?.Trim() ?? string.Empty, StringComparison.Ordinal) &&
            SelectedReader == null;

        private bool IsCurrentBookRequest(long generation, string query) =>
            generation == _bookRecommendationGeneration &&
            string.Equals(query, BookSearchQuery?.Trim() ?? string.Empty, StringComparison.Ordinal) &&
            !BookCopyBarcode.LooksLikeBarcode(query);

        private async Task LoadReaderRecommendationsAsync(string query, int pageNumber, long generation, bool append)
        {
            if (append) IsLoadingMoreReaders = true;
            try
            {
                var page = await Task.Run(() => _readerService.SearchForBorrow(query, pageNumber, RecommendationPageSize));
                if (!IsCurrentReaderRequest(generation, query)) return;

                if (!append) RecommendedReaders.Clear();
                foreach (var reader in page.Items)
                {
                    if (!RecommendedReaders.Any(existing => existing.ReaderId == reader.ReaderId))
                        RecommendedReaders.Add(reader);
                }

                ReaderRecommendationPage = pageNumber;
                ReaderRecommendationHasMore = page.HasMore;
                ReaderRecommendationHeader = query.Length == 0
                    ? RecommendedReaders.Count > 0 ? "💡 Gợi ý độc giả:" : "⚠️ Chưa có độc giả đang hoạt động."
                    : RecommendedReaders.Count > 0
                        ? $"🔍 Gợi ý phù hợp ({RecommendedReaders.Count}):"
                        : $"⚠️ Không tìm thấy độc giả khớp với \"{query}\"";
                OnPropertyChanged(nameof(HasReaderResults));
            }
            catch (Exception ex)
            {
                if (!IsCurrentReaderRequest(generation, query)) return;
                ReaderRecommendationHeader = "⚠️ Không thể tìm kiếm độc giả.";
                ReportRecommendationError("Không thể tìm kiếm độc giả: " + ex.Message);
            }
            finally
            {
                if (append && generation == _readerRecommendationGeneration)
                    IsLoadingMoreReaders = false;
            }
        }

        private async Task LoadBookRecommendationsAsync(string query, int pageNumber, long generation, bool append)
        {
            if (append) IsLoadingMoreBooks = true;
            try
            {
                var page = await Task.Run(() => _bookService.SearchForBorrow(query, pageNumber, RecommendationPageSize));
                if (!IsCurrentBookRequest(generation, query)) return;

                if (!append) RecommendedBooks.Clear();
                foreach (var suggestion in page.Items)
                {
                    if (!RecommendedBooks.Any(existing => existing.BookId == suggestion.BookId))
                        RecommendedBooks.Add(MapBorrowSuggestionToDisplayBook(suggestion));
                }

                BookRecommendationPage = pageNumber;
                BookRecommendationHasMore = page.HasMore;
                BookRecommendationHeader = query.Length == 0
                    ? RecommendedBooks.Count > 0 ? "💡 Gợi ý sách có sẵn:" : "⚠️ Chưa có sách khả dụng."
                    : RecommendedBooks.Count > 0
                        ? $"🔍 Gợi ý phù hợp ({RecommendedBooks.Count}):"
                        : $"⚠️ Không tìm thấy sách khớp với \"{query}\"";
                OnPropertyChanged(nameof(HasBookResults));
            }
            catch (Exception ex)
            {
                if (!IsCurrentBookRequest(generation, query)) return;
                BookRecommendationHeader = "⚠️ Không thể tìm kiếm sách.";
                ReportRecommendationError("Không thể tìm kiếm sách: " + ex.Message);
            }
            finally
            {
                if (append && generation == _bookRecommendationGeneration)
                    IsLoadingMoreBooks = false;
            }
        }

        public Task LoadMoreReadersAsync()
        {
            if (!ReaderRecommendationHasMore || IsLoadingMoreReaders || SelectedReader != null)
                return Task.CompletedTask;

            string query = ReaderSearchQuery?.Trim() ?? string.Empty;
            return LoadReaderRecommendationsAsync(
                query, ReaderRecommendationPage + 1, _readerRecommendationGeneration, append: true);
        }

        public Task LoadMoreBooksAsync()
        {
            string query = BookSearchQuery?.Trim() ?? string.Empty;
            if (!BookRecommendationHasMore || IsLoadingMoreBooks || BookCopyBarcode.LooksLikeBarcode(query))
                return Task.CompletedTask;

            return LoadBookRecommendationsAsync(
                query, BookRecommendationPage + 1, _bookRecommendationGeneration, append: true);
        }

        private void ScheduleReaderSearch()
        {
            CancelPendingReaderSearch();
            ResetReaderRecommendationPaging();
            var cancellation = new CancellationTokenSource();
            _readerSearchDebounceCts = cancellation;
            _ = DebounceReaderSearchAsync(ReaderSearchQuery ?? string.Empty,
                _readerRecommendationGeneration, cancellation);
        }

        private void CancelPendingReaderSearch()
        {
            var pending = _readerSearchDebounceCts;
            _readerSearchDebounceCts = null;
            pending?.Cancel();
        }

        private async Task DebounceReaderSearchAsync(string query, long generation, CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellation.Token);
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_readerSearchDebounceCts, cancellation) ||
                    !IsCurrentReaderRequest(generation, query))
                    return;

                _readerSearchDebounceCts = null;
                await LoadReaderRecommendationsAsync(query.Trim(), pageNumber: 1, generation: generation, append: false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A newer query or reader selection superseded this search.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Debounced Borrow reader search failed: {0}", ex);
                ReportRecommendationError("Không thể tìm kiếm độc giả: " + ex.Message);
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void ScheduleBookSearch()
        {
            CancelPendingBookSearch();
            ResetBookRecommendationPaging();
            var cancellation = new CancellationTokenSource();
            _bookSearchDebounceCts = cancellation;
            _ = DebounceBookSearchAsync(BookSearchQuery ?? string.Empty,
                _bookRecommendationGeneration, cancellation);
        }

        private void CancelPendingBookSearch()
        {
            var pending = _bookSearchDebounceCts;
            _bookSearchDebounceCts = null;
            pending?.Cancel();
        }

        private async Task DebounceBookSearchAsync(string query, long generation, CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellation.Token);
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_bookSearchDebounceCts, cancellation) ||
                    !IsCurrentBookRequest(generation, query))
                    return;

                _bookSearchDebounceCts = null;
                await LoadBookRecommendationsAsync(query.Trim(), pageNumber: 1, generation: generation, append: false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A newer query, barcode lookup, or book selection superseded this search.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Debounced Borrow book search failed: {0}", ex);
                ReportRecommendationError("Không thể tìm kiếm sách: " + ex.Message);
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void ReportRecommendationError(string message)
        {
            try
            {
                _dialogService.ShowError(message, "Lỗi");
            }
            catch (Exception dialogException)
            {
                System.Diagnostics.Trace.TraceError("Reporting a Borrow recommendation failure failed: {0}", dialogException);
            }
        }

        private static Book MapBorrowSuggestionToDisplayBook(BorrowBookSuggestion suggestion) => new()
        {
            BookId = suggestion.BookId,
            Title = suggestion.Title,
            Author = suggestion.Author,
            Category = suggestion.Category,
            Isbn = suggestion.Isbn,
            AvailableQuantity = suggestion.AvailableCopyCount
        };

        private void LoadCurrentBorrowings(int? requestedPageNumber = null)
        {
            try
            {
                var page = _borrowService.GetCurrentBorrowingPage(
                    new CurrentBorrowingPageQuery(requestedPageNumber ?? CurrentBorrowingsPageNumber));

                CurrentBorrowingsPageNumber = page.PageNumber;
                CurrentBorrowingsTotalCount = page.TotalCount;
                CurrentBorrowingsTotalPages = page.TotalPages;
                OnPropertyChanged(nameof(CanGoPreviousBorrowings));
                OnPropertyChanged(nameof(CanGoNextBorrowings));
                OnPropertyChanged(nameof(ShowingBorrowingsText));
                OnPropertyChanged(nameof(CurrentBorrowingsPageText));
                CommandManager.InvalidateRequerySuggested();

                CurrentBorrowings.Clear();
                foreach (var row in page.Items)
                {
                    CurrentBorrowings.Add(new BorrowRow
                    {
                        ReaderName = row.ReaderName,
                        BookTitle = row.BookTitle,
                        BorrowDate = row.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = row.DueDate.ToString("dd/MM/yyyy")
                    });
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Không thể tải danh sách đang mượn: " + ex.Message, "Lỗi");
            }
        }

        private void PreviousBorrowingsPage() => ChangeBorrowingsPage(CurrentBorrowingsPageNumber - 1);

        private void NextBorrowingsPage() => ChangeBorrowingsPage(CurrentBorrowingsPageNumber + 1);

        private void ChangeBorrowingsPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > CurrentBorrowingsTotalPages) return;
            LoadCurrentBorrowings(pageNumber);
        }

        private void DoBorrow()
        {
            if (IsBusy) return;
            if (SelectedReader == null)
            {
                _dialogService.ShowInfo("Vui lòng chọn độc giả.", "Thiếu thông tin");
                return;
            }
            if (SelectedBorrowCount == 0)
            {
                _dialogService.ShowInfo("Vui lòng thêm ít nhất một bản sách vào danh sách chọn.", "Thiếu thông tin");
                return;
            }
            if (!CanConfirmBorrow)
            {
                _dialogService.ShowError(_readerEligibility?.ReasonSummary ??
                    "Độc giả hiện không đủ điều kiện hoặc số sách chọn vượt lượt còn lại.", "Không thể mượn sách");
                return;
            }

            IsBusy = true;
            try
            {
                int copyCount = SelectedBorrowCount;
                int readerId = SelectedReader.ReaderId;
                int[] copyIds = SelectedBorrowCopies.Select(item => item.CopyId).ToArray();
                _borrowBooks(readerId, copyIds);
                _dialogService.ShowInfo($"Mượn thành công {copyCount} sách.", "Thành công");

                LoadCurrentBorrowings(1);
                ClearSelectedBorrowCopies();
                ClearReaderSelection();
                ClearBookSelection();
                BookSearchError = string.Empty;
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
