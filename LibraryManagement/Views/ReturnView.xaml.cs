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
                Loaded += (_, _) => SearchInputBox.Focus();
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ReturnViewModel.IsBusy) && !viewModel.IsBusy)
                        SearchInputBox.Focus();
                };
            }
        }

        private void ActiveBorrowings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ActiveBorrowRow selectedRow)
                grid.ScrollIntoView(selectedRow);
        }
    }
}
