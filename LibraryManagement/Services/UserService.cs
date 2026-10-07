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
        private readonly ICurrentUserContext _currentUserContext;

        public UserService() : this(new UserRepository(), new AuthServiceCurrentUserContext())
        {
        }

        public UserService(UserRepository userRepository) : this(userRepository, new AuthServiceCurrentUserContext())
        {
        }

        public UserService(UserRepository userRepository, ICurrentUserContext currentUserContext)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        }

        private static void EnsureAdmin()
        {
            var current = AuthService.CurrentUser;
            if (!UserPermissions.CanManageUsers(current))
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

            if (_userRepository.ExistsByEmail(trimmedEmail))
            {
                return (false, "email này đã được sử dụng!", null);
            }
            if (_userRepository.ExistsByUsername(trimmedUsername))
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

            if (_userRepository.ExistsByEmail(email, id))
            {
                return (false, "email này đã được sử dụng!");
            }

            if (_userRepository.ExistsByUsername(username, id))
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
                int adminCount = _userRepository.GetAll().Count(u => string.Equals(u.Role, "Administrator", StringComparison.OrdinalIgnoreCase));
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

        public MyAccountProfile GetMyProfile()
        {
            var user = GetMyAccountFromRepository();
            return ToProfile(user);
        }

        public (bool Success, string Message, MyAccountProfile? Profile) UpdateMyProfile(
            string fullName, string username, string email)
        {
            var currentUserId = GetMyUserId();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Please enter your full name.", null);
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "Please enter your username.", null);
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return (false, "Please enter a valid email address.", null);
            }

            string normalizedUsername = username.Trim().ToLowerInvariant();
            string normalizedEmail = email.Trim().ToLowerInvariant();

            if (_userRepository.ExistsByEmail(normalizedEmail, currentUserId))
            {
                return (false, "email này đã được sử dụng!", null);
            }

            if (_userRepository.ExistsByUsername(normalizedUsername, currentUserId))
            {
                return (false, "An account with this username already exists.", null);
            }

            var existing = _userRepository.GetById(currentUserId);
            if (existing == null)
            {
                return (false, "Account not found.", null);
            }

            existing.FullName = fullName.Trim();
            existing.Username = normalizedUsername;
            existing.Email = normalizedEmail;

            bool ok = _userRepository.UpdateProfile(
                existing.Id,
                existing.FullName,
                existing.Username,
                existing.Email);
            if (ok)
            {
                AuthService.CurrentUser = existing;
                return (true, "Profile updated successfully!", ToProfile(existing));
            }

            if (_userRepository.ExistsByEmail(normalizedEmail, currentUserId))
            {
                return (false, "email này đã được sử dụng!", null);
            }
            if (_userRepository.ExistsByUsername(normalizedUsername, currentUserId))
            {
                return (false, "An account with this username already exists.", null);
            }

            return (false, "Failed to update profile.", null);
        }

        public (bool Success, string Message) ChangeMyPassword(
            string currentPassword, string newPassword, string confirmPassword)
        {
            int currentUserId = GetMyUserId();

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                return (false, "Please enter your current password.");
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                return (false, "Please enter a new password.");
            }

            if (newPassword.Length < 6)
            {
                return (false, "New password must be at least 6 characters long.");
            }

            if (newPassword != confirmPassword)
            {
                return (false, "New passwords do not match.");
            }

            if (!_userRepository.VerifyPassword(currentUserId, currentPassword))
            {
                return (false, "Current password is incorrect.");
            }

            bool ok = _userRepository.ChangePassword(currentUserId, newPassword);
            return ok ? (true, "Password changed successfully!") : (false, "Failed to update password.");
        }

        public AccountAccessInfo GetMyAccess()
        {
            var user = GetMyAccountFromRepository();
            return UserPermissions.Describe(user);
        }

        private int GetMyUserId()
        {
            var currentUser = _currentUserContext.CurrentUser;
            if (currentUser == null || currentUser.Id <= 0)
            {
                throw new BusinessRuleException("No authenticated account is available.");
            }

            return currentUser.Id;
        }

        private User GetMyAccountFromRepository()
        {
            int currentUserId = GetMyUserId();
            return _userRepository.GetById(currentUserId)
                ?? throw new BusinessRuleException("The authenticated account could not be found.");
        }

        private static MyAccountProfile ToProfile(User user) => new(
            user.Id,
            user.FullName,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt);
    }
}
