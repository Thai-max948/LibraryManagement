using LibraryManagement.Models;

namespace LibraryManagement.Tests;

public sealed class NotificationPageQueryTests
{
    [Fact]
    public void DefaultsToAllNotificationsOnPageOneWithFiftyItems()
    {
        var query = new NotificationPageQuery();

        Assert.False(query.UnreadOnly);
        Assert.Equal(1, query.PageNumber);
        Assert.Equal(50, query.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void PageNumber_IsNormalizedToAtLeastOne(int requested, int expected)
    {
        var query = new NotificationPageQuery(pageNumber: requested);

        Assert.Equal(expected, query.PageNumber);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(1000, 100)]
    public void PageSize_IsBounded(int requested, int expected)
    {
        var query = new NotificationPageQuery(pageSize: requested);

        Assert.Equal(expected, query.PageSize);
    }

    [Fact]
    public void AllAndUnreadModesAreExplicit()
    {
        Assert.False(new NotificationPageQuery(unreadOnly: false).UnreadOnly);
        Assert.True(new NotificationPageQuery(unreadOnly: true).UnreadOnly);
    }

    [Fact]
    public void StableOrdering_IsNewestFirstWithIdTieBreaker()
    {
        Assert.Equal("n.CreatedAt DESC, n.Id DESC", NotificationPageQuery.StableOrderBySql);
    }

    [Fact]
    public void RequestedPage_IsClampedAndEmptyResultUsesPageOne()
    {
        var query = new NotificationPageQuery(pageNumber: 8, pageSize: 10);

        Assert.Equal(3, query.GetEffectivePageNumber(totalCount: 25));
        Assert.Equal(1, query.GetEffectivePageNumber(totalCount: 0));
    }
}
