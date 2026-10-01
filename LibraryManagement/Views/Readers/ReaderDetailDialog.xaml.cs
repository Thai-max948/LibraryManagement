using System;
using System.Windows;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Readers
{
    public partial class ReaderDetailDialog : Window
    {
        private readonly ReaderService _readerService;
        private readonly int _readerId;
        private ReaderProfile _profile;

        public ReaderDetailDialog(ReaderProfile profile, ReaderService? readerService = null)
        {
            InitializeComponent();
            _profile = profile;
            _readerId = profile.Reader.ReaderId;
            _readerService = readerService ?? new ReaderService();
            ApplyProfile(profile);
        }

        private void ApplyProfile(ReaderProfile profile)
        {
            _profile = profile;
            DataContext = profile;
            ActiveActionsPanel.Visibility = profile.Reader.IsActive ? Visibility.Visible : Visibility.Collapsed;
            ReactivateButton.Visibility = profile.Reader.IsSuspended || profile.Reader.IsInactive
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshProfile() => ApplyProfile(_readerService.GetReaderProfile(_readerId));

        private void Suspend_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SuspendReaderDialog(_profile.Reader) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            ExecuteLifecycleAction(
                () => _readerService.SuspendReader(_readerId, dialog.Reason),
                "Đã tạm khóa độc giả.");
        }

        private void Deactivate_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Chuyển độc giả sang Inactive? Độc giả sẽ không thể mượn sách cho đến khi được kích hoạt lại.",
                "Xác nhận ngừng hoạt động", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            ExecuteLifecycleAction(() => _readerService.DeactivateReader(_readerId), "Đã chuyển độc giả sang Inactive.");
        }

        private void Reactivate_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Kích hoạt lại độc giả này?", "Xác nhận kích hoạt",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            ExecuteLifecycleAction(() => _readerService.ReactivateReader(_readerId), "Đã kích hoạt lại độc giả.");
        }

        private void ExecuteLifecycleAction(Action action, string successMessage)
        {
            try
            {
                action();
                RefreshProfile();
                MessageBox.Show(successMessage, "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể cập nhật trạng thái", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
