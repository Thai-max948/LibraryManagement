using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Auth
{
    public partial class RegisterView : UserControl
    {
        public event System.Action? RequestNavigateToSignIn;
        public event System.Action<string>? RequestRegister;

        public RegisterView()
        {
            InitializeComponent();
            DataContextChanged += RegisterView_DataContextChanged;

            LibraryManagement.Helpers.PasswordBoxHelper.SetupPasswordToggle(
                TxtRegPassword,
                TxtRegPasswordVisible,
                BtnToggleRegPassword,
                IconRegPasswordToggle,
                pwd => PlaceholderPassword.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed);
        }

        private void RegisterView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is AuthViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(AuthViewModel.StatusMessage))
                    {
                        UpdateStatusVisibility(vm);
                    }
                };
            }
        }

        private void UpdateStatusVisibility(AuthViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.StatusMessage))
            {
                StatusCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusCard.Visibility = Visibility.Visible;
                if (vm.IsStatusError)
                {
                    StatusCard.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                    StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
                else
                {
                    StatusCard.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                    StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                }
            }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string password = TxtRegPassword.Password;
            if (DataContext is AuthViewModel vm)
            {
                vm.ExecuteRegister(password);
            }
            else
            {
                RequestRegister?.Invoke(password);
            }
        }

        private void BtnGoToSignIn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AuthViewModel vm)
            {
                vm.TriggerSwipeToSignIn();
            }
            RequestNavigateToSignIn?.Invoke();
        }

        public string GetPassword() => TxtRegPassword.Password;
    }
}
