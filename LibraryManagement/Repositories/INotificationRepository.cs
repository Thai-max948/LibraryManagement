using LibraryManagement.Models;

namespace LibraryManagement.Repositories;

public interface INotificationRepository
{
    Task<Notification?> AddAsync(Notification notification, string? idempotencyKey = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetUnreadAsync(CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}
