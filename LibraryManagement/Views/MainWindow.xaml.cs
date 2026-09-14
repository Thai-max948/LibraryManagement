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
