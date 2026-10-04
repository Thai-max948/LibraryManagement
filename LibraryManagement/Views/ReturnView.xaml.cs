using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class ReturnView : UserControl
    {
        public ReturnView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                var viewModel = new ReturnViewModel(new BorrowService(), new BookService(),
                    new ReaderService(), new MessageBoxDialogService());
                DataContext = viewModel;
                Loaded += (_, _) => BarcodeInput.Focus();
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ReturnViewModel.IsBusy) && !viewModel.IsBusy)
                        BarcodeInput.Focus();
                };
            }
        }
    }
}
