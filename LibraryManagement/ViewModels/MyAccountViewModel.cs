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
        public enum AccountSection
        {
            Profile,
            Security,
            AccessRole
        }

        private readonly UserService _userService;
        private readonly IUserDialogService _dialogService;
        private MyAccountProfile? _profile;
        private AccountAccessInfo? _access;
        private string _editFullName = string.Empty;
        private string _editUsername = string.Empty;
        private string _editEmail = string.Empty;
        private string _profileMessage = string.Empty;
        private string _passwordMessage = string.Empty;
        private string _currentPassword = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private bool _isProfileSuccess;
        private bool _isProfileError;
        private bool _isPasswordSuccess;
        private bool _isPasswordError;
        private bool _isEditProfileOpen;
        private AccountSection _selectedSection = AccountSection.Profile;

        public MyAccountViewModel(IUserDialogService dialogService, UserService? userService = null)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _userService = userService ?? new UserService();

            SelectProfileCommand = new RelayCommand(_ => SelectSection(AccountSection.Profile));
            SelectSecurityCommand = new RelayCommand(_ => SelectSection(AccountSection.Security));
            SelectAccessRoleCommand = new RelayCommand(_ => SelectSection(AccountSection.AccessRole));
            ToggleEditProfileCommand = new RelayCommand(_ => ToggleEditProfile());
            CloseSidePanelCommand = new RelayCommand(_ => CloseEditProfile());
            SaveProfileCommand = new RelayCommand(_ => ExecuteSaveProfile());
            ChangePasswordCommand = new RelayCommand(_ => ExecuteChangePassword());

            LoadUserData();
        }

        public string FullName => _profile?.FullName ?? "—";
        public string Username => _profile?.Username ?? "—";
        public string Email => _profile?.Email ?? "—";
        public string IdentityLine => _profile == null ? "—" : $"@{Username} · {Email}";
        public string Role => _access?.Role ?? _profile?.Role ?? "—";
        public string MemberSinceFormatted => _profile?.CreatedAt.ToString("dd/MM/yyyy") ?? "—";
        public string AccessLevel => _access?.AccessLevel ?? "—";

        public AccountSection SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (SetProperty(ref _selectedSection, value))
                {
                    OnPropertyChanged(nameof(IsProfileSelected));
                    OnPropertyChanged(nameof(IsSecuritySelected));
                    OnPropertyChanged(nameof(IsAccessRoleSelected));
                    OnPropertyChanged(nameof(ProfileSectionVisibility));
                    OnPropertyChanged(nameof(ProfileSummaryVisibility));
                    OnPropertyChanged(nameof(EditProfileVisibility));
                    OnPropertyChanged(nameof(SecuritySectionVisibility));
                    OnPropertyChanged(nameof(AccessRoleSectionVisibility));
                }
            }
        }

        public bool IsProfileSelected => SelectedSection == AccountSection.Profile;
        public bool IsSecuritySelected => SelectedSection == AccountSection.Security;
        public bool IsAccessRoleSelected => SelectedSection == AccountSection.AccessRole;
        public Visibility ProfileSectionVisibility => IsProfileSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProfileSummaryVisibility => IsProfileSelected && !IsEditProfileOpen ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EditProfileVisibility => IsProfileSelected && IsEditProfileOpen ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SecuritySectionVisibility => IsSecuritySelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AccessRoleSectionVisibility => IsAccessRoleSelected ? Visibility.Visible : Visibility.Collapsed;

        public string Initials
        {
            get
            {
                if (_profile == null || string.IsNullOrWhiteSpace(_profile.FullName))
                {
                    return "—";
                }

                var parts = _profile.FullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    return parts[0].Length >= 2
                        ? parts[0].Substring(0, 2).ToUpperInvariant()
                        : parts[0].ToUpperInvariant();
                }

                return $"{parts[0][0]}{parts[parts.Length - 1][0]}".ToUpperInvariant();
            }
        }

        public bool IsAdmin => UserPermissions.CanManageUsers(Role == "—" ? null : Role);
        public Visibility UserAccountsCheckVisibility => _access?.CanManageUsers == true ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UserAccountsLockVisibility => _access != null && !_access.CanManageUsers
            ? Visibility.Visible
            : Visibility.Collapsed;
        public Visibility CatalogCheckVisibility => _access?.CanManageCatalog == true ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReadersCheckVisibility => _access?.CanManageReaders == true ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CirculationCheckVisibility => _access?.CanUseCirculation == true ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FeesHistoryCheckVisibility => _access?.CanViewFeesAndHistory == true ? Visibility.Visible : Visibility.Collapsed;

        public string EditFullName
        {
            get => _editFullName;
            set => SetProperty(ref _editFullName, value);
        }

        public string EditUsername
        {
            get => _editUsername;
            set => SetProperty(ref _editUsername, value);
        }

        public string EditEmail
        {
            get => _editEmail;
            set => SetProperty(ref _editEmail, value);
        }

        public string ProfileMessage
        {
            get => _profileMessage;
            set
            {
                if (SetProperty(ref _profileMessage, value))
                {
                    OnPropertyChanged(nameof(HasProfileMessage));
                }
            }
        }

        public bool HasProfileMessage => !string.IsNullOrWhiteSpace(ProfileMessage);

        public bool IsProfileSuccess
        {
            get => _isProfileSuccess;
            set => SetProperty(ref _isProfileSuccess, value);
        }

        public bool IsProfileError
        {
            get => _isProfileError;
            set => SetProperty(ref _isProfileError, value);
        }

        public string CurrentPassword
        {
            get => _currentPassword;
            set => SetProperty(ref _currentPassword, value);
        }

        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        public string PasswordMessage
        {
            get => _passwordMessage;
            set
            {
                if (SetProperty(ref _passwordMessage, value))
                {
                    OnPropertyChanged(nameof(HasPasswordMessage));
                }
            }
        }

        public bool HasPasswordMessage => !string.IsNullOrWhiteSpace(PasswordMessage);

        public bool IsPasswordSuccess
        {
            get => _isPasswordSuccess;
            set => SetProperty(ref _isPasswordSuccess, value);
        }

        public bool IsPasswordError
        {
            get => _isPasswordError;
            set => SetProperty(ref _isPasswordError, value);
        }

        public bool IsEditProfileOpen
        {
            get => _isEditProfileOpen;
            private set
            {
                if (SetProperty(ref _isEditProfileOpen, value))
                {
                    OnPropertyChanged(nameof(ProfileSummaryVisibility));
                    OnPropertyChanged(nameof(EditProfileVisibility));
                }
            }
        }

        public ICommand SelectProfileCommand { get; }
        public ICommand SelectSecurityCommand { get; }
        public ICommand SelectAccessRoleCommand { get; }
        public ICommand ToggleEditProfileCommand { get; }
        public ICommand CloseSidePanelCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand ChangePasswordCommand { get; }

        public void LoadUserData()
        {
            try
            {
                var profile = _userService.GetMyProfile();
                var access = _userService.GetMyAccess();
                _profile = profile;
                _access = access;
                CopyProfileToEditFields();
                ProfileMessage = string.Empty;
                IsProfileError = false;
            }
            catch (BusinessRuleException ex)
            {
                _profile = null;
                _access = null;
                EditFullName = string.Empty;
                EditUsername = string.Empty;
                EditEmail = string.Empty;
                ProfileMessage = ex.Message;
                IsProfileError = true;
            }
            catch
            {
                _profile = null;
                _access = null;
                EditFullName = string.Empty;
                EditUsername = string.Empty;
                EditEmail = string.Empty;
                ProfileMessage = "Unable to load account information. Please try again.";
                IsProfileError = true;
            }

            RefreshAccountProperties();
        }

        private void SelectSection(AccountSection section)
        {
            SelectedSection = section;
            CloseEditProfile();
            ProfileMessage = string.Empty;
            PasswordMessage = string.Empty;
        }

        private void ToggleEditProfile()
        {
            if (IsEditProfileOpen)
            {
                CloseEditProfile();
            }
            else
            {
                SelectedSection = AccountSection.Profile;
                CopyProfileToEditFields();
                IsEditProfileOpen = true;
            }

            ProfileMessage = string.Empty;
            PasswordMessage = string.Empty;
        }

        private void CloseEditProfile()
        {
            CopyProfileToEditFields();
            IsEditProfileOpen = false;
        }

        private void CopyProfileToEditFields()
        {
            EditFullName = _profile?.FullName ?? string.Empty;
            EditUsername = _profile?.Username ?? string.Empty;
            EditEmail = _profile?.Email ?? string.Empty;
        }

        private void ExecuteSaveProfile()
        {
            try
            {
                var result = _userService.UpdateMyProfile(EditFullName, EditUsername, EditEmail);
                if (!result.Success || result.Profile == null)
                {
                    SetProfileFailure(result.Message);
                    return;
                }

                _profile = result.Profile;
                CopyProfileToEditFields();
                RefreshAccountProperties();
                ProfileMessage = result.Message;
                IsProfileSuccess = true;
                IsProfileError = false;
                IsEditProfileOpen = false;
                _dialogService.ShowInformation("Profile updated successfully", "Notification");
            }
            catch (BusinessRuleException ex)
            {
                SetProfileFailure(ex.Message);
            }
            catch
            {
                SetProfileFailure("Unable to update profile. Please try again.");
            }
        }

        private void SetProfileFailure(string message)
        {
            ProfileMessage = message;
            IsProfileSuccess = false;
            IsProfileError = true;
        }

        private void ExecuteChangePassword()
        {
            try
            {
                var result = _userService.ChangeMyPassword(CurrentPassword, NewPassword, ConfirmPassword);
                if (!result.Success)
                {
                    SetPasswordFailure(result.Message);
                    return;
                }

                PasswordMessage = result.Message;
                IsPasswordSuccess = true;
                IsPasswordError = false;
                ClearPasswordFields();
                _dialogService.ShowInformation("Password changed successfully", "Notification");
            }
            catch (BusinessRuleException ex)
            {
                SetPasswordFailure(ex.Message);
            }
            catch
            {
                SetPasswordFailure("Unable to change password. Please try again.");
            }
        }

        private void SetPasswordFailure(string message)
        {
            PasswordMessage = message;
            IsPasswordSuccess = false;
            IsPasswordError = true;
        }

        private void ClearPasswordFields()
        {
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
        }

        private void RefreshAccountProperties()
        {
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(IdentityLine));
            OnPropertyChanged(nameof(Role));
            OnPropertyChanged(nameof(MemberSinceFormatted));
            OnPropertyChanged(nameof(AccessLevel));
            OnPropertyChanged(nameof(Initials));
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(UserAccountsCheckVisibility));
            OnPropertyChanged(nameof(UserAccountsLockVisibility));
            OnPropertyChanged(nameof(CatalogCheckVisibility));
            OnPropertyChanged(nameof(ReadersCheckVisibility));
            OnPropertyChanged(nameof(CirculationCheckVisibility));
            OnPropertyChanged(nameof(FeesHistoryCheckVisibility));
        }
    }
}
