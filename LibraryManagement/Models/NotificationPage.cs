using System;
using System.Collections.Generic;

namespace LibraryManagement.Models;

public sealed class NotificationPage
{
    public IReadOnlyList<Notification> Items { get; init; } = Array.Empty<Notification>();
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}
