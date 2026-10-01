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
        private readonly DateTime? _suspendedDate;
        public Reader ResultReader { get; private set; } = new();

        public EditReaderDialog(Reader existing)
        {
            InitializeComponent();
            _readerId = existing.ReaderId;
            _registrationDate = existing.RegistrationDate == default ? DateTime.Now : existing.RegistrationDate;
            _suspendedDate = existing.SuspendedDate;

            ReaderIdBox.Text = existing.FormattedId;
            FullNameBox.Text = existing.FullName;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            AddressBox.Text = existing.Address;

            bool isExternal = string.Equals(existing.ReaderType, "External", StringComparison.OrdinalIgnoreCase);
            ReaderTypeComboBox.SelectedIndex = isExternal ? 1 : 0;

            StudentIdBox.Text = existing.StudentId ?? string.Empty;
            IdentityNumberBox.Text = existing.IdentityNumber ?? string.Empty;

            UpdateIdentificationVisibility(isExternal);

            bool isSuspended = string.Equals(existing.Status, "Suspended", StringComparison.OrdinalIgnoreCase);
            bool isInactive = string.Equals(existing.Status, "Inactive", StringComparison.OrdinalIgnoreCase);
            StatusComboBox.SelectedIndex = isSuspended ? 1 : isInactive ? 2 : 0;
            SuspensionReasonBox.Text = existing.SuspensionReason;
            UpdateSuspensionVisibility(isSuspended);
        }

        private void Status_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SuspensionPanel == null) return;
            UpdateSuspensionVisibility(StatusComboBox.SelectedIndex == 1);
        }

        private void UpdateSuspensionVisibility(bool isSuspended)
        {
            SuspensionPanel.Visibility = isSuspended ? Visibility.Visible : Visibility.Collapsed;
            SuspendedDateText.Text = _suspendedDate.HasValue
                ? $"Đã khóa từ: {_suspendedDate:dd/MM/yyyy HH:mm}"
                : "Ngày khóa sẽ được ghi tự động khi lưu.";
        }

        private void ReaderType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentIdPanel == null || IdentityNumberPanel == null)
            {
                return;
            }

            bool isExternal = ReaderTypeComboBox.SelectedIndex == 1;
            UpdateIdentificationVisibility(isExternal);
        }

        private void UpdateIdentificationVisibility(bool isExternal)
        {
            StudentIdPanel.Visibility = isExternal ? Visibility.Collapsed : Visibility.Visible;
            IdentityNumberPanel.Visibility = isExternal ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullNameBox.Text))
            {
                MessageBox.Show("Họ tên không được để trống.", "Thiếu thông tin");
                return;
            }

            bool isExternal = ReaderTypeComboBox.SelectedIndex == 1;
            string readerType = isExternal ? "External" : "Student";
            string studentId = StudentIdBox.Text.Trim();
            string identityNumber = IdentityNumberBox.Text.Trim();

            if (!isExternal && string.IsNullOrWhiteSpace(studentId))
            {
                MessageBox.Show("Mã sinh viên không được để trống.", "Thiếu thông tin");
                return;
            }

            if (isExternal && string.IsNullOrWhiteSpace(identityNumber))
            {
                MessageBox.Show("Số CCCD / Định danh không được để trống.", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                MessageBox.Show("thiếu thông tin sđt", "Thiếu thông tin");
                return;
            }

            string status = StatusComboBox.SelectedIndex switch
            {
                1 => "Suspended",
                2 => "Inactive",
                _ => "Active"
            };
            if (status == "Suspended" && string.IsNullOrWhiteSpace(SuspensionReasonBox.Text))
            {
                MessageBox.Show("Vui lòng nhập lý do tạm khóa độc giả.", "Thiếu thông tin");
                return;
            }

            ResultReader = new Reader
            {
                ReaderId = _readerId,
                FullName = FullNameBox.Text.Trim(),
                ReaderType = readerType,
                StudentId = !isExternal ? studentId : null,
                IdentityNumber = isExternal ? identityNumber : null,
                Phone = PhoneBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Address = AddressBox.Text.Trim(),
                RegistrationDate = _registrationDate,
                Status = status,
                SuspensionReason = status == "Suspended" ? SuspensionReasonBox.Text.Trim() : string.Empty,
                SuspendedDate = status == "Suspended" ? _suspendedDate : null
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
