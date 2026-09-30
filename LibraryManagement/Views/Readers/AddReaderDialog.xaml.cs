using System;
using System.Windows;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class AddReaderDialog : Window
    {
        public Reader ResultReader { get; private set; } = new();

        public AddReaderDialog()
        {
            InitializeComponent();
        }

        private void ReaderType_Changed(object sender, RoutedEventArgs e)
        {
            if (StudentIdPanel == null || IdentityNumberPanel == null)
            {
                return;
            }

            bool isStudent = RadioStudent.IsChecked == true;
            StudentIdPanel.Visibility = isStudent ? Visibility.Visible : Visibility.Collapsed;
            IdentityNumberPanel.Visibility = isStudent ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                MessageBox.Show("Họ tên không được để trống.", "Thiếu thông tin");
                return;
            }

            bool isStudent = RadioStudent.IsChecked == true;
            string readerType = isStudent ? "Student" : "External";
            string studentId = StudentIdBox.Text.Trim();
            string identityNumber = IdentityNumberBox.Text.Trim();

            if (isStudent && string.IsNullOrWhiteSpace(studentId))
            {
                MessageBox.Show("Mã sinh viên không được để trống.", "Thiếu thông tin");
                return;
            }

            if (!isStudent && string.IsNullOrWhiteSpace(identityNumber))
            {
                MessageBox.Show("Số CCCD / Định danh không được để trống.", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                MessageBox.Show("thiếu thông tin sđt", "Thiếu thông tin");
                return;
            }

            ResultReader = new Reader
            {
                FullName = FullNameBox.Text.Trim(),
                ReaderType = readerType,
                StudentId = isStudent ? studentId : null,
                IdentityNumber = isStudent ? null : identityNumber,
                Phone = PhoneBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Address = AddressBox.Text.Trim(),
                RegistrationDate = DateTime.Now,
                Status = "Active"
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
