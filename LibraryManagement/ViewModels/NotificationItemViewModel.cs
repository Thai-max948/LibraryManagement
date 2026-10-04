using System;
using LibraryManagement.Models;

namespace LibraryManagement.ViewModels;

public class NotificationItemViewModel : BaseViewModel
{
    private bool _isRead;

    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationType Type { get; init; }
    public string SourceModule { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string CreatedAtText => CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public string Icon => Type switch
    {
        NotificationType.Success => "✓",
        NotificationType.Info => "ℹ",
        NotificationType.Warning => "⚠",
        NotificationType.Error => "✕",
        NotificationType.DueSoon => "⏰",
        _ => "•"
    };
    public string AccentColor => Type switch
    {
        NotificationType.Success => "#059669",
        NotificationType.Warning or NotificationType.DueSoon => "#D97706",
        NotificationType.Error => "#E11D48",
        _ => "#0284C7"
    };
    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public static NotificationItemViewModel FromNotification(Notification notification) => new()
    {
        Id = notification.Id, Title = notification.Title, Message = notification.Message,
        Type = notification.Type, SourceModule = notification.SourceModule,
        CreatedAt = notification.CreatedAt.ToLocalTime(), IsRead = notification.IsRead
    };
}
