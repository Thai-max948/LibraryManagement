using System;
using System.Windows;
using System.Windows.Controls;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class EditReaderDialog : Window
    {
        private readonly int _readerId;
        private readonly DateTime _registrationDate;
        private readonly string _status;
        private readonly string _suspensionReason;
        private readonly DateTime? _suspendedDate;
        public Reader ResultReader { get; private set; } = new();

        public EditReaderDialog(Reader existing)
        {
            InitializeComponent();
            _readerId = existing.ReaderId;
            _registrationDate = existing.RegistrationDate == default ? DateTime.Now : existing.RegistrationDate;
            _status = existing.Status;
            _suspensionReason = existing.SuspensionReason;
            _suspendedDate = existing.SuspendedDate;

            ReaderIdBox.Text = existing.FormattedId;
            FullNameBox.Text = existing.FullName;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            AddressBox.Text = existing.Address;
            MembershipExpiresOnPicker.SelectedDate = existing.MembershipExpiresOn;

            StudentIdBox.Text = existing.StudentId ?? string.Empty;
            IdentityNumberBox.Text = existing.IdentityNumber ?? string.Empty;
            LecturerCodeBox.Text = existing.LecturerCode ?? string.Empty;
            DepartmentBox.Text = existing.Department ?? string.Empty;
            ReaderTypeComboBox.SelectedIndex = existing.IsExternal ? 2 : existing.IsLecturer ? 1 : 0;
            UpdateIdentificationVisibility(existing.ReaderType);

        }

        private void ReaderType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentIdPanel == null || IdentityNumberPanel == null || LecturerInfoPanel == null)
            {
                return;
            }

            UpdateIdentificationVisibility(GetSelectedReaderType());
        }

        private string GetSelectedReaderType() =>
            (ReaderTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Student";

        private void UpdateIdentificationVisibility(string readerType)
        {
            bool isStudent = string.Equals(readerType, "Student", StringComparison.OrdinalIgnoreCase);
            bool isLecturer = string.Equals(readerType, "Lecturer", StringComparison.OrdinalIgnoreCase);
            StudentIdPanel.Visibility = isStudent ? Visibility.Visible : Visibility.Collapsed;
            IdentityNumberPanel.Visibility = !isStudent && !isLecturer ? Visibility.Visible : Visibility.Collapsed;
            LecturerInfoPanel.Visibility = isLecturer ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                MessageBox.Show("Họ tên không được để trống.", "Thiếu thông tin");
                return;
            }

            string readerType = GetSelectedReaderType();
            bool isStudent = string.Equals(readerType, "Student", StringComparison.OrdinalIgnoreCase);
            bool isLecturer = string.Equals(readerType, "Lecturer", StringComparison.OrdinalIgnoreCase);
            bool isExternal = string.Equals(readerType, "External", StringComparison.OrdinalIgnoreCase);
            string studentId = StudentIdBox.Text.Trim();
            string identityNumber = IdentityNumberBox.Text.Trim();
            string lecturerCode = LecturerCodeBox.Text.Trim();

            if (isStudent && string.IsNullOrWhiteSpace(studentId))
            {
                MessageBox.Show("Mã sinh viên không được để trống.", "Thiếu thông tin");
                return;
            }

            if (isExternal && string.IsNullOrWhiteSpace(identityNumber))
            {
                MessageBox.Show("Số CCCD / Định danh không được để trống.", "Thiếu thông tin");
                return;
            }

            if (isLecturer && string.IsNullOrWhiteSpace(lecturerCode))
            {
                MessageBox.Show("Mã giảng viên không được để trống.", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                MessageBox.Show("thiếu thông tin sđt", "Thiếu thông tin");
                return;
            }

            ResultReader = new Reader
            {
                ReaderId = _readerId,
                FullName = FullNameBox.Text.Trim(),
                ReaderType = readerType,
                StudentId = string.IsNullOrWhiteSpace(studentId) ? null : studentId,
                IdentityNumber = string.IsNullOrWhiteSpace(identityNumber) ? null : identityNumber,
                LecturerCode = string.IsNullOrWhiteSpace(lecturerCode) ? null : lecturerCode,
                Department = string.IsNullOrWhiteSpace(DepartmentBox.Text) ? null : DepartmentBox.Text.Trim(),
                Phone = PhoneBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Address = AddressBox.Text.Trim(),
                RegistrationDate = _registrationDate,
                MembershipExpiresOn = MembershipExpiresOnPicker.SelectedDate?.Date,
                Status = _status,
                SuspensionReason = _suspensionReason,
                SuspendedDate = _suspendedDate
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
