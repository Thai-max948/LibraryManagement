using System.Windows;
using System.Windows.Controls;
using LibraryManagement.ViewModels;
using LibraryManagement.Helpers;
using LibraryManagement.Views;

namespace LibraryManagement.Views.Accounts
{
    public partial class MyAccountView : UserControl
    {
        private Action _resetCurrentPassword = () => { };
        private Action _resetNewPassword = () => { };
        private Action _resetConfirmPassword = () => { };

        public MyAccountView()
            : this(null)
        {
        }

        public MyAccountView(MyAccountViewModel? viewModel)
        {
            InitializeComponent();
            DataContextChanged += MyAccountView_DataContextChanged;

            _resetCurrentPassword = PasswordBoxHelper.SetupPasswordToggle(
                PwdCurrent,
                PwdCurrentVisible,
                BtnTogglePwdCurrent,
                IconPwdCurrentToggle,
                pwd =>
                {
                    PlaceholderPwdCurrent.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            _resetNewPassword = PasswordBoxHelper.SetupPasswordToggle(
                PwdNew,
                PwdNewVisible,
                BtnTogglePwdNew,
                IconPwdNewToggle,
                pwd =>
                {
                    PlaceholderPwdNew.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            _resetConfirmPassword = PasswordBoxHelper.SetupPasswordToggle(
                PwdConfirm,
                PwdConfirmVisible,
                BtnTogglePwdConfirm,
                IconPwdConfirmToggle,
                pwd =>
                {
                    PlaceholderPwdConfirm.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed;
                    SyncPasswordsToVm();
                });

            if (viewModel != null)
            {
                DataContext = viewModel;
            }
            else if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new MyAccountViewModel(new MessageBoxDialogService());
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
                if (args.PropertyName == nameof(MyAccountViewModel.SelectedSection)
                    && vm.SelectedSection != MyAccountViewModel.AccountSection.Security)
                {
                    ClearPasswordFields();
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
                vm.SelectProfileCommand.Execute(null);
            }
        }

        private void ClearPasswordFields()
        {
            _resetCurrentPassword();
            _resetNewPassword();
            _resetConfirmPassword();
            PlaceholderPwdCurrent.Visibility = Visibility.Visible;
            PlaceholderPwdNew.Visibility = Visibility.Visible;
            PlaceholderPwdConfirm.Visibility = Visibility.Visible;
            if (DataContext is MyAccountViewModel vm)
            {
                vm.CurrentPassword = string.Empty;
                vm.NewPassword = string.Empty;
                vm.ConfirmPassword = string.Empty;
            }
        }
    }
}
