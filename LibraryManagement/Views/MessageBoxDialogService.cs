using System.Windows;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views;

public sealed class MessageBoxDialogService : IUserDialogService
{
    public void ShowInfo(string message, string title) => MessageBox.Show(message, title);

    public void ShowInformation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title) => MessageBox.Show(message, title);

    public bool Confirm(string message, string title, bool warning = false)
    {
        var result = warning
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo);
        return result == MessageBoxResult.Yes;
    }
}
