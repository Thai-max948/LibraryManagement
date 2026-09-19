using System;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class AuthService
    {
        private static User? _currentUser;
        public static User? CurrentUser
        {
            get => _currentUser;
            set => _currentUser = value;
        }

        private readonly UserRepository _userRepository = new();

        public (bool Success, string Message, User? User) Login(string usernameOrEmail, string password)
        {
            if (string.IsNullOrWhiteSpace(usernameOrEmail))
            {
                return (false, "Please enter your username or email.", null);
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Please enter your password.", null);
            }

            var result = _userRepository.Authenticate(usernameOrEmail, password);
            if (result.Success && result.User != null)
            {
                CurrentUser = result.User;
            }
            return result;
        }

        public (bool Success, string Message, User? User) Register(string fullName, string email, string password)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Please enter your full name.", null);
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return (false, "Please enter a valid email address.", null);
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                return (false, "Password must be at least 6 characters.", null);
            }

            string trimmedEmail = email.Trim().ToLowerInvariant();
            string trimmedName = fullName.Trim();

            var allUsers = _userRepository.GetAll();

            if (allUsers.Any(u => string.Equals(u.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "email này đã được sử dụng!", null);
            }

            string baseUsername = trimmedEmail.Split('@')[0];
            string username = baseUsername;
            int suffix = 1;
            while (allUsers.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                suffix++;
                username = $"{baseUsername}{suffix}";
            }

            var newUser = new User
            {
                Username = username,
                Email = trimmedEmail,
                FullName = trimmedName,
                Role = "Librarian",
                CreatedAt = DateTime.Now
            };

            return _userRepository.Add(newUser, password);
        }
    }
}