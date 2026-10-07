using System.Windows;
using System.Windows.Controls;
using System;
using System.Linq;
using LibraryManagement.Models;
using System.Globalization;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Books
{
    public partial class EditBookDialog : Window
    {
        private readonly int _bookId;
        private readonly BookPricingPolicy _pricingPolicy;
        public Book ResultBook { get; private set; } = new();

        public EditBookDialog(Book existing) : this(existing, BookPricingPolicy.Default)
        {
        }

        public EditBookDialog(Book existing, BookPricingPolicy pricingPolicy)
        {
            _pricingPolicy = pricingPolicy ?? throw new ArgumentNullException(nameof(pricingPolicy));
            InitializeComponent();
            ReplacementValueBox.TextChanged += BookPrice_TextChanged;
            PricingRateBadge.Text = FormatRateBadge();
            PricingPreviewText.Text = "Enter a valid Book Price to preview.";
            _bookId = existing.BookId;
            TitleBox.Text = existing.Title;
            IsbnBox.Text = existing.Isbn ?? string.Empty;
            AuthorBox.Text = existing.Author;
            CategoryBox.Text = existing.Category;
            PublisherBox.Text = existing.Publisher ?? string.Empty;
            var languageOptions = LanguageCatalog.GetBookOptions(existing.Language);
            LanguageBox.ItemsSource = languageOptions;
            var currentLanguage = languageOptions.FirstOrDefault(option =>
                string.Equals(option.Code, existing.Language?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (currentLanguage is not null)
                LanguageBox.SelectedValue = currentLanguage.Code;
            else
                LanguageBox.Text = existing.Language ?? string.Empty;
            PublishYearBox.Text = existing.PublishYear.ToString();
            QuantityBox.Text = existing.Quantity.ToString();
            ReplacementValueBox.Text = existing.ReplacementValue?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty;
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
                BookId = _bookId,
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
            {
                decimal rentalPrice = _pricingPolicy.CalculateRentalPrice(value);
                RentalPriceBox.Text = rentalPrice.ToString("0.00", CultureInfo.GetCultureInfo("en-US"));
                PricingPreviewText.Text = $"{value.ToString("C2", CultureInfo.GetCultureInfo("en-US"))} × {_pricingPolicy.RentalRate.ToString("P0", CultureInfo.InvariantCulture)} = {rentalPrice.ToString("C2", CultureInfo.GetCultureInfo("en-US"))}";
            }
            else
            {
                RentalPriceBox.Clear();
                PricingPreviewText.Text = "Enter a valid Book Price to preview.";
            }
        }

        private string FormatRateBadge()
            => $"AUTO · {_pricingPolicy.RentalRate.ToString("P0", CultureInfo.InvariantCulture)}";

        private static bool TryParseBookPrice(string text, out decimal value)
            => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                && value >= 0m && decimal.Round(value, 2) == value && value < 10000000000000000m;

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
