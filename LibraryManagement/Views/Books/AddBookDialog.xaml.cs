using System.Windows;
using System.Windows.Controls;
using LibraryManagement.Models;
using System.Globalization;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Books
{
    public partial class AddBookDialog : Window
    {
        private readonly BookPricingPolicy _pricingPolicy;
        public Book ResultBook { get; private set; } = new();

        public AddBookDialog() : this(BookPricingPolicy.Default)
        {
        }

        public AddBookDialog(BookPricingPolicy pricingPolicy)
        {
            _pricingPolicy = pricingPolicy ?? throw new ArgumentNullException(nameof(pricingPolicy));
            InitializeComponent();
            ReplacementValueBox.TextChanged += BookPrice_TextChanged;
            LanguageBox.SelectedValue = string.Empty;
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
            if (!TryParseBookPrice(ReplacementValueBox.Text, out decimal replacementValue))
            {
                MessageBox.Show("Book Price phải là số không âm với tối đa 2 chữ số thập phân.", "Lỗi");
                return;
            }

            ResultBook = new Book
            {
                Title = TitleBox.Text.Trim(),
                Isbn = IsbnBox.Text,
                Author = AuthorBox.Text.Trim(),
                Category = CategoryBox.Text.Trim(),
                Publisher = string.IsNullOrWhiteSpace(PublisherBox.Text) ? null : PublisherBox.Text.Trim(),
                Language = LanguageCatalog.ResolveCode(LanguageBox.SelectedItem as LanguageOption, LanguageBox.Text),
                PublishYear = year,
                Quantity = qty,
                ReplacementValue = replacementValue,
                RentalPrice = _pricingPolicy.CalculateRentalPrice(replacementValue)
            };
            DialogResult = true;
        }

        private void BookPrice_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TryParseBookPrice(ReplacementValueBox.Text, out decimal value))
                RentalPriceBox.Text = _pricingPolicy.CalculateRentalPrice(value).ToString("0.00", CultureInfo.CurrentCulture);
            else
                RentalPriceBox.Clear();
        }

        private static bool TryParseBookPrice(string text, out decimal value)
            => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                && value >= 0m && decimal.Round(value, 2) == value && value < 10000000000000000m;

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
