using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Readers
{
    public partial class ReadersView : UserControl
    {
        public ReadersView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new ReadersViewModel();
            }
        }
    }
}
