using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Auth
{
    public partial class LoginView : UserControl
    {
        public event System.Action? RequestNavigateToRegister;
        public event System.Action<string>? RequestSignIn;

        public LoginView()
        {
            InitializeComponent();
            DataContextChanged += LoginView_DataContextChanged;
        }

        private void LoginView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PlaceholderPassword.Visibility = string.IsNullOrEmpty(TxtPassword.Password)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            string password = TxtPassword.Password;
            if (DataContext is AuthViewModel vm)
            {
                vm.ExecuteSignIn(password);
            }
            else
            {
                RequestSignIn?.Invoke(password);
            }
        }

        private void BtnGoToRegister_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AuthViewModel vm)
            {
                vm.TriggerSwipeToRegister();
            }
            RequestNavigateToRegister?.Invoke();
        }

        public string GetPassword() => TxtPassword.Password;
    }
}
