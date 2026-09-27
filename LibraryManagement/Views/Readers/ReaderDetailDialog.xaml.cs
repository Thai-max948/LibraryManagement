using System;
using System.Windows;
using System.Windows.Media;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class ReaderDetailDialog : Window
    {
        public ReaderDetailDialog(Reader reader)
        {
            InitializeComponent();
            PopulateDetails(reader);
        }

        private void PopulateDetails(Reader reader)
        {
            ReaderIdText.Text = reader.FormattedId;
            ReaderTypeText.Text = reader.ReaderType;
            FullNameText.Text = reader.FullName;

            bool isExternal = string.Equals(reader.ReaderType, "External", StringComparison.OrdinalIgnoreCase);
            if (isExternal)
            {
                IdLabelText.Text = "CCCD/Định danh:";
                IdValueText.Text = string.IsNullOrWhiteSpace(reader.IdentityNumber) ? "-" : reader.IdentityNumber;
            }
            else
            {
                IdLabelText.Text = "Mã sinh viên (Student ID):";
                IdValueText.Text = string.IsNullOrWhiteSpace(reader.StudentId) ? "-" : reader.StudentId;
            }

            PhoneText.Text = string.IsNullOrWhiteSpace(reader.Phone) ? "-" : reader.Phone;
            EmailText.Text = string.IsNullOrWhiteSpace(reader.Email) ? "-" : reader.Email;
            AddressText.Text = string.IsNullOrWhiteSpace(reader.Address) ? "-" : reader.Address;
            RegistrationDateText.Text = reader.RegistrationDate == default ? "-" : reader.RegistrationDate.ToString("dd/MM/yyyy");

            bool isSuspended = reader.IsSuspended;
            StatusText.Text = reader.Status;

            if (isSuspended)
            {
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)); // Red-100
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red-600
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(209, 250, 229)); // Green-100
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // Green-600
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
