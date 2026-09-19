using System;
using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class UserService
    {
        private readonly UserRepository _userRepository;

        public UserService()
        {
            _userRepository = new UserRepository();
        }

        private static void EnsureAdmin()
        {
            var current = AuthService.CurrentUser;
            if (current == null || !string.Equals(current.Role, "Administrator", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("Bạn không có quyền thực hiện thao tác này.");
            }
        }

        public List<User> GetAllUsers()
        {
            EnsureAdmin();
            return _userRepository.GetAll();
        }

        public List<User> SearchUsers(string? query)
        {
            EnsureAdmin();
            var list = _userRepository.GetAll();
            if (string.IsNullOrWhiteSpace(query))
            {
                return list;
            }

            string q = query.Trim().ToLowerInvariant();
            return list.Where(u =>
                u.FullName.ToLowerInvariant().Contains(q) ||
                u.Username.ToLowerInvariant().Contains(q) ||
                u.Email.ToLowerInvariant().Contains(q) ||
                u.Role.ToLowerInvariant().Contains(q)
            ).ToList();
        }

        public (bool Success, string Message, User? User) CreateAccount(
            string fullName, string username, string email, string password, string role = "Librarian")
        {
            EnsureAdmin();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Please enter the full name.", null);
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "Please enter a username.", null);
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return (false, "Please enter a valid email address.", null);
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                return (false, "Password must be at least 6 characters.", null);
            }

            string trimmedUsername = username.Trim().ToLowerInvariant();
            string trimmedEmail = email.Trim().ToLowerInvariant();

            var allUsers = _userRepository.GetAll();
            if (allUsers.Any(u => string.Equals(u.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "email này đã được sử dụng!", null);
            }
            if (allUsers.Any(u => string.Equals(u.Username, trimmedUsername, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "Username này đã được sử dụng!", null);
            }

            var user = new User
            {
                FullName = fullName.Trim(),
                Username = trimmedUsername,
                Email = trimmedEmail,
                Role = string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ? "Administrator" : "Librarian",
                CreatedAt = DateTime.Now
            };

            return _userRepository.Add(user, password);
        }

        public bool UpdateUser(User user)
        {
            EnsureAdmin();
            if (user == null)
            {
                return false;
            }
            return _userRepository.Update(user);
        }

        public (bool Success, string Message) UpdateAccount(
            int id, string fullName, string username, string email, string role, string? newPassword = null)
        {
            EnsureAdmin();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Please enter the full name.");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "Please enter a username.");
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return (false, "Please enter a valid email address.");
            }

            if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 6)
            {
                return (false, "New password must be at least 6 characters.");
            }

            var allUsers = _userRepository.GetAll();
            if (allUsers.Any(u => u.Id != id && string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "email này đã được sử dụng!");
            }

            if (allUsers.Any(u => u.Id != id && string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "An account with this username already exists.");
            }

            var existing = _userRepository.GetById(id);
            if (existing == null)
            {
                return (false, "User account not found.");
            }

            bool demotingFromAdmin = string.Equals(existing.Role, "Administrator", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
            if (demotingFromAdmin)
            {
                int adminCount = allUsers.Count(u => string.Equals(u.Role, "Administrator", StringComparison.OrdinalIgnoreCase));
                if (adminCount <= 1)
                {
                    return (false, "Không thể hạ quyền Administrator cuối cùng trong hệ thống.");
                }
            }

            existing.FullName = fullName.Trim();
            existing.Username = username.Trim().ToLowerInvariant();
            existing.Email = email.Trim().ToLowerInvariant();
            existing.Role = string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ? "Administrator" : "Librarian";

            bool ok = _userRepository.Update(existing, newPassword);
            if (ok)
            {
                if (AuthService.CurrentUser != null && AuthService.CurrentUser.Id == existing.Id)
                {
                    AuthService.CurrentUser = existing;
                }
                return (true, "Account updated successfully.");
            }

            return (false, "Failed to update account.");
        }

        public bool DeleteUser(int id)
        {
            EnsureAdmin();

            var current = AuthService.CurrentUser;
            if (current != null && current.Id == id)
            {
                throw new BusinessRuleException("Không thể tự xóa tài khoản đang đăng nhập.");
            }

            var target = _userRepository.GetById(id);
            if (target == null)
            {
                return false;
            }

            if (string.Equals(target.Role, "Administrator", StringComparison.OrdinalIgnoreCase))
            {
                int adminCount = _userRepository.GetAll().Count(u => string.Equals(u.Role, "Administrator", StringComparison.OrdinalIgnoreCase));
                if (adminCount <= 1)
                {
                    throw new BusinessRuleException("Không thể xóa Administrator cuối cùng trong hệ thống.");
                }
            }

            return _userRepository.Delete(id);
        }

        public (bool Success, string Message) UpdateSelfProfile(int userId, string fullName, string username, string email)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Please enter your full name.");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "Please enter your username.");
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return (false, "Please enter a valid email address.");
            }

            var allUsers = _userRepository.GetAll();
            if (allUsers.Any(u => u.Id != userId && string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "email này đã được sử dụng!");
            }

            if (allUsers.Any(u => u.Id != userId && string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "An account with this username already exists.");
            }

            var existing = _userRepository.GetById(userId);
            if (existing == null)
            {
                return (false, "Account not found.");
            }

            existing.FullName = fullName.Trim();
            existing.Username = username.Trim().ToLowerInvariant();
            existing.Email = email.Trim().ToLowerInvariant();

            bool ok = _userRepository.Update(existing);
            if (ok)
            {
                if (AuthService.CurrentUser != null && AuthService.CurrentUser.Id == userId)
                {
                    AuthService.CurrentUser = existing;
                }
                return (true, "Profile updated successfully!");
            }

            return (false, "Failed to update profile.");
        }

        public (bool Success, string Message) ChangePassword(int userId, string oldPassword, string newPassword, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(oldPassword))
            {
                return (false, "Please enter your current password.");
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                return (false, "New password must be at least 6 characters long.");
            }

            if (newPassword != confirmPassword)
            {
                return (false, "New passwords do not match.");
            }

            if (!_userRepository.VerifyPassword(userId, oldPassword))
            {
                return (false, "Current password is incorrect.");
            }

            bool ok = _userRepository.ChangePassword(userId, newPassword);
            return ok ? (true, "Password changed successfully!") : (false, "Failed to update password.");
        }
    }
}