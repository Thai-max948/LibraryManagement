using LibraryManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LibraryManagement.ViewModels
{
    public class DashboardViewModel : BaseViewModel
    {
        private readonly BookService _bookService = new BookService();
        private readonly ReaderService _readerService = new ReaderService();
        private readonly BorrowService _borrowService = new BorrowService();

        private int _totalBooks;
        public int TotalBooks
        {
            get => _totalBooks;
            set => SetProperty(ref _totalBooks, value);
        }

        private int _totalReaders;
        public int TotalReaders
        {
            get => _totalReaders;
            set => SetProperty(ref _totalReaders, value);
        }

        private int _availableBooks;
        public int AvailableBooks
        {
            get => _availableBooks;
            set => SetProperty(ref _availableBooks, value);
        }

        private int _currentlyBorrowed;
        public int CurrentlyBorrowed
        {
            get => _currentlyBorrowed;
            set => SetProperty(ref _currentlyBorrowed, value);
        }

        private int _overdueBooks;
        public int OverdueBooks
        {
            get => _overdueBooks;
            set => SetProperty(ref _overdueBooks, value);
        }

        public List<RecentBorrowRow> RecentBorrowings { get; set; } = new List<RecentBorrowRow>();
        public List<RecentReturnRow> RecentReturnings { get; set; } = new List<RecentReturnRow>();

        public DashboardViewModel()
        {
            LoadData();
        }

        public void LoadData()
        {
            try
            {
                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders();
                var borrowing = _borrowService.GetBorrowingBooks();
                var allHistory = _borrowService.GetHistory(); // toàn bộ record, không phân biệt Status

                TotalBooks = books.Sum(b => b.Quantity);
                TotalReaders = readers.Count;
                AvailableBooks = books.Sum(b => b.AvailableQuantity);
                CurrentlyBorrowed = borrowing.Count;
                OverdueBooks = borrowing.Count(r => r.DueDate.Date < DateTime.Now.Date);

                // Recent Borrowings: lấy từ toàn bộ history, không mất khi đã trả
                RecentBorrowings = allHistory
                    .OrderByDescending(r => r.BorrowId)
                    .Take(5)
                    .Select(r =>
                    {
                        var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                        var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                        return new RecentBorrowRow
                        {
                            ReaderName = reader?.FullName ?? "?",
                            BookTitle = book?.Title ?? "?",
                            BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy")
                        };
                    })
                    .ToList();

                // Recent Returnings: chỉ lấy record đã trả, sort theo ReturnDate mới nhất
                RecentReturnings = allHistory
                    .Where(r => r.Status == "Returned" && r.ReturnDate.HasValue)
                    .OrderByDescending(r => r.ReturnDate)
                    .Take(5)
                    .Select(r =>
                    {
                        var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                        var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                        return new RecentReturnRow
                        {
                            ReaderName = reader?.FullName ?? "?",
                            BookTitle = book?.Title ?? "?",
                            ReturnDate = r.ReturnDate.Value.ToString("dd/MM/yyyy")
                        };
                    })
                    .ToList();

                OnPropertyChanged(nameof(RecentBorrowings));
                OnPropertyChanged(nameof(RecentReturnings));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải dữ liệu Dashboard: " + ex.Message, "Lỗi");
            }
        }
    }

    public class RecentBorrowRow
    {
        public string ReaderName { get; set; }
        public string BookTitle { get; set; }
        public string BorrowDate { get; set; }
    }

    public class RecentReturnRow
    {
        public string ReaderName { get; set; }
        public string BookTitle { get; set; }
        public string ReturnDate { get; set; }
    }
}