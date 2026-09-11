using LibraryManagement.Models;
using System.Windows;

namespace LibraryManagement.Views.Readers
{
    public partial class EditReaderDialog : Window
    {
        private readonly int _readerId;
        public Reader ResultReader { get; private set; }

        public EditReaderDialog(Reader existing)
        {
            InitializeComponent();
            _readerId = existing.ReaderId;
            FullNameBox.Text = existing.FullName;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                MessageBox.Show("Họ tên không được để trống.", "Thiếu thông tin");
                return;
            }

            ResultReader = new Reader
            {
                ReaderId = _readerId,
                FullName = FullNameBox.Text.Trim(),
                Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim()
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}