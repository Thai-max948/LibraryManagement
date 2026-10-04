using System.Windows;
using LibraryManagement.Services;

namespace LibraryManagement.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                if (DataContext is LibraryManagement.ViewModels.MainViewModel vm)
                    await vm.Notifications.LoadAsync();
            };
            Closed += (_, _) =>
            {
                if (DataContext is LibraryManagement.ViewModels.MainViewModel vm)
                    vm.Notifications.Dispose();
            };
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            AuthService.CurrentUser = null;
            var authWindow = new AuthWindow();
            authWindow.Show();
            Close();
        }
    }
}
