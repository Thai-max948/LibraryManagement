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
        private readonly BorrowService _borrowService = new BorrowService();
        private readonly BookService _bookService = new BookService();
        private readonly ReaderService _readerService = new ReaderService();

        public ObservableCollection<HistoryRow> Records { get; set; } = new ObservableCollection<HistoryRow>();
        public ObservableCollection<string> StatusOptions { get; set; } =
            new ObservableCollection<string> { "All", "Borrowing", "Returned" };

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

        public ICommand RefreshCommand { get; }

        public HistoryViewModel()
        {
            RefreshCommand = new RelayCommand(Load);
            Load();
        }

        private void Load()
        {
            try
            {
                string? statusFilter = SelectedStatus == "All" ? null : SelectedStatus;

                var records = _borrowService.GetHistory(
                    status: statusFilter,
                    fromDate: FromDate,
                    toDate: ToDate);

                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders();

                var rows = records.Select(r =>
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    DateTime actionDate = r.ReturnDate ?? r.BorrowDate;
                    return new HistoryRow
                    {
                        ReaderName = reader?.FullName ?? "?",
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        ReturnDate = r.DueDate.ToString("dd/MM/yyyy"),
                        Status = r.Status,
                        BorrowId = r.BorrowId,
                        ActionDate = actionDate
                    };
                });

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
        public string ReturnDate { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int BorrowId { get; set; }
        public DateTime ActionDate { get; set; }
    }
}