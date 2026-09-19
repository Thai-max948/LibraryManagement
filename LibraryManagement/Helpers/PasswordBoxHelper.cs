using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LibraryManagement.Helpers
{
    /// <summary>
    /// Helper for password visibility toggle micro-interactions.
    /// Provides synchronization between PasswordBox and visible TextBox,
    /// smooth icon transitions, and cursor focus preservation.
    /// </summary>
    public static class PasswordBoxHelper
    {
        // Vector icon geometry for Eye (Show password)
        public static readonly Geometry EyeOpenGeometry = Geometry.Parse(
            "M 2,12 C 6,5 18,5 22,12 C 18,19 6,19 2,12 Z M 12,8.5 C 10.07,8.5 8.5,10.07 8.5,12 C 8.5,13.93 10.07,15.5 12,15.5 C 13.93,15.5 15.5,13.93 15.5,12 C 15.5,10.07 13.93,8.5 12,8.5 Z");

        // Vector icon geometry for Eye with slash (Hide password)
        public static readonly Geometry EyeSlashGeometry = Geometry.Parse(
            "M 2,12 C 6,5 18,5 22,12 C 18,19 6,19 2,12 Z M 12,8.5 C 10.07,8.5 8.5,10.07 8.5,12 C 8.5,13.93 10.07,15.5 12,15.5 C 13.93,15.5 15.5,13.93 15.5,12 C 15.5,10.07 13.93,8.5 12,8.5 Z M 3,3 L 21,21");

        /// <summary>
        /// Configures two-way synchronization and micro-interaction toggle for a password box and visible text box.
        /// </summary>
        public static Action SetupPasswordToggle(
            PasswordBox passwordBox,
            TextBox visibleTextBox,
            Button toggleButton,
            Path iconPath,
            Action<string>? onPasswordChanged = null)
        {
            bool isVisible = false;

            visibleTextBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            iconPath.Data = EyeOpenGeometry;
            toggleButton.ToolTip = "Show password";

            passwordBox.PasswordChanged += (s, e) =>
            {
                if (!isVisible)
                {
                    if (visibleTextBox.Text != passwordBox.Password)
                    {
                        visibleTextBox.Text = passwordBox.Password;
                    }
                    onPasswordChanged?.Invoke(passwordBox.Password);
                }
                else
                {
                    // In case password was reset or changed externally
                    if (visibleTextBox.Text != passwordBox.Password)
                    {
                        visibleTextBox.Text = passwordBox.Password;
                        onPasswordChanged?.Invoke(visibleTextBox.Text);
                    }
                }
            };

            visibleTextBox.TextChanged += (s, e) =>
            {
                if (isVisible)
                {
                    if (passwordBox.Password != visibleTextBox.Text)
                    {
                        passwordBox.Password = visibleTextBox.Text;
                    }
                    onPasswordChanged?.Invoke(visibleTextBox.Text);
                }
            };

            void Toggle()
            {
                isVisible = !isVisible;
                if (isVisible)
                {
                    visibleTextBox.Text = passwordBox.Password;
                    passwordBox.Visibility = Visibility.Collapsed;
                    visibleTextBox.Visibility = Visibility.Visible;
                    visibleTextBox.Focus();
                    visibleTextBox.CaretIndex = visibleTextBox.Text.Length;
                    toggleButton.ToolTip = "Hide password";
                    iconPath.Data = EyeSlashGeometry;
                }
                else
                {
                    passwordBox.Password = visibleTextBox.Text;
                    visibleTextBox.Visibility = Visibility.Collapsed;
                    passwordBox.Visibility = Visibility.Visible;
                    passwordBox.Focus();
                    SetPasswordBoxCaretToEnd(passwordBox);
                    toggleButton.ToolTip = "Show password";
                    iconPath.Data = EyeOpenGeometry;
                }
            }

            toggleButton.Click += (s, e) => Toggle();

            // Return reset action
            return () =>
            {
                if (isVisible)
                {
                    Toggle();
                }
                passwordBox.Clear();
                visibleTextBox.Clear();
            };
        }

        private static void SetPasswordBoxCaretToEnd(PasswordBox passwordBox)
        {
            try
            {
                MethodInfo? selectMethod = typeof(PasswordBox).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
                selectMethod?.Invoke(passwordBox, new object[] { passwordBox.Password.Length, 0 });
            }
            catch
            {
                // Fallback: simply retain focus
            }
        }
    }
}
