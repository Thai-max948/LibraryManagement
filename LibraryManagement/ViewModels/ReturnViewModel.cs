using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class ReturnViewModel : BaseViewModel
    {
        private readonly IReturnCirculationService _borrowService;
        private readonly BookService _bookService;
        private readonly ReaderService _readerService;
        private readonly IUserDialogService _dialogService;
        private readonly BookCopyService _copyService;

        public ObservableCollection<ActiveBorrowRow> ActiveBorrowings { get; set; } = new ObservableCollection<ActiveBorrowRow>();
        public ObservableCollection<BookCopy> LegacyCopyChoices { get; } = new ObservableCollection<BookCopy>();

        private BookCopy? _selectedLegacyCopy;
        public BookCopy? SelectedLegacyCopy
        {
            get => _selectedLegacyCopy;
            set
            {
                if (SetProperty(ref _selectedLegacyCopy, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    SearchErrorMessage = string.Empty;
                    Load();
                }
            }
        }

        private ActiveBorrowRow? _selectedRow;
        public ActiveBorrowRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetProperty(ref _selectedRow, value)) return;
                ConditionNote = string.Empty;
                OnPropertyChanged(nameof(NeedsLegacyMapping));
                LoadLegacyCopyChoices();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool NeedsLegacyMapping => SelectedRow?.NeedsMapping == true;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _searchErrorMessage = string.Empty;
        public string SearchErrorMessage
        {
            get => _searchErrorMessage;
            private set
            {
                if (SetProperty(ref _searchErrorMessage, value))
                    OnPropertyChanged(nameof(HasSearchError));
            }
        }

        public bool HasSearchError => !string.IsNullOrWhiteSpace(SearchErrorMessage);

        private string _resultMessage = string.Empty;
        public string ResultMessage { get => _resultMessage; set => SetProperty(ref _resultMessage, value); }
        public ICommand SearchEnterCommand { get; }
        public ICommand NeedsRepairCommand { get; }

        public void HandleSearchEnter()
        {
            SearchErrorMessage = string.Empty;
            if (!BookCopyBarcode.LooksLikeBarcode(SearchText)) return;

            string barcode = SearchText;
            SelectedRow = null;
            ConditionNote = string.Empty;
            ResultMessage = string.Empty;
            try
            {
                var loan = _borrowService.FindActiveReturnByBarcode(barcode);
                var book = _bookService.GetBookById(loan.BookId);
                var reader = _readerService.GetReaderById(loan.ReaderId);
                var row = new ActiveBorrowRow
                {
                    BorrowId = loan.BorrowId, BookId = loan.BookId, BookCopyId = loan.BookCopyId,
                    CopyBarcode = barcode, IsBarcodeScan = true, BookTitle = book?.Title ?? "?",
                    ReaderName = reader?.FullName ?? "?",
                    BorrowDate = loan.BorrowDate.ToString("dd/MM/yyyy"),
                    DueDate = loan.DueDate.ToString("dd/MM/yyyy"), BorrowDateValue = loan.BorrowDate,
                    OverdueDays = _borrowService.GetCurrentLateDays(loan.DueDate)
                };
                ActiveBorrowings.Clear();
                ActiveBorrowings.Add(row);
                SelectedRow = row;
            }
            catch (BusinessRuleException ex)
            {
                ActiveBorrowings.Clear();
                SearchErrorMessage = ex.Message.Contains("Barcode không tồn tại", StringComparison.OrdinalIgnoreCase)
                    ? $"Không tìm thấy barcode {barcode}."
                    : ex.Message.Contains("phiếu mượn", StringComparison.OrdinalIgnoreCase) ||
                      ex.Message.Contains("đang được mượn", StringComparison.OrdinalIgnoreCase)
                        ? $"Không có phiếu mượn đang hoạt động cho barcode {barcode}."
                        : ex.Message;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Return barcode lookup failed: {0}", ex);
                ActiveBorrowings.Clear();
                SearchErrorMessage = "Không thể tìm phiếu mượn bằng barcode lúc này. Vui lòng thử lại.";
            }
        }

        public ICommand ReturnCommand { get; }
        public ICommand DamagedReturnCommand { get; }
        public ICommand MarkLostCommand { get; }
        public ICommand LinkLegacyCopyCommand { get; }

        private string _conditionNote = string.Empty;
        public string ConditionNote
        {
            get => _conditionNote;
            set => SetProperty(ref _conditionNote, value);
        }

        public ReturnViewModel(IReturnCirculationService borrowService, BookService bookService,
            ReaderService readerService, IUserDialogService dialogService, BookCopyService? copyService = null)
        {
            _borrowService = borrowService ?? throw new ArgumentNullException(nameof(borrowService));
            _bookService = bookService ?? throw new ArgumentNullException(nameof(bookService));
            _readerService = readerService ?? throw new ArgumentNullException(nameof(readerService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _copyService = copyService ?? new BookCopyService();
            ReturnCommand = new RelayCommand(() => DoReturn(ReturnCondition.Normal), () => SelectedRow != null && !NeedsLegacyMapping && !IsBusy);
            DamagedReturnCommand = new RelayCommand(() => DoReturn(ReturnCondition.Damaged), () => SelectedRow != null && !NeedsLegacyMapping && !IsBusy);
            MarkLostCommand = new RelayCommand(DoMarkLost, () => SelectedRow != null && !NeedsLegacyMapping && !IsBusy);
            LinkLegacyCopyCommand = new RelayCommand(DoLinkLegacyCopy, () => NeedsLegacyMapping && SelectedLegacyCopy != null);
            SearchEnterCommand = new RelayCommand(HandleSearchEnter, () => !IsBusy);
            NeedsRepairCommand = new RelayCommand(() => DoReturn(ReturnCondition.NeedsRepair), () => SelectedRow != null && !NeedsLegacyMapping && !IsBusy);
            Load();
        }

        private void LoadLegacyCopyChoices()
        {
            LegacyCopyChoices.Clear();
            SelectedLegacyCopy = null;
            if (!NeedsLegacyMapping || SelectedRow == null) return;
            try
            {
                foreach (var copy in _copyService.GetCopies(SelectedRow.BookId).Where(copy =>
                    copy.Status == BookCopyStatuses.Available ||
                    (copy.Status == BookCopyStatuses.UnderRepair && copy.Condition == "LegacyUnverified")))
                    LegacyCopyChoices.Add(copy);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Loading copies for legacy return verification failed: {0}", ex);
                _dialogService.ShowError("Không thể tải barcode để đối chiếu lúc này.", "Lỗi");
            }
        }

        public void Load()
        {
            try
            {
                SelectedRow = null;
                ActiveBorrowings.Clear();
                var records = _borrowService.GetBorrowingBooks();
                var books = _bookService.GetAllBooksIncludingArchived();
                var readers = _readerService.GetAllReaders(includeDeleted: true);
                var barcodes = new Dictionary<int, string>();
                foreach (int bookId in records.Where(record => record.BookCopyId.HasValue).Select(record => record.BookId).Distinct())
                    foreach (var copy in _copyService.GetCopies(bookId))
                        barcodes[copy.CopyId] = copy.Barcode;

                var rows = records.Select(r =>
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    return new ActiveBorrowRow
                    {
                        BorrowId = r.BorrowId,
                        BookId = r.BookId,
                        BookCopyId = r.BookCopyId,
                        CopyBarcode = r.BookCopyId.HasValue && barcodes.TryGetValue(r.BookCopyId.Value, out var barcode)
                            ? barcode : "Cần đối chiếu",
                        ReaderName = reader != null
                            ? (reader.IsDeleted ? $"{reader.FullName} (Đã xóa)" : reader.FullName)
                            : "?",
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = r.DueDate.ToString("dd/MM/yyyy"),
                        BorrowDateValue = r.BorrowDate,
                        OverdueDays = _borrowService.GetCurrentLateDays(r.DueDate)
                    };
                });

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var kw = SearchText.Trim();
                    rows = rows.Where(x =>
                        x.ReaderName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        x.BookTitle.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        x.CopyBarcode.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var row in rows.OrderByDescending(x => x.BorrowDateValue).ThenByDescending(x => x.BorrowId))
                {
                    ActiveBorrowings.Add(row);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Loading active return loans failed: {0}", ex);
                _dialogService.ShowError("Không thể tải danh sách đang mượn lúc này.", "Lỗi");
            }
        }

        private void DoLinkLegacyCopy()
        {
            if (SelectedRow == null || SelectedLegacyCopy == null) return;
            int borrowId = SelectedRow.BorrowId;
            var barcode = SelectedLegacyCopy.Barcode;
            if (!_dialogService.Confirm(
                $"Đã đối chiếu bản vật lý {barcode} với phiếu mượn #{borrowId}?",
                "Xác nhận barcode")) return;

            IsBusy = true;
            try
            {
                _borrowService.LinkLegacyBorrowToCopy(borrowId, SelectedLegacyCopy.CopyId);
                Load();
                SelectedRow = ActiveBorrowings.FirstOrDefault(row => row.BorrowId == borrowId);
            }
            catch (BusinessRuleException ex)
            {
                _dialogService.ShowError(ex.Message, "Không thể gắn bản sách");
                Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Linking a legacy loan to a copy failed: {0}", ex);
                _dialogService.ShowError("Không thể gắn bản sách do lỗi hệ thống.", "Lỗi");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void DoReturn(ReturnCondition condition)
        {
            if (SelectedRow == null)
            {
                return;
            }

            if (NeedsLegacyMapping)
            {
                _dialogService.ShowInfo("Hãy đối chiếu và gắn barcode bản sách trước khi trả.", "Cần đối chiếu");
                return;
            }

            var confirm = _dialogService.Confirm(
                $"Xác nhận trả bản {SelectedRow.CopyBarcode} của sách \"{SelectedRow.BookTitle}\" từ {SelectedRow.ReaderName}? Tình trạng: {condition}.",
                "Xác nhận trả sách");
            if (!confirm)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var result = SelectedRow.IsBarcodeScan
                    ? _borrowService.ReturnBookByBarcode(SearchText, condition, ConditionNote)
                    : _borrowService.ReturnBook(SelectedRow.BorrowId, condition, ConditionNote);
                ShowResult(result);
                SelectedRow = null;
                ConditionNote = string.Empty;
                Load();
            }
            catch (BusinessRuleException ex)
            {
                _dialogService.ShowError(ex.Message, "Không thể trả sách");
                Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Returning a book copy failed: {0}", ex);
                _dialogService.ShowError("Không thể ghi nhận trả sách do lỗi hệ thống. Vui lòng thử lại.", "Lỗi");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowResult(ReturnResult result)
        {
            ResultMessage = $"Đã xử lý phiếu #{result.BorrowId}: {result.Disposition}."
                + (result.LateDays.HasValue ? $" Quá hạn: {result.LateDays.Value} ngày." : string.Empty)
                + (result.FeesCreated > 0 ? $" Đã ghi nhận {result.FeesCreated} khoản phí." : string.Empty)
                + (result.FeeWarnings.Count > 0 ? "\n" + string.Join("\n", result.FeeWarnings) : string.Empty);
            SearchErrorMessage = string.Empty;
            _dialogService.ShowInfo(ResultMessage, result.FeeWarnings.Count > 0
                ? "Trả sách hoàn tất; một số phí chưa được ghi"
                : "Kết quả trả sách");
        }

        private void DoMarkLost()
        {
            if (SelectedRow == null || NeedsLegacyMapping) return;
            int borrowId = SelectedRow.BorrowId;
            if (!_dialogService.Confirm(
                $"Xác nhận đánh dấu bản {SelectedRow.CopyBarcode} của sách \"{SelectedRow.BookTitle}\" là ĐÃ MẤT? Sách chưa được trả về thư viện.",
                "Xác nhận báo mất", warning: true)) return;
            IsBusy = true;
            try
            {
                var result = SelectedRow.IsBarcodeScan
                    ? _borrowService.ReturnBookByBarcode(SearchText, ReturnCondition.Lost, ConditionNote)
                    : _borrowService.MarkAsLost(borrowId, ConditionNote);
                ShowResult(result);
                ConditionNote = string.Empty;
                Load();
            }
            catch (BusinessRuleException ex)
            {
                _dialogService.ShowError(ex.Message, "Không thể báo mất");
                Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Marking a copy lost failed: {0}", ex);
                string detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                _dialogService.ShowError(
                    $"Không thể báo mất do lỗi hệ thống.\nChi tiết: {detail}", "Lỗi");
            }
            finally { IsBusy = false; }
        }
    }

    public class ActiveBorrowRow
    {
        public int BorrowId { get; set; }
        public int BookId { get; set; }
        public int? BookCopyId { get; set; }
        public bool NeedsMapping => !BookCopyId.HasValue;
        public string CopyBarcode { get; set; } = string.Empty;
        public bool IsBarcodeScan { get; set; }
        public int OverdueDays { get; set; }
        public bool IsOverdue => OverdueDays > 0;
        public string OverdueStatus => IsOverdue ? $"Overdue ({OverdueDays}d)" : "On time";
        public string ReaderName { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string BorrowDate { get; set; } = string.Empty;
        public string DueDate { get; set; } = string.Empty;
        public DateTime BorrowDateValue { get; set; }
    }
}
