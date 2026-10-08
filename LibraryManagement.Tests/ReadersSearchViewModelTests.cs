using System.Collections.Concurrent;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public sealed class ReadersSearchViewModelTests
{
    [Fact]
    public async Task SearchText_DebouncesRapidTypingAndResetsToFirstPage()
    {
        var context = CreateContext();
        Assert.Single(context.Queries);

        StaHelper.RunInSta(() => context.ViewModel.NextPageCommand.Execute(null));
        Assert.Equal(2, context.ViewModel.CurrentPage);
        int immediateQueryCount = context.Queries.Count;

        StaHelper.RunInSta(() =>
        {
            context.ViewModel.SearchText = "n";
            context.ViewModel.SearchText = "ng";
            context.ViewModel.SearchText = "ngu";
        });

        Assert.Equal(1, context.ViewModel.CurrentPage);
        Assert.Equal(immediateQueryCount, context.Queries.Count);
        await Task.Delay(100);
        Assert.Equal(immediateQueryCount, context.Queries.Count);

        await Task.Delay(450);

        var queries = context.Queries.ToArray();
        Assert.Equal(immediateQueryCount + 1, queries.Length);
        var searchQuery = Assert.Single(queries, query => query.Keyword is not null);
        Assert.Equal("ngu", searchQuery.Keyword);
        Assert.Equal(1, searchQuery.PageNumber);
    }

    [Fact]
    public async Task EmptySearch_RetainsCurrentFiltersSortAndPageSize()
    {
        var context = CreateContext();
        StaHelper.RunInSta(() =>
        {
            context.ViewModel.SelectedTypeFilter = "Student";
            context.ViewModel.SelectedStatusFilter = "Suspended";
            context.ViewModel.SelectedSort = "Oldest";
            context.ViewModel.PageSize = 10;
            context.ViewModel.NextPageCommand.Execute(null);
        });

        Assert.Equal(2, context.ViewModel.CurrentPage);
        int immediateQueryCount = context.Queries.Count;
        StaHelper.RunInSta(() => context.ViewModel.SearchText = "  ");

        Assert.Equal(1, context.ViewModel.CurrentPage);
        Assert.Equal(immediateQueryCount, context.Queries.Count);
        await Task.Delay(450);

        var query = context.Queries.ToArray()[^1];
        Assert.Null(query.Keyword);
        Assert.Equal("Student", query.ReaderType);
        Assert.Equal("Suspended", query.Status);
        Assert.Equal("Oldest", query.SortBy);
        Assert.Equal(10, query.PageSize);
        Assert.Equal(1, query.PageNumber);
    }

    [Fact]
    public async Task TypeStatusSortPageSizeAndPagingRemainImmediate()
    {
        var context = CreateContext();
        StaHelper.RunInSta(() =>
        {
            int count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-type";
            context.ViewModel.SelectedTypeFilter = "Student";
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("pending-type", context.Queries.ToArray()[^1].Keyword);
            Assert.Equal("Student", context.Queries.ToArray()[^1].ReaderType);

            count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-status";
            context.ViewModel.SelectedStatusFilter = "Active";
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("pending-status", context.Queries.ToArray()[^1].Keyword);
            Assert.Equal("Active", context.Queries.ToArray()[^1].Status);

            count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-sort";
            context.ViewModel.SelectedSort = "Newest";
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("Newest", context.Queries.ToArray()[^1].SortBy);

            count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-size";
            context.ViewModel.PageSize = 10;
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal(10, context.Queries.ToArray()[^1].PageSize);

            count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-next";
            context.ViewModel.NextPageCommand.Execute(null);
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("pending-next", context.Queries.ToArray()[^1].Keyword);
            Assert.Equal(2, context.Queries.ToArray()[^1].PageNumber);

            count = context.Queries.Count;
            context.ViewModel.PreviousPageCommand.Execute(null);
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal(1, context.Queries.ToArray()[^1].PageNumber);
        });

        int finalQueryCount = context.Queries.Count;
        await Task.Delay(450);
        Assert.Equal(finalQueryCount, context.Queries.Count);
    }

    [Fact]
    public async Task SearchAndLoadRemainImmediateAndCancelPendingTextSearch()
    {
        var context = CreateContext();
        StaHelper.RunInSta(() =>
        {
            int count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-search";
            context.ViewModel.Search();
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("pending-search", context.Queries.ToArray()[^1].Keyword);

            count = context.Queries.Count;
            context.ViewModel.SearchText = "pending-load";
            context.ViewModel.Load();
            Assert.Equal(count + 1, context.Queries.Count);
            Assert.Equal("pending-load", context.Queries.ToArray()[^1].Keyword);
        });

        int finalQueryCount = context.Queries.Count;
        await Task.Delay(450);
        Assert.Equal(finalQueryCount, context.Queries.Count);
    }

    private static TestContext CreateContext()
    {
        var repository = new Mock<ReaderRepository>();
        var queries = new ConcurrentQueue<ReaderPageQuery>();
        repository.Setup(repo => repo.GetPage(It.IsAny<ReaderPageQuery>()))
            .Returns((ReaderPageQuery query) =>
            {
                queries.Enqueue(query);
                return new ReaderPage
                {
                    TotalCount = 45,
                    PageNumber = query.GetEffectivePageNumber(45),
                    PageSize = query.PageSize
                };
            });

        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);
        var viewModel = StaHelper.RunInSta(() => new ReadersViewModel(service));
        return new TestContext(viewModel, queries);
    }

    private sealed record TestContext(
        ReadersViewModel ViewModel,
        ConcurrentQueue<ReaderPageQuery> Queries);
}
