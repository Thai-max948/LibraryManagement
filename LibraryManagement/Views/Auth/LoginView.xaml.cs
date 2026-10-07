using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views.Auth
{
    public partial class LoginView : UserControl
    {
        private AuthViewModel? _observedViewModel;

        public event System.Action? RequestNavigateToRegister;
        public event System.Action<string>? RequestSignIn;

        public LoginView()
        {
            InitializeComponent();
            DataContextChanged += LoginView_DataContextChanged;

            LibraryManagement.Helpers.PasswordBoxHelper.SetupPasswordToggle(
                TxtPassword,
                TxtPasswordVisible,
                BtnTogglePassword,
                IconPasswordToggle,
                pwd => PlaceholderPassword.Visibility = string.IsNullOrEmpty(pwd) ? Visibility.Visible : Visibility.Collapsed);
        }

        private void LoginView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_observedViewModel != null)
            {
                _observedViewModel.PropertyChanged -= AuthViewModel_PropertyChanged;
            }

            _observedViewModel = e.NewValue as AuthViewModel;
            if (_observedViewModel == null)
            {
                StatusCard.Visibility = Visibility.Collapsed;
                return;
            }

            _observedViewModel.PropertyChanged += AuthViewModel_PropertyChanged;
            UpdateStatusVisibility(_observedViewModel);
        }

        private void AuthViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName is nameof(AuthViewModel.StatusMessage) or nameof(AuthViewModel.IsStatusError))
                && _observedViewModel != null)
            {
                UpdateStatusVisibility(_observedViewModel);
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

        public void FocusFirstInput() => TxtUsernameOrEmail.Focus();

        public string GetPassword() => TxtPassword.Password;
    }
}
