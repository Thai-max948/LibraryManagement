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
    public class BorrowViewModel : BaseViewModel
    {
        private readonly BorrowService _borrowService = new BorrowService();
        private readonly BookService _bookService = new BookService();
        private readonly ReaderService _readerService = new ReaderService();

        public ObservableCollection<Reader> Readers { get; set; } = new ObservableCollection<Reader>();
        public ObservableCollection<Book> Books { get; set; } = new ObservableCollection<Book>();
        public ObservableCollection<BorrowRow> CurrentBorrowings { get; set; } = new ObservableCollection<BorrowRow>();

        private Reader? _selectedReader;
        public Reader? SelectedReader
        {
            get => _selectedReader;
            set => SetProperty(ref _selectedReader, value);
        }

        private Book? _selectedBook;
        public Book? SelectedBook
        {
            get => _selectedBook;
            set => SetProperty(ref _selectedBook, value);
        }

        private DateTime _borrowDate = DateTime.Now;
        public DateTime BorrowDate
        {
            get => _borrowDate;
            set => SetProperty(ref _borrowDate, value);
        }

        private DateTime _dueDate = DateTime.Now.AddDays(7);
        public DateTime DueDate
        {
            get => _dueDate;
            set => SetProperty(ref _dueDate, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand BorrowCommand { get; }

        public BorrowViewModel()
        {
            BorrowCommand = new RelayCommand(DoBorrow);
            LoadDropdowns();
            LoadCurrentBorrowings();
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
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách Reader/Book: " + ex.Message, "Lỗi");
            }
        }

        private void LoadCurrentBorrowings()
        {
            try
            {
                CurrentBorrowings.Clear();
                var records = _borrowService.GetBorrowingBooks();
                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders();

                foreach (var r in records.OrderByDescending(x => x.BorrowId))
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    CurrentBorrowings.Add(new BorrowRow
                    {
                        ReaderName = reader?.FullName ?? "?",
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = r.DueDate.ToString("dd/MM/yyyy")
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách đang mượn: " + ex.Message, "Lỗi");
            }
        }

        private void DoBorrow()
        {
            if (SelectedReader == null || SelectedBook == null)
            {
                MessageBox.Show("Vui lòng chọn Reader và Book.", "Thiếu thông tin");
                return;
            }

            IsBusy = true;
            try
            {
                _borrowService.BorrowBook(SelectedReader.ReaderId, SelectedBook.BookId, BorrowDate, DueDate);
                MessageBox.Show("Mượn sách thành công.", "OK");

                LoadDropdowns();
                LoadCurrentBorrowings();
                SelectedReader = null;
                SelectedBook = null;
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể mượn sách");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
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