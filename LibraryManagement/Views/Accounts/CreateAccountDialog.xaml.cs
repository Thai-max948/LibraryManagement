using System;
using System.Windows;
using System.Windows.Controls;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Accounts
{
    public partial class CreateAccountDialog : Window
    {
        public User? ResultUser { get; private set; }
        public string ResultPassword { get; private set; } = string.Empty;

        public CreateAccountDialog()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LibraryManagement.Helpers.PasswordBoxHelper.SetupPasswordToggle(
                TxtPassword,
                TxtPasswordVisible,
                BtnTogglePassword,
                IconPasswordToggle,
                _ => InputChanged(null!, null!));

            LibraryManagement.Helpers.PasswordBoxHelper.SetupPasswordToggle(
                TxtConfirmPassword,
                TxtConfirmPasswordVisible,
                BtnToggleConfirmPassword,
                IconConfirmPasswordToggle,
                _ => InputChanged(null!, null!));

            TxtFullName.Focus();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string fullName = TxtFullName.Text.Trim();
            string username = TxtUsername.Text.Trim();
            string email = TxtEmail.Text.Trim();
            string role = (CmbRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Librarian";
            string password = TxtPassword.Password;
            string confirm = TxtConfirmPassword.Password;

            if (string.IsNullOrWhiteSpace(fullName))
            {
                ShowError("Please enter the user's full name.");
                TxtFullName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("Please enter a username.");
                TxtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ShowError("Please enter a valid email address.");
                TxtEmail.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                ShowError("Password must be at least 6 characters long.");
                TxtPassword.Focus();
                return;
            }

            if (password != confirm)
            {
                ShowError("Passwords do not match. Please re-enter.");
                TxtConfirmPassword.Focus();
                return;
            }

            ResultUser = new User
            {
                FullName = fullName,
                Username = username.ToLowerInvariant(),
                Email = email.ToLowerInvariant(),
                Role = role,
                CreatedAt = DateTime.Now
            };
            ResultPassword = password;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            TxtError.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void InputChanged(object sender, RoutedEventArgs e)
        {
            if (ErrorPanel != null)
            {
                ErrorPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
