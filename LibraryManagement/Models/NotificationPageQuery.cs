namespace LibraryManagement.Models;

/// <summary>Normalized options for a bounded notification history query.</summary>
public sealed class NotificationPageQuery
{
    public const int DefaultPageSize = 50;
    public const int MinimumPageSize = 1;
    public const int MaximumPageSize = 100;
    public const string StableOrderBySql = "n.CreatedAt DESC, n.Id DESC";

    public NotificationPageQuery(bool unreadOnly = false, int pageNumber = 1, int pageSize = DefaultPageSize)
    {
        UnreadOnly = unreadOnly;
        PageNumber = Math.Max(1, pageNumber);
        PageSize = Math.Clamp(pageSize, MinimumPageSize, MaximumPageSize);
    }

    public bool UnreadOnly { get; }
    public int PageNumber { get; }
    public int PageSize { get; }

    public int GetEffectivePageNumber(int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        return Math.Clamp(PageNumber, 1, totalPages);
    }
}
