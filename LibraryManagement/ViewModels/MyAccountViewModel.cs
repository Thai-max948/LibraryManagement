using System;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class MyAccountViewModel : BaseViewModel
    {
        private readonly UserService _userService = new();

        public User? CurrentUser => AuthService.CurrentUser;

        public int UserId => CurrentUser?.Id ?? 0;
        public string FullName => CurrentUser?.FullName ?? "System User";
        public string Username => CurrentUser?.Username ?? "user";
        public string Email => CurrentUser?.Email ?? "user@library.com";
        public string Role => CurrentUser?.Role ?? "Librarian";
        public string CreatedAtFormatted => CurrentUser?.CreatedAt.ToString("yyyy-MM-dd HH:mm") ?? "N/A";

        public string ProjectName => "Library Management System (Library OS)";
        public string ProjectVersion => "v2.4 Core Node";
        public string DatabaseStatus => "Active & Connected";

        public string PermissionsSummary => Role == "Administrator"
            ? "Full Access: Catalog management, circulation desk, readers directory, system user accounts, and security privileges."
            : "Circulation Access: Book search & catalog maintenance, readers registry, book borrowing & returns, and circulation history.";

        // Edit Profile State
        private string _editFullName = "";
        public string EditFullName
        {
            get => _editFullName;
            set => SetProperty(ref _editFullName, value);
        }

        private string _editUsername = "";
        public string EditUsername
        {
            get => _editUsername;
            set => SetProperty(ref _editUsername, value);
        }

        private string _editEmail = "";
        public string EditEmail
        {
            get => _editEmail;
            set => SetProperty(ref _editEmail, value);
        }

        private string _profileMessage = "";
        public string ProfileMessage
        {
            get => _profileMessage;
            set => SetProperty(ref _profileMessage, value);
        }

        private bool _isProfileSuccess = false;
        public bool IsProfileSuccess
        {
            get => _isProfileSuccess;
            set => SetProperty(ref _isProfileSuccess, value);
        }

        private bool _isProfileError = false;
        public bool IsProfileError
        {
            get => _isProfileError;
            set => SetProperty(ref _isProfileError, value);
        }

        // Change Password State
        private string _currentPassword = "";
        public string CurrentPassword
        {
            get => _currentPassword;
            set => SetProperty(ref _currentPassword, value);
        }

        private string _newPassword = "";
        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        private string _confirmPassword = "";
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        private string _passwordMessage = "";
        public string PasswordMessage
        {
            get => _passwordMessage;
            set => SetProperty(ref _passwordMessage, value);
        }

        private bool _isPasswordSuccess = false;
        public bool IsPasswordSuccess
        {
            get => _isPasswordSuccess;
            set => SetProperty(ref _isPasswordSuccess, value);
        }

        private bool _isPasswordError = false;
        public bool IsPasswordError
        {
            get => _isPasswordError;
            set => SetProperty(ref _isPasswordError, value);
        }

        // Selected Tab (0 = My Project, 1 = Edit Profile, 2 = Change Password)
        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public ICommand SelectTabCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand ChangePasswordCommand { get; }
        public ICommand ResetProfileFieldsCommand { get; }

        public MyAccountViewModel()
        {
            SelectTabCommand = new RelayCommand(p =>
            {
                if (p != null && int.TryParse(p.ToString(), out int idx))
                {
                    SelectedTabIndex = idx;
                    ProfileMessage = "";
                    PasswordMessage = "";
                }
            });

            SaveProfileCommand = new RelayCommand(_ => ExecuteSaveProfile());
            ChangePasswordCommand = new RelayCommand(_ => ExecuteChangePassword());
            ResetProfileFieldsCommand = new RelayCommand(_ => LoadUserData());

            LoadUserData();
        }

        public void LoadUserData()
        {
            if (CurrentUser != null)
            {
                EditFullName = CurrentUser.FullName;
                EditUsername = CurrentUser.Username;
                EditEmail = CurrentUser.Email;
            }
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(Role));
            OnPropertyChanged(nameof(UserId));
            OnPropertyChanged(nameof(CreatedAtFormatted));
            OnPropertyChanged(nameof(PermissionsSummary));
        }

        private void ExecuteSaveProfile()
        {
            if (CurrentUser == null) return;

            var result = _userService.UpdateSelfProfile(
                CurrentUser.Id,
                EditFullName,
                EditUsername,
                EditEmail);

            if (result.Success)
            {
                ProfileMessage = result.Message;
                IsProfileSuccess = true;
                IsProfileError = false;
                LoadUserData();
                MessageBox.Show("changed succesfully", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ProfileMessage = result.Message;
                IsProfileSuccess = false;
                IsProfileError = true;
            }
        }

        private void ExecuteChangePassword()
        {
            if (CurrentUser == null) return;

            var result = _userService.ChangePassword(
                CurrentUser.Id,
                CurrentPassword,
                NewPassword,
                ConfirmPassword);

            if (result.Success)
            {
                PasswordMessage = result.Message;
                IsPasswordSuccess = true;
                IsPasswordError = false;
                CurrentPassword = "";
                NewPassword = "";
                ConfirmPassword = "";
                MessageBox.Show("changed succesfully", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                PasswordMessage = result.Message;
                IsPasswordSuccess = false;
                IsPasswordError = true;
            }
        }
    }
}
