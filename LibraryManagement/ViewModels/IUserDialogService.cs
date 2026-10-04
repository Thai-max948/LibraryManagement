namespace LibraryManagement.ViewModels;

public interface IUserDialogService
{
    void ShowInfo(string message, string title);
    void ShowInformation(string message, string title);
    void ShowWarning(string message, string title);
    void ShowError(string message, string title);
    bool Confirm(string message, string title, bool warning = false);
}
