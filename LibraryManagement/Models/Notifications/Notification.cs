namespace LibraryManagement.Models;

public sealed class Notification
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public NotificationType Type { get; init; }
    public required string SourceModule { get; init; }
    public string? SourceEntityType { get; init; }
    public string? SourceEntityId { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsRead { get; init; }
}
