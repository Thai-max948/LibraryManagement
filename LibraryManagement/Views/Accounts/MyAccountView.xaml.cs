using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryManagement.ViewModels;
using LibraryManagement.Helpers;

namespace LibraryManagement.Views.Accounts
{
    public partial class MyAccountView : UserControl
    {
        public MyAccountView()
        {
            InitializeComponent();
            DataContextChanged += MyAccountView_DataContextChanged;

            PasswordBoxHelper.SetupPasswordToggle(
                PwdCurrent,
                PwdCurrentVisible,
                BtnTogglePwdCurrent,
                IconPwdCurrentToggle,
                pwd =>
                {
                    PlaceholderPwdCurrent.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            PasswordBoxHelper.SetupPasswordToggle(
                PwdNew,
                PwdNewVisible,
                BtnTogglePwdNew,
                IconPwdNewToggle,
                pwd =>
                {
                    PlaceholderPwdNew.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            PasswordBoxHelper.SetupPasswordToggle(
                PwdConfirm,
                PwdConfirmVisible,
                BtnTogglePwdConfirm,
                IconPwdConfirmToggle,
                pwd =>
                {
                    PlaceholderPwdConfirm.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new MyAccountViewModel();
            }
            else if (DataContext is MyAccountViewModel vm)
            {
                HookViewModel(vm);
            }
        }

        private void MyAccountView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MyAccountViewModel vm)
            {
                HookViewModel(vm);
            }
        }

        private void HookViewModel(MyAccountViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MyAccountViewModel.ProfileMessage))
                {
                    UpdateProfileAlert(vm);
                }
                else if (args.PropertyName == nameof(MyAccountViewModel.PasswordMessage))
                {
                    UpdatePasswordAlert(vm);
                }
            };
        }

        private void SyncPasswordsToVm()
        {
            if (DataContext is MyAccountViewModel vm)
            {
                vm.CurrentPassword = PwdCurrent.Password;
                vm.NewPassword = PwdNew.Password;
                vm.ConfirmPassword = PwdConfirm.Password;
            }
        }

        private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MyAccountViewModel vm)
            {
                SyncPasswordsToVm();

                if (vm.ChangePasswordCommand.CanExecute(null))
                {
                    vm.ChangePasswordCommand.Execute(null);
                }

                if (vm.IsPasswordSuccess)
                {
                    ClearPasswordFields();
                }
            }
        }

        private void BtnCancelPassword_Click(object sender, RoutedEventArgs e)
        {
            ClearPasswordFields();
            if (DataContext is MyAccountViewModel vm)
            {
                vm.CloseSidePanelCommand.Execute(null);
            }
        }

        private void ClearPasswordFields()
        {
            PwdCurrent.Clear();
            PwdCurrentVisible.Clear();
            PwdNew.Clear();
            PwdNewVisible.Clear();
            PwdConfirm.Clear();
            PwdConfirmVisible.Clear();
            PlaceholderPwdCurrent.Visibility = Visibility.Visible;
            PlaceholderPwdNew.Visibility = Visibility.Visible;
            PlaceholderPwdConfirm.Visibility = Visibility.Visible;
        }

        private void UpdateProfileAlert(MyAccountViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.ProfileMessage))
            {
                ProfileAlertBox.Visibility = Visibility.Collapsed;
                return;
            }

            ProfileAlertBox.Visibility = Visibility.Visible;
            if (vm.IsProfileSuccess)
            {
                ProfileAlertBox.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
                ProfileAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
                TxtProfileAlert.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
            }
            else
            {
                ProfileAlertBox.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226));
                ProfileAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                TxtProfileAlert.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            }
        }

        private void UpdatePasswordAlert(MyAccountViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.PasswordMessage))
            {
                PasswordAlertBox.Visibility = Visibility.Collapsed;
                return;
            }

            PasswordAlertBox.Visibility = Visibility.Visible;
            if (vm.IsPasswordSuccess)
            {
                PasswordAlertBox.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
                PasswordAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
                TxtPasswordAlert.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
            }
            else
            {
                PasswordAlertBox.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226));
                PasswordAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                TxtPasswordAlert.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            }
        }
    }
}
