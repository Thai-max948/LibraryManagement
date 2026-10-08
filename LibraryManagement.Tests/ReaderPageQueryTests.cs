using LibraryManagement.Models;

namespace LibraryManagement.Tests;

public sealed class ReaderPageQueryTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-12, 1)]
    [InlineData(3, 3)]
    public void PageNumber_IsNormalizedToAtLeastOne(int requestedPage, int expectedPage)
    {
        var query = CreateQuery(pageNumber: requestedPage);

        Assert.Equal(expectedPage, query.PageNumber);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(20, 20)]
    [InlineData(10_000, 100)]
    public void PageSize_IsBounded(int requestedSize, int expectedSize)
    {
        var query = CreateQuery(pageSize: requestedSize);

        Assert.Equal(expectedSize, query.PageSize);
    }

    [Fact]
    public void RequestedPage_IsClampedToLastPageAfterCount()
    {
        var query = CreateQuery(pageNumber: 9, pageSize: 10);

        Assert.Equal(3, query.GetEffectivePageNumber(totalCount: 25));
    }

    [Fact]
    public void EmptyResult_UsesFirstPage()
    {
        var query = CreateQuery(pageNumber: 8, pageSize: 10);

        Assert.Equal(1, query.GetEffectivePageNumber(totalCount: 0));
    }

    [Theory]
    [InlineData("Name A-Z", "r.FullName ASC, r.ReaderId ASC")]
    [InlineData("Name Z-A", "r.FullName DESC, r.ReaderId DESC")]
    [InlineData("Newest", "r.RegistrationDate DESC, r.ReaderId DESC")]
    [InlineData("Oldest", "r.RegistrationDate ASC, r.ReaderId ASC")]
    [InlineData("Status", "r.Status ASC, r.FullName ASC, r.ReaderId ASC")]
    public void SortOptions_MapToStableWhitelistedOrderBy(string sortBy, string expectedOrderBy)
    {
        var query = CreateQuery(sortBy: sortBy);

        Assert.Equal(expectedOrderBy, query.OrderBySql);
    }

    [Theory]
    [InlineData("Unknown sort")]
    [InlineData(null)]
    public void InvalidSort_FallsBackToNameAscending(string? sortBy)
    {
        var query = CreateQuery(sortBy: sortBy);

        Assert.Equal("Name A-Z", query.SortBy);
        Assert.Equal("r.FullName ASC, r.ReaderId ASC", query.OrderBySql);
    }

    [Fact]
    public void Keyword_IsTrimmedAndAllFiltersAreOmitted()
    {
        var query = CreateQuery(keyword: "  R000123  ", readerType: " All ", status: "all");

        Assert.Equal("R000123", query.Keyword);
        Assert.Null(query.ReaderType);
        Assert.Null(query.Status);
    }

    [Fact]
    public void SelectedTypeAndStatusFilters_AreTrimmedAndPreserved()
    {
        var query = CreateQuery(readerType: " Student ", status: " Active ");

        Assert.Equal("Student", query.ReaderType);
        Assert.Equal("Active", query.Status);
    }

    [Fact]
    public void LecturerTypeFilter_IsPreservedForRepositoryPaging()
    {
        var query = CreateQuery(readerType: " Lecturer ", pageNumber: 2, pageSize: 10);

        Assert.Equal("Lecturer", query.ReaderType);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(10, query.PageSize);
    }

    private static ReaderPageQuery CreateQuery(
        string? keyword = null,
        string? readerType = "All",
        string? status = "All",
        string? sortBy = "Name A-Z",
        int pageNumber = 1,
        int pageSize = 20) =>
        new(keyword, readerType, status, sortBy, pageNumber, pageSize);
}
