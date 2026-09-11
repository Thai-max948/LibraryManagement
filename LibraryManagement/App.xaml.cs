using System;
using System.Windows;

namespace LibraryManagement
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Đã có lỗi không mong muốn xảy ra:\n" + e.Exception.Message, "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}