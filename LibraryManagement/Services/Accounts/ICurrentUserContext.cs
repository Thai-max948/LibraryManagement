using LibraryManagement.Models;

namespace LibraryManagement.Services;

public interface ICurrentUserContext
{
    User? CurrentUser { get; }
}

public sealed class AuthServiceCurrentUserContext : ICurrentUserContext
{
    public User? CurrentUser => AuthService.CurrentUser;
}
