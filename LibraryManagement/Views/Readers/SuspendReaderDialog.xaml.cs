using System.Windows;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class SuspendReaderDialog : Window
    {
        public string Reason { get; private set; } = string.Empty;

        public SuspendReaderDialog(Reader reader)
        {
            InitializeComponent();
            ReaderText.Text = $"{reader.FormattedId} - {reader.FullName}";
            ReasonBox.Focus();
        }

        private void Suspend_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ReasonBox.Text))
            {
                MessageBox.Show("Vui lòng nhập lý do tạm khóa độc giả.", "Thiếu thông tin");
                return;
            }

            Reason = ReasonBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
