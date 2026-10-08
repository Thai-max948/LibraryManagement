namespace LibraryManagement.Models;

/// <summary>Normalized filters and bounded paging options for a reader-list query.</summary>
public sealed class ReaderPageQuery
{
    public const int MinimumPageSize = 5;
    public const int MaximumPageSize = 100;

    public ReaderPageQuery(
        string? keyword,
        string? readerType,
        string? status,
        string? sortBy,
        int pageNumber,
        int pageSize)
    {
        Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        ReaderType = NormalizeFilter(readerType);
        Status = NormalizeFilter(status);
        SortBy = NormalizeSort(sortBy);
        PageNumber = Math.Max(1, pageNumber);
        PageSize = Math.Clamp(pageSize, MinimumPageSize, MaximumPageSize);
    }

    public string? Keyword { get; }
    public string? ReaderType { get; }
    public string? Status { get; }
    public string SortBy { get; }
    public int PageNumber { get; }
    public int PageSize { get; }

    public int GetEffectivePageNumber(int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        return Math.Clamp(PageNumber, 1, totalPages);
    }

    // Every fragment is a fixed whitelist value; caller-provided sort text is never SQL.
    public string OrderBySql => SortBy switch
    {
        "Name Z-A" => "r.FullName DESC, r.ReaderId DESC",
        "Newest" => "r.RegistrationDate DESC, r.ReaderId DESC",
        "Oldest" => "r.RegistrationDate ASC, r.ReaderId ASC",
        "Status" => "r.Status ASC, r.FullName ASC, r.ReaderId ASC",
        _ => "r.FullName ASC, r.ReaderId ASC"
    };

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "All", StringComparison.OrdinalIgnoreCase))
            return null;

        return value.Trim();
    }

    private static string NormalizeSort(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return candidate switch
        {
            "Name Z-A" => candidate,
            "Newest" => candidate,
            "Oldest" => candidate,
            "Status" => candidate,
            _ => "Name A-Z"
        };
    }
}
