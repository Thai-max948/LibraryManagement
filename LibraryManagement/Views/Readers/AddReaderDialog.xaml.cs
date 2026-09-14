using LibraryManagement.Models;
using System.Windows;

namespace LibraryManagement.Views.Readers
{
    public partial class AddReaderDialog : Window
    {
        public Reader ResultReader { get; private set; }

        public AddReaderDialog()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                MessageBox.Show("Họ tên không được để trống.", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                MessageBox.Show("thiếu thông tin sđt", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(EmailBox.Text))
            {
                MessageBox.Show("thiếu thông tin email", "Thiếu thông tin");
                return;
            }

            ResultReader = new Reader
            {
                FullName = FullNameBox.Text.Trim(),
                Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim()
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}