using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Accounts
{
    public partial class MyAccountView : UserControl
    {
        public MyAccountView()
        {
            InitializeComponent();
            DataContextChanged += MyAccountView_DataContextChanged;
            if (DataContext is MyAccountViewModel vm)
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
        }

        private void MyAccountView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MyAccountViewModel vm)
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
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelProject == null || PanelProfile == null || PanelPassword == null) return;
            if (sender is not RadioButton rb || rb.Tag == null) return;

            string tag = rb.Tag.ToString() ?? "0";

            // Reset all tabs styling
            ResetTabStyle(TabBtnProject);
            ResetTabStyle(TabBtnProfile);
            ResetTabStyle(TabBtnPassword);

            // Set active style
            SetActiveTabStyle(rb);

            // Switch panels
            PanelProject.Visibility = tag == "0" ? Visibility.Visible : Visibility.Collapsed;
            PanelProfile.Visibility = tag == "1" ? Visibility.Visible : Visibility.Collapsed;
            PanelPassword.Visibility = tag == "2" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetActiveTabStyle(RadioButton btn)
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // #0284C7
            btn.Foreground = Brushes.White;
        }

        private void ResetTabStyle(RadioButton btn)
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0
            btn.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // #0F172A
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
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
                vm.CurrentPassword = PwdCurrent.Password;
                vm.NewPassword = PwdNew.Password;
                vm.ConfirmPassword = PwdConfirm.Password;

                if (vm.ChangePasswordCommand.CanExecute(null))
                {
                    vm.ChangePasswordCommand.Execute(null);
                }

                if (vm.IsPasswordSuccess)
                {
                    PwdCurrent.Clear();
                    PwdNew.Clear();
                    PwdConfirm.Clear();
                }
            }
        }

        private void UpdateProfileAlert(MyAccountViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.ProfileMessage))
            {
                ProfileAlertBox.Visibility = Visibility.Collapsed;
                return;
            }

            ProfileAlertBox.Visibility = Visibility.Visible;
            if (vm.IsProfileError)
            {
                ProfileAlertBox.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                ProfileAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                ProfileAlertBox.BorderThickness = new Thickness(1);
                TxtProfileAlert.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
            else
            {
                ProfileAlertBox.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                ProfileAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                ProfileAlertBox.BorderThickness = new Thickness(1);
                TxtProfileAlert.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
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
            if (vm.IsPasswordError)
            {
                PasswordAlertBox.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                PasswordAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                PasswordAlertBox.BorderThickness = new Thickness(1);
                TxtPasswordAlert.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
            else
            {
                PasswordAlertBox.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                PasswordAlertBox.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                PasswordAlertBox.BorderThickness = new Thickness(1);
                TxtPasswordAlert.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
        }
    }
}
