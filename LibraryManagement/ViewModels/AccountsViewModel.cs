using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Views.Accounts;

namespace LibraryManagement.ViewModels
{
    public class AccountsViewModel : BaseViewModel
    {
        private readonly UserService _userService = new();

        public ObservableCollection<User> Users { get; set; } = new();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    Search();
                }
            }
        }

        private User? _selectedUser;
        public User? SelectedUser
        {
            get => _selectedUser;
            set => SetProperty(ref _selectedUser, value);
        }

        public int TotalAccounts => Users.Count;
        public int TotalAdmins => Users.Count(u => u.Role == "Administrator");
        public int TotalLibrarians => Users.Count(u => u.Role == "Librarian");

        public ICommand CreateAccountCommand { get; }
        public ICommand EditAccountCommand { get; }
        public ICommand DeleteAccountCommand { get; }
        public ICommand RefreshCommand { get; }

        public AccountsViewModel()
        {
            CreateAccountCommand = new RelayCommand(_ => ShowCreateAccountDialog());
            EditAccountCommand = new RelayCommand(_ => ShowEditAccountDialog(), _ => SelectedUser != null);
            DeleteAccountCommand = new RelayCommand(_ => DeleteAccount(), _ => SelectedUser != null);
            RefreshCommand = new RelayCommand(_ => Load());

            Load();
        }

        public void Load()
        {
            try
            {
                Users.Clear();
                foreach (var u in _userService.GetAllUsers())
                {
                    Users.Add(u);
                }
                NotifyCounts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load user accounts: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Search()
        {
            try
            {
                Users.Clear();
                foreach (var u in _userService.SearchUsers(SearchText))
                {
                    Users.Add(u);
                }
                NotifyCounts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Search error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowCreateAccountDialog()
        {
            var dialog = new CreateAccountDialog();
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialog.Owner = Application.Current.MainWindow;
            }

            if (dialog.ShowDialog() == true && dialog.ResultUser != null)
            {
                try
                {
                    var result = _userService.CreateAccount(
                        dialog.ResultUser.FullName,
                        dialog.ResultUser.Username,
                        dialog.ResultUser.Email,
                        dialog.ResultPassword,
                        dialog.ResultUser.Role);

                    if (result.Success)
                    {
                        MessageBox.Show($"Account '{dialog.ResultUser.Username}' created successfully!\n\nEmail: {dialog.ResultUser.Email}\nRole: {dialog.ResultUser.Role}",
                            "Account Created", MessageBoxButton.OK, MessageBoxImage.Information);
                        Load();
                    }
                    else
                    {
                        MessageBox.Show(result.Message, "Creation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể tạo tài khoản");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        private void ShowEditAccountDialog()
        {
            if (SelectedUser == null)
            {
                return;
            }

            var dialog = new EditAccountDialog(SelectedUser);
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dialog.Owner = Application.Current.MainWindow;
            }

            if (dialog.ShowDialog() == true && dialog.ResultUser != null)
            {
                try
                {
                    var result = _userService.UpdateAccount(
                        dialog.ResultUser.Id,
                        dialog.ResultUser.FullName,
                        dialog.ResultUser.Username,
                        dialog.ResultUser.Email,
                        dialog.ResultUser.Role,
                        dialog.ResultPassword);

                    if (result.Success)
                    {
                        MessageBox.Show(result.Message, "Account Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                        Load();
                    }
                    else
                    {
                        MessageBox.Show(result.Message, "Update Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể cập nhật tài khoản");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        private void DeleteAccount()
        {
            if (SelectedUser == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to delete account '{SelectedUser.Username}' ({SelectedUser.FullName})?",
                "Confirm Account Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    if (_userService.DeleteUser(SelectedUser.Id))
                    {
                        Load();
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete account.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể xóa tài khoản");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(TotalAccounts));
            OnPropertyChanged(nameof(TotalAdmins));
            OnPropertyChanged(nameof(TotalLibrarians));
        }
    }
}
