using LibraryManagement.ViewModels;
using System.Windows.Controls;

namespace LibraryManagement.Views.Accounts
{
    public partial class AccountsView : UserControl
    {
        public AccountsView()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new AccountsViewModel();
            }
        }
    }
}
