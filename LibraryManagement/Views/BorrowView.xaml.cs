using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class BorrowView : UserControl
    {
        public BorrowView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new BorrowViewModel();
            }
        }
    }
}
