using LibraryManagement.Models;

namespace LibraryManagement.Tests;

public sealed class CurrentBorrowingPageModelTests
{
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void QueryNormalizesPageNumberAndKeepsFixedPageSize(int requestedPage, int expectedPage)
    {
        var query = new CurrentBorrowingPageQuery(requestedPage);

        Assert.Equal(expectedPage, query.PageNumber);
        Assert.Equal(5, query.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(10, 2)]
    [InlineData(12, 3)]
    public void PageResultReportsAtLeastOnePageAndCorrectTotalPages(int totalCount, int expectedPages)
    {
        var page = new CurrentBorrowingPage(Array.Empty<CurrentBorrowingRow>(), 1, 5, totalCount);

        Assert.Equal(expectedPages, page.TotalPages);
    }
}
