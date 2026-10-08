namespace LibraryManagement.Models;

public static class BookFilterCodes
{
    public const string All = "All";
    public const string Uncategorized = "__uncategorized__";
}

public enum BookStatusFilter
{
    Active,
    Archived,
    All
}

public sealed record BookSearchQuery
{
    public string? SearchText { get; init; }
    public string? Category { get; init; } = BookFilterCodes.All;
    public string? Author { get; init; }
    public string? LanguageCode { get; init; } = LanguageCatalog.AllFilterCode;
    public string? Publisher { get; init; }
    public int? PublishYearFrom { get; init; }
    public int? PublishYearTo { get; init; }
    public decimal? MinBookPrice { get; init; }
    public decimal? MaxBookPrice { get; init; }
    public BookStatusFilter Status { get; init; } = BookStatusFilter.Active;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string SortBy { get; init; } = "Title";
    public string SortDirection { get; init; } = "ASC";
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record BookCatalogMetrics(int TotalBooks, int AvailableBooks, int BorrowedBooks);

public sealed record BookPriceRange(decimal? Minimum, decimal? Maximum)
{
    public bool HasConfiguredPrices => Minimum is not null && Maximum is not null;
}

public sealed record BookFilterOption(string Value, string Label);

public sealed record BookStatusFilterOption(BookStatusFilter Value, string Label);

public sealed record BookSortOption(string Label, string SortBy, string SortDirection);

public sealed record BookFilterChip(string Key, string Label);
