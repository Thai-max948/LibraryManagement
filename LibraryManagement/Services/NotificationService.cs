using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public interface INotificationService
{
    event Action<Notification>? Created;
    Task<Notification?> NotifyAsync(string title, string message, NotificationType type, string sourceModule,
        string? sourceEntityType = null, string? sourceEntityId = null, CancellationToken cancellationToken = default, string? idempotencyKey = null);
    Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetUnreadAsync(CancellationToken cancellationToken = default);
    Task<NotificationPage> GetPageAsync(NotificationPageQuery query, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    public event Action<Notification>? Created;

    public NotificationService(INotificationRepository repository) => _repository = repository;

    public async Task<Notification?> NotifyAsync(string title, string message, NotificationType type, string sourceModule,
        string? sourceEntityType = null, string? sourceEntityId = null, CancellationToken cancellationToken = default, string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new ArgumentException("Tiêu đề thông báo không hợp lệ.", nameof(title));
        if (string.IsNullOrWhiteSpace(message) || message.Length > 1000) throw new ArgumentException("Nội dung thông báo không hợp lệ.", nameof(message));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(sourceModule) || sourceModule.Length > 40) throw new ArgumentException("Nguồn thông báo không hợp lệ.", nameof(sourceModule));
        if (sourceEntityType?.Length > 80 || sourceEntityId?.Length > 100) throw new ArgumentException("Mã nguồn thông báo quá dài.");
        if (idempotencyKey?.Length > 200) throw new ArgumentException("Idempotency key quá dài.", nameof(idempotencyKey));
        var saved = await _repository.AddAsync(new Notification
        {
            Title = title.Trim(), Message = message.Trim(), Type = type, SourceModule = sourceModule.Trim(),
            SourceEntityType = sourceEntityType, SourceEntityId = sourceEntityId, IdempotencyKey = idempotencyKey, CreatedAt = DateTime.UtcNow
        }, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (saved == null) return null;
        Created?.Invoke(saved);
        return saved;
    }

    public Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default) => _repository.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<Notification>> GetUnreadAsync(CancellationToken cancellationToken = default) => _repository.GetUnreadAsync(cancellationToken);
    public Task<NotificationPage> GetPageAsync(NotificationPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _repository.GetPageAsync(query, cancellationToken);
    }
    public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) => _repository.GetUnreadCountAsync(cancellationToken);
    public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default) => _repository.MarkAsReadAsync(id, cancellationToken);
    public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default) => _repository.MarkAllAsReadAsync(cancellationToken);
}
