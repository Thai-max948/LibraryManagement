using System.Windows;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class ReaderDetailDialog : Window
    {
        public ReaderDetailDialog(ReaderProfile profile)
        {
            InitializeComponent();
            DataContext = profile;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
