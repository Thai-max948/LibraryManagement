using System.Windows;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Books;

public partial class BookDetailDialog : Window
{
    private readonly BookDetailViewModel _viewModel;

    public BookDetailDialog(int bookId)
    {
        InitializeComponent();
        _viewModel = new BookDetailViewModel(bookId);
        DataContext = _viewModel;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Run(_viewModel.Refresh);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentBook is not Book book) return;
        var dialog = new EditBookDialog(book, _viewModel.PricingPolicy) { Owner = this };
        if (dialog.ShowDialog() == true) Run(() => _viewModel.UpdateBook(dialog.ResultBook));
    }

    private void ManageCopies_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentBook is not Book book || !_viewModel.CanManage) return;

        try
        {
            var dialog = new BookCopiesDialog(book) { Owner = this };
            dialog.ShowDialog();
            Run(_viewModel.Refresh);
        }
        catch (BusinessRuleException exception) { MessageBox.Show(exception.Message, "Book detail"); }
        catch (Exception) { MessageBox.Show("Không thể mở danh sách bản sách. Vui lòng thử lại.", "Lỗi"); }
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Lưu trữ đầu sách này? Lịch sử mượn sẽ được giữ lại.",
            "Xác nhận lưu trữ", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Run(_viewModel.Archive);
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => Run(_viewModel.Restore);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Run(Action action)
    {
        try { action(); }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Book detail");
            TryRefresh();
        }
        catch (Exception)
        {
            MessageBox.Show("Không thể cập nhật thông tin sách. Vui lòng thử lại.", "Lỗi");
            TryRefresh();
        }
    }

    private void TryRefresh()
    {
        try { _viewModel.Refresh(); }
        catch (Exception) { /* Keep the dialog open so the librarian can retry Refresh. */ }
    }
}
