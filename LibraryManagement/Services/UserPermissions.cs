using System;
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public static class UserPermissions
    {
        public static bool CanManageUsers(User? user) => CanManageUsers(user?.Role);

        public static bool CanManageUsers(string? role) =>
            string.Equals(role?.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);

        public static AccountAccessInfo Describe(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            bool canManageUsers = CanManageUsers(user);
            return new AccountAccessInfo(
                user.Role,
                canManageUsers ? "Full system access" : "Library operations",
                CanManageCatalog: true,
                CanManageReaders: true,
                CanUseCirculation: true,
                CanViewFeesAndHistory: true,
                CanManageUsers: canManageUsers);
        }
    }
}
