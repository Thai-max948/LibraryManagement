using System;
using System.Windows;
using System.Windows.Controls;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Accounts
{
    public partial class EditAccountDialog : Window
    {
        private readonly User _existingUser;

        public User? ResultUser { get; private set; }
        public string? ResultPassword { get; private set; }

        public EditAccountDialog(User existing)
        {
            InitializeComponent();
            _existingUser = existing;

            TxtFullName.Text = existing.FullName;
            TxtUsername.Text = existing.Username;
            TxtEmail.Text = existing.Email;

            if (string.Equals(existing.Role, "Administrator", StringComparison.OrdinalIgnoreCase))
            {
                CmbRole.SelectedIndex = 1;
            }
            else
            {
                CmbRole.SelectedIndex = 0;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string fullName = TxtFullName.Text.Trim();
            string username = TxtUsername.Text.Trim();
            string email = TxtEmail.Text.Trim();
            string role = (CmbRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Librarian";
            string newPassword = TxtNewPassword.Password;
            string confirmPassword = TxtConfirmPassword.Password;

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

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                if (newPassword.Length < 6)
                {
                    ShowError("New password must be at least 6 characters long.");
                    TxtNewPassword.Focus();
                    return;
                }

                if (newPassword != confirmPassword)
                {
                    ShowError("New passwords do not match. Please re-enter.");
                    TxtConfirmPassword.Focus();
                    return;
                }

                ResultPassword = newPassword;
            }
            else
            {
                ResultPassword = null;
            }

            ResultUser = new User
            {
                Id = _existingUser.Id,
                FullName = fullName,
                Username = username.ToLowerInvariant(),
                Email = email.ToLowerInvariant(),
                Role = role,
                CreatedAt = _existingUser.CreatedAt
            };

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
            TxtError.Visibility = Visibility.Visible;
        }
    }
}
