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
    public class ReturnViewModel : BaseViewModel
    {
        private readonly BorrowService _borrowService = new BorrowService();
        private readonly BookService _bookService = new BookService();
        private readonly ReaderService _readerService = new ReaderService();

        public ObservableCollection<ActiveBorrowRow> ActiveBorrowings { get; set; } = new ObservableCollection<ActiveBorrowRow>();

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

        private ActiveBorrowRow? _selectedRow;
        public ActiveBorrowRow? SelectedRow
        {
            get => _selectedRow;
            set => SetProperty(ref _selectedRow, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand ReturnCommand { get; }

        public ReturnViewModel()
        {
            ReturnCommand = new RelayCommand(DoReturn, () => SelectedRow != null);
            Load();
        }

        public void Load()
        {
            try
            {
                ActiveBorrowings.Clear();
                var records = _borrowService.GetBorrowingBooks();
                var books = _bookService.GetAllBooks();
                var readers = _readerService.GetAllReaders();

                var rows = records.Select(r =>
                {
                    var book = books.FirstOrDefault(b => b.BookId == r.BookId);
                    var reader = readers.FirstOrDefault(x => x.ReaderId == r.ReaderId);
                    return new ActiveBorrowRow
                    {
                        BorrowId = r.BorrowId,
                        ReaderName = reader?.FullName ?? "?",
                        BookTitle = book?.Title ?? "?",
                        BorrowDate = r.BorrowDate.ToString("dd/MM/yyyy"),
                        DueDate = r.DueDate.ToString("dd/MM/yyyy"),
                        BorrowDateValue = r.BorrowDate
                    };
                });

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var kw = SearchText.Trim();
                    rows = rows.Where(x =>
                        x.ReaderName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        x.BookTitle.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var row in rows.OrderByDescending(x => x.BorrowDateValue).ThenByDescending(x => x.BorrowId))
                {
                    ActiveBorrowings.Add(row);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách đang mượn: " + ex.Message, "Lỗi");
            }
        }

        private void DoReturn()
        {
            if (SelectedRow == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"Xác nhận trả sách \"{SelectedRow.BookTitle}\" của {SelectedRow.ReaderName}?",
                "Xác nhận trả sách", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            IsBusy = true;
            try
            {
                _borrowService.ReturnBook(SelectedRow.BorrowId, DateTime.Now);
                MessageBox.Show("Trả sách thành công.", "OK");
                SelectedRow = null;
                Load();
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể trả sách");
                Load();
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

    public class ActiveBorrowRow
    {
        public int BorrowId { get; set; }
        public string ReaderName { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string BorrowDate { get; set; } = string.Empty;
        public string DueDate { get; set; } = string.Empty;
        public DateTime BorrowDateValue { get; set; }
    }
}