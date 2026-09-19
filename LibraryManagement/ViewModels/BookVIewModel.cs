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
        private readonly BookService _bookService = new();

        public ObservableCollection<Book> Books { get; set; } = new();

        private string _searchText = string.Empty;
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
            set => SetProperty(ref _selectedBook, value);
        }

        public int TotalBooks => Books.Sum(b => b.Quantity);
        public int TotalAvailable => Books.Sum(b => b.AvailableQuantity);
        public int TotalBorrowed => Books.Sum(b => b.Quantity - b.AvailableQuantity);

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public BooksViewModel()
        {
            AddCommand = new RelayCommand(AddBook);
            EditCommand = new RelayCommand(EditBook, () => SelectedBook != null);
            DeleteCommand = new RelayCommand(DeleteBook, () => SelectedBook != null);
            Load();
        }

        public void Load()
        {
            try
            {
                Books.Clear();
                foreach (var b in _bookService.GetAllBooks())
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
                foreach (var b in _bookService.SearchBook(SearchText))
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
            var dialog = new AddBookDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _bookService.AddBook(dialog.ResultBook);
                    Load();
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

            var dialog = new EditBookDialog(SelectedBook);
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

        private void DeleteBook()
        {
            if (SelectedBook == null)
            {
                return;
            }

            var confirm = MessageBox.Show($"Xóa sách \"{SelectedBook.Title}\"?", "Xác nhận", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _bookService.DeleteBook(SelectedBook.BookId);
                Load();
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể xóa sách");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
            }
        }

        private void RaiseStats()
        {
            OnPropertyChanged(nameof(TotalBooks));
            OnPropertyChanged(nameof(TotalAvailable));
            OnPropertyChanged(nameof(TotalBorrowed));
        }
    }
}