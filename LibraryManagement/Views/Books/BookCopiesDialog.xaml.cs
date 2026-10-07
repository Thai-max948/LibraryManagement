using System.Windows;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Books;

public partial class BookCopiesDialog : Window
{
    private readonly Book _book;
    private readonly BookCopyService _copyService = new();
    private bool _isBusy;

    public BookCopiesDialog(Book book)
    {
        InitializeComponent();
        _book = book;
        BookTitleText.Text = book.Title;
        ArchivedNoticeText.Visibility = book.Status == BookStatuses.Archived
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadCopies();
    }

    private void LoadCopies()
    {
        var copies = _copyService.GetCopies(_book.BookId);
        CopiesGrid.ItemsSource = copies;
        CopiesGrid.SelectedItem = null;

        var inventory = BookCopyInventory.FromCopies(copies);
        InventoryText.Text = $"Total: {inventory.TotalCopies}  •  Active: {inventory.ActiveCopies}  •  Available: {inventory.Available}  •  Borrowed: {inventory.Borrowed}  •  {BookCopyStatusDisplay.DamagedUnderRepair}: {inventory.DamagedUnderRepair}  •  Lost: {inventory.Lost}  •  Retired: {inventory.Retired}";
        UpdateManagementControls();
    }

    private void CopiesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateManagementControls();

    private void UpdateManagementControls()
    {
        bool canManage = _book.Status == BookStatuses.Active && !_isBusy;
        AddCopiesButton.IsEnabled = canManage;

        var selected = CopiesGrid.SelectedItem as BookCopy;
        MarkAvailableButton.IsEnabled = canManage && selected?.Status is
            BookCopyStatuses.Damaged or BookCopyStatuses.UnderRepair or BookCopyStatuses.Lost;
        RetireCopyButton.IsEnabled = canManage && selected != null
            && BookCopyService.GetManualTransitions(selected.Status).Contains(BookCopyStatuses.Retired, StringComparer.Ordinal);
    }

    private async void AddCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_book.Status != BookStatuses.Active) return;

        int quantity;
        try { quantity = BookCopyService.ParseQuantity(QuantityBox.Text); }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Không thể thêm bản sách");
            return;
        }

        _isBusy = true;
        UpdateManagementControls();
        try
        {
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
            _isBusy = false;
            UpdateManagementControls();
        }
    }

    private void MarkAvailable_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBookActive()) return;
        if (CopiesGrid.SelectedItem is not BookCopy selected)
        {
            MessageBox.Show("Select a copy first.", "Manage copies");
            return;
        }

        try
        {
            _copyService.MarkAvailable(selected.CopyId);
            LoadCopies();
        }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Cannot mark copy available");
        }
        catch (Exception exception)
        {
            MessageBox.Show("Could not mark copy available: " + exception.Message, "Error");
        }
    }

    private void RetireCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBookActive()) return;
        if (CopiesGrid.SelectedItem is not BookCopy selected)
        {
            MessageBox.Show("Select a copy first.", "Manage copies");
            return;
        }

        if (!BookCopyService.GetManualTransitions(selected.Status).Contains(BookCopyStatuses.Retired, StringComparer.Ordinal))
        {
            MessageBox.Show("Bản sách này không thể được retire thủ công.", "Không thể retire bản sách");
            LoadCopies();
            return;
        }

        if (MessageBox.Show(
                "Retire selected copy?\nA retired copy cannot be returned to circulation.",
                "Confirm retirement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _copyService.RetireCopy(selected.CopyId);
            LoadCopies();
        }
        catch (BusinessRuleException exception)
        {
            MessageBox.Show(exception.Message, "Không thể retire bản sách");
        }
        catch (Exception exception)
        {
            MessageBox.Show("Không thể retire bản sách: " + exception.Message, "Lỗi");
        }
    }

    private bool IsBookActive() => _book.Status == BookStatuses.Active;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
