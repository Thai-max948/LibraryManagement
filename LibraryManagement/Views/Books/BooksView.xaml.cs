using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Books
{
    public partial class BooksView : UserControl
    {
        public BooksView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new BooksViewModel();
            }
        }
    }
}
