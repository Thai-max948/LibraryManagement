using System;

namespace LibraryManagement.Models
{
    public sealed record MyAccountProfile(
        int UserId,
        string FullName,
        string Username,
        string Email,
        string Role,
        DateTime CreatedAt);
}
