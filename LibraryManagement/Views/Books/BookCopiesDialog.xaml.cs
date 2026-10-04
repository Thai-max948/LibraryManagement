using System.Windows;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Books;

public partial class BookCopiesDialog : Window
{
    private readonly Book _book;
    private readonly BookCopyService _copyService = new();

    public BookCopiesDialog(Book book)
    {
        InitializeComponent();
        _book = book;
        BookTitleText.Text = book.Title;
        StatusBox.ItemsSource = new[]
        {
            BookCopyStatuses.Available, BookCopyStatuses.Lost, BookCopyStatusDisplay.DamagedUnderRepair,
            BookCopyStatuses.Retired
        };
        StatusBox.SelectedItem = BookCopyStatuses.Available;
        LoadCopies();
    }

    private void LoadCopies()
    {
        var copies = _copyService.GetCopies(_book.BookId);
        CopiesGrid.ItemsSource = copies;
        var inventory = BookCopyInventory.FromCopies(copies);
        InventoryText.Text = $"Active: {inventory.ActiveCopies}  •  Available: {inventory.Available}  •  Borrowed: {inventory.Borrowed}  •  {BookCopyStatusDisplay.DamagedUnderRepair}: {inventory.DamagedUnderRepair}  •  Lost: {inventory.Lost}  •  Retired: {inventory.Retired}";
    }

    private async void AddCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int quantity = BookCopyService.ParseQuantity(QuantityBox.Text);
            AddCopiesButton.IsEnabled = false;
            await Task.Run(() => _copyService.AddCopies(_book.BookId, quantity));
            QuantityBox.Clear();
            LoadCopies();
        }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Không thể thêm bản sách");
        }
        catch (Exception exception)
        {
            MessageBox.Show("Không thể thêm bản sách: " + exception.Message, "Lỗi");
        }
        finally
        {
            AddCopiesButton.IsEnabled = true;
        }
    }

    private void UpdateStatus_Click(object sender, RoutedEventArgs e)
    {
        if (CopiesGrid.SelectedItem is not BookCopy selected || StatusBox.SelectedItem is not string status)
        {
            MessageBox.Show("Chọn bản sách và trạng thái cần cập nhật.", "Quản lý bản sách");
            return;
        }
        try
        {
            _copyService.ChangeStatus(selected.CopyId, BookCopyStatusDisplay.GetStoredStatus(status));
            LoadCopies();
        }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Không thể cập nhật trạng thái");
        }
        catch (Exception exception)
        {
            MessageBox.Show("Không thể cập nhật trạng thái: " + exception.Message, "Lỗi");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
