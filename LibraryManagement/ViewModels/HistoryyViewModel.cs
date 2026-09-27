using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class HistoryViewModel : BaseViewModel
    {
        private readonly BorrowService _borrowService;
        private readonly BookService _bookService;
        private readonly ReaderService _readerService;

        public ObservableCollection<HistoryRow> Records { get; set; } = new ObservableCollection<HistoryRow>();
        public ObservableCollection<string> StatusOptions { get; set; } =
            new ObservableCollection<string> { "All", "Borrowing", "Returned", "Overdue" };

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                Load();
            }
        }

        private string _selectedStatus = "All";
        public string SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                SetProperty(ref _selectedStatus, value);
                Load();
            }
        }

        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set
            {
                SetProperty(ref _fromDate, value);
                Load();
            }
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set
            {
                SetProperty(ref _toDate, value);
                Load();
            }
        }

        private int _overdueCount;
        public int OverdueCount
        {
            get => _overdueCount;
            set => SetProperty(ref _overdueCount, value);
        }

        public bool HasOverdue => OverdueCount > 0;

        public ICommand RefreshCommand { get; }
        public ICommand FilterOverdueCommand { get; }

        public HistoryViewModel() : this(new BorrowService(), new BookService(), new ReaderService())
        {
        }

        public HistoryViewModel(BorrowService borrowService, BookService bookService, ReaderService readerService)
        {
            _borrowService = borrowService;
            _bookService = bookService;
            _readerService = readerService;
            RefreshCommand = new RelayCommand(Load);
            FilterOverdueCommand = new RelayCommand(() => SelectedStatus = "Overdue");
            Load();
        }

        private void Load()
        {
            try
            {
                var activeBorrowings = _borrowService.GetBorrowingBooks();
                OverdueCount = activeBorrowings.Count(r => r.DueDate.Date < DateTime.Today);
                OnPropertyChanged(nameof(HasOverdue));

                string? statusFilter = null;
                if (SelectedStatus == "Borrowing")
                {
                    statusFilter = "Borrowing";
                }
                else if (SelectedStatus == "Returned")
                {
                    statusFilter = "Returned";
                }
                else if (SelectedStatus == "Overdue")
                {
                    statusFilter = "Borrowing";
                }

                var records = _borrowService.GetHistory(
                    status: statusFilter,
                    fromDate: FromDate,
                    toDate: ToDate);

                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders(includeDeleted: true);

                var rows = records.Select(r =>
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    DateTime actionDate = r.ReturnDate ?? r.BorrowDate;

                    bool isOverdue = r.Status == "Borrowing" && r.DueDate.Date < DateTime.Today;
                    int overdueDays = isOverdue ? (DateTime.Today - r.DueDate.Date).Days : 0;
                    string status = isOverdue ? "Overdue" : r.Status;
                    string statusDisplay = isOverdue ? $"Overdue ({overdueDays}d)" : r.Status;
                    string overdueText = isOverdue
                        ? $"Quá hạn {overdueDays} ngày (Hạn trả: {r.DueDate:dd/MM/yyyy})"
                        : (r.Status == "Borrowing" ? $"Đang mượn (Hạn: {r.DueDate:dd/MM/yyyy})" : "Đã hoàn thành");

                    string readerName = reader != null
                        ? (reader.IsDeleted ? $"{reader.FullName} (Đã xóa)" : reader.FullName)
                        : "?";

                    return new HistoryRow
                    {
                        ReaderName = readerName,
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = r.DueDate.ToString("dd/MM/yyyy"),
                        ReturnDate = r.DueDate.ToString("dd/MM/yyyy"),
                        Status = status,
                        StatusDisplay = statusDisplay,
                        IsOverdue = isOverdue,
                        OverdueDays = overdueDays,
                        OverdueText = overdueText,
                        BorrowId = r.BorrowId,
                        ActionDate = actionDate
                    };
                });

                if (SelectedStatus == "Overdue")
                {
                    rows = rows.Where(x => x.IsOverdue);
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var kw = SearchText.Trim();
                    rows = rows.Where(x =>
                        x.ReaderName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        x.BookTitle.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }

                Records.Clear();
                foreach (var row in rows.OrderByDescending(x => x.ActionDate).ThenByDescending(x => x.BorrowId))
                {
                    Records.Add(row);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải lịch sử: " + ex.Message, "Lỗi");
            }
        }
    }

    public class HistoryRow
    {
        public string ReaderName { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string BorrowDate { get; set; } = string.Empty;
        public string DueDate { get; set; } = string.Empty;
        public string ReturnDate { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public bool IsOverdue { get; set; }
        public int OverdueDays { get; set; }
        public string OverdueText { get; set; } = string.Empty;
        public int BorrowId { get; set; }
        public DateTime ActionDate { get; set; }
    }
}