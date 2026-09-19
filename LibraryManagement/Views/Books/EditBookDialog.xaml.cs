using System.Windows;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Books
{
    public partial class EditBookDialog : Window
    {
        private readonly int _bookId;
        public Book ResultBook { get; private set; } = new();

        public EditBookDialog(Book existing)
        {
            InitializeComponent();
            _bookId = existing.BookId;
            TitleBox.Text = existing.Title;
            AuthorBox.Text = existing.Author;
            CategoryBox.Text = existing.Category;
            PublishYearBox.Text = existing.PublishYear.ToString();
            QuantityBox.Text = existing.Quantity.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text) || string.IsNullOrWhiteSpace(AuthorBox.Text))
            {
                MessageBox.Show("Title và Author không được để trống.", "Thiếu thông tin");
                return;
            }
            if (!int.TryParse(PublishYearBox.Text, out int year) || year <= 0)
            {
                MessageBox.Show("Publish Year không hợp lệ.", "Lỗi");
                return;
            }
            if (!int.TryParse(QuantityBox.Text, out int qty) || qty < 0)
            {
                MessageBox.Show("Quantity không hợp lệ.", "Lỗi");
                return;
            }

            ResultBook = new Book
            {
                BookId = _bookId,
                Title = TitleBox.Text.Trim(),
                Author = AuthorBox.Text.Trim(),
                Category = CategoryBox.Text.Trim(),
                PublishYear = year,
                Quantity = qty
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}