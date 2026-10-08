namespace LibraryManagement.Models;

/// <summary>Fixed-size page options for the Borrow workflow's current-loans panel.</summary>
public sealed class CurrentBorrowingPageQuery
{
    public const int FixedPageSize = 5;

    public int PageNumber { get; }
    public int PageSize => FixedPageSize;

    public CurrentBorrowingPageQuery(int pageNumber = 1)
    {
        PageNumber = Math.Max(1, pageNumber);
    }
}
