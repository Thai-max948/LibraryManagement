using System;
using System.Windows;
using LibraryManagement.Data;
using LibraryManagement.Services;

namespace LibraryManagement
{
    public partial class App : Application
    {
        private BookCopyMigrationResult? _startupBookCopyMigration;
        private bool _shownStartupCopyReviewNotice;

        public void SetStartupBookCopyMigration(BookCopyMigrationResult result)
        {
            _startupBookCopyMigration = result;
            _shownStartupCopyReviewNotice = false;
        }

        public BookCopyMigrationResult? GetStartupBookCopyMigrationNotice()
        {
            if (_startupBookCopyMigration is not { } result) return null;
            if (_shownStartupCopyReviewNotice)
                return result with { CopiesCreated = 0, CopiesNeedingReview = 0 };

            _shownStartupCopyReviewNotice = true;
            return result;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            NotificationRuntime.Initialize();
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Đã có lỗi không mong muốn xảy ra:\n" + e.Exception.Message, "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
