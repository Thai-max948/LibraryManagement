using System.Windows.Controls;
using System.Windows.Input;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views;

public partial class FeesView : UserControl
{
    public FeesView() => InitializeComponent();

    private async void FeesView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is FeesViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FeesViewModel viewModel)
        {
            viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
