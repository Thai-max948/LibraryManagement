using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new DashboardViewModel();
            }
        }
    }
}
