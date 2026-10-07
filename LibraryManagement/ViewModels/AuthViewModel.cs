using System;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels
{
    public class AuthViewModel : BaseViewModel
    {
        private readonly AuthService _authService;

        private string _signInEmailOrUsername="";
        public string SignInEmailOrUsername
        {
            get => _signInEmailOrUsername;
            set => SetProperty(ref _signInEmailOrUsername, value);
        }

        private string _signInPassword = "";
        public string SignInPassword
        {
            get => _signInPassword;
            set => SetProperty(ref _signInPassword, value);
        }

        private bool _rememberMe = true;
        public bool RememberMe
        {
            get => _rememberMe;
            set => SetProperty(ref _rememberMe, value);
        }

        private string _registerFullName = "";
        public string RegisterFullName
        {
            get => _registerFullName;
            set => SetProperty(ref _registerFullName, value);
        }

        private string _registerEmail = "";
        public string RegisterEmail
        {
            get => _registerEmail;
            set => SetProperty(ref _registerEmail, value);
        }

        private string _registerPassword = "";
        public string RegisterPassword
        {
            get => _registerPassword;
            set => SetProperty(ref _registerPassword, value);
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isStatusError = false;
        public bool IsStatusError
        {
            get => _isStatusError;
            set => SetProperty(ref _isStatusError, value);
        }

        public ICommand ForgotPasswordCommand { get; }

        public event Action<User>? LoginSuccessful;
        public event Action? RequestSwipeToRegister;
        public event Action? RequestSwipeToSignIn;

        public AuthViewModel()
        {
            _authService = new AuthService();

            ForgotPasswordCommand = new RelayCommand(_ => ExecuteForgotPassword());
        }

        public void TriggerSwipeToRegister()
        {
            StatusMessage = "";
            RequestSwipeToRegister?.Invoke();
        }

        public void TriggerSwipeToSignIn()
        {
            StatusMessage = "";
            RequestSwipeToSignIn?.Invoke();
        }

        public void ExecuteSignIn(object? parameter)
        {
            string password = parameter as string ?? SignInPassword;

            StatusMessage = "";

            var result = _authService.Login(SignInEmailOrUsername, password);

            if (result.Success && result.User != null)
            {
                StatusMessage = result.Message;
                IsStatusError = false;
                LoginSuccessful?.Invoke(result.User);
            }
            else
            {
                StatusMessage = result.Message;
                IsStatusError = true;
            }
        }

        public void ExecuteRegister(object? parameter)
        {
            string password = parameter as string ?? RegisterPassword;

            StatusMessage = "";

            var result = _authService.Register(RegisterFullName, RegisterEmail, password);

            if (result.Success)
            {
                StatusMessage = result.Message;
                IsStatusError = false;

                // Pre-fill email for sign in and return to the sign-in view.
                SignInEmailOrUsername = RegisterEmail;
                TriggerSwipeToSignIn();
            }
            else
            {
                StatusMessage = result.Message;
                IsStatusError = true;
            }
        }

        private void ExecuteForgotPassword()
        {
            StatusMessage = "Password reset instructions sent to your email (Demo).";
            IsStatusError = false;
        }
    }
}
