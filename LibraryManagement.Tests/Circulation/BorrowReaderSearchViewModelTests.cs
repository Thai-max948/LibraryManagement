using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BorrowReaderSearchViewModelTests
{
    [Fact]
    public async Task OpeningBorrowLoadsFirstFiveReaderSuggestionsWithoutLoadingCatalog()
    {
        var context = CreateContext((readers, _) =>
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(hasMore: true, Readers(1, 2, 3, 4, 5))));

        await context.ViewModel.InitialRecommendationsLoaded;

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));
        Assert.Equal(1, context.ViewModel.ReaderRecommendationPage);
        Assert.True(context.ViewModel.ReaderRecommendationHasMore);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 1, 5), Times.Once);
        context.ReaderRepository.Verify(repository => repository.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ReaderLoadMoreAppendsFiveAtATimeAndStopsAtLastPage()
    {
        var context = CreateContext((readers, _) =>
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, It.IsAny<int>(), 5))
                .Returns((string? _, int pageNumber, int _) => pageNumber switch
                {
                    1 => Page(true, Readers(1, 2, 3, 4, 5)),
                    2 => Page(true, Readers(6, 7, 8, 9, 10)),
                    3 => Page(false, Readers(11, 12)),
                    _ => Page(false)
                }));

        await context.ViewModel.InitialRecommendationsLoaded;
        Assert.Equal(5, context.ViewModel.RecommendedReaders.Count);

        await context.ViewModel.LoadMoreReadersAsync();
        Assert.Equal(10, context.ViewModel.RecommendedReaders.Count);
        Assert.Equal(2, context.ViewModel.ReaderRecommendationPage);

        await context.ViewModel.LoadMoreReadersAsync();
        Assert.Equal(12, context.ViewModel.RecommendedReaders.Count);
        Assert.Equal(3, context.ViewModel.ReaderRecommendationPage);
        Assert.False(context.ViewModel.ReaderRecommendationHasMore);

        await context.ViewModel.LoadMoreReadersAsync();
        Assert.Equal(12, context.ViewModel.RecommendedReaders.Count);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 4, 5), Times.Never);
    }

    [Fact]
    public async Task ReaderSearchReplacesResultsAfterDebounceAndClearingRestoresDefaultPage()
    {
        var context = CreateContext((readers, _) =>
        {
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Readers(1, 2, 3, 4, 5)));
            readers.Setup(repository => repository.SearchForBorrow("nguyen", 1, 5))
                .Returns(Page(true, Readers(20, 21, 22, 23, 24)));
            readers.Setup(repository => repository.SearchForBorrow("nguyen", 2, 5))
                .Returns(Page(false, Readers(25, 26)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.ReaderSearchQuery = "nguyen";
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));
        await WaitUntilAsync(() => context.ViewModel.RecommendedReaders.FirstOrDefault()?.ReaderId == 20);

        Assert.Equal(new[] { 20, 21, 22, 23, 24 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));
        Assert.Equal(1, context.ViewModel.ReaderRecommendationPage);
        await context.ViewModel.LoadMoreReadersAsync();
        Assert.Equal(new[] { 20, 21, 22, 23, 24, 25, 26 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));

        context.ViewModel.ReaderSearchQuery = "  ";
        Assert.Equal(20, context.ViewModel.RecommendedReaders[0].ReaderId);
        await WaitUntilAsync(() => context.ViewModel.RecommendedReaders.FirstOrDefault()?.ReaderId == 1);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));
        Assert.Equal(1, context.ViewModel.ReaderRecommendationPage);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow("nguyen", 1, 5), Times.Once);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 1, 5), Times.Exactly(2));
    }

    [Fact]
    public async Task RapidReaderTypingRunsOnlyTheLatestDebouncedQuery()
    {
        var context = CreateContext((readers, _) =>
            readers.Setup(repository => repository.SearchForBorrow("nguyen", 1, 5))
                .Returns(Page(false, Readers(9))));
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.ReaderSearchQuery = "n";
        context.ViewModel.ReaderSearchQuery = "ng";
        context.ViewModel.ReaderSearchQuery = "nguyen";

        await WaitUntilAsync(() => context.ViewModel.RecommendedReaders.FirstOrDefault()?.ReaderId == 9);

        context.ReaderRepository.Verify(repository => repository.SearchForBorrow("nguyen", 1, 5), Times.Once);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow("n", 1, 5), Times.Never);
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow("ng", 1, 5), Times.Never);
    }

    [Fact]
    public async Task StaleReaderLoadMoreCannotAppendAfterSearchChanges()
    {
        var blockedPage = new TaskCompletionSource<BorrowRecommendationPage<Reader>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = CreateContext((readers, _) =>
        {
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Readers(1, 2, 3, 4, 5)));
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 2, 5))
                .Returns(() => blockedPage.Task.GetAwaiter().GetResult());
            readers.Setup(repository => repository.SearchForBorrow("nguyen", 1, 5))
                .Returns(Page(false, Readers(20, 21)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        Task pendingLoad = context.ViewModel.LoadMoreReadersAsync();
        await WaitUntilAsync(() => context.ViewModel.IsLoadingMoreReaders);
        context.ViewModel.ReaderSearchQuery = "nguyen";
        await WaitUntilAsync(() => context.ViewModel.RecommendedReaders.FirstOrDefault()?.ReaderId == 20);

        blockedPage.SetResult(Page(false, Readers(6, 7, 8, 9, 10)));
        await pendingLoad;

        Assert.Equal(new[] { 20, 21 }, context.ViewModel.RecommendedReaders.Select(reader => reader.ReaderId));
        Assert.Equal(1, context.ViewModel.ReaderRecommendationPage);
    }

    [Fact]
    public async Task RepeatedReaderScrollRequestsDoNotStartDuplicateQueries()
    {
        var blockedPage = new TaskCompletionSource<BorrowRecommendationPage<Reader>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = CreateContext((readers, _) =>
        {
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Readers(1, 2, 3, 4, 5)));
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 2, 5))
                .Returns(() => blockedPage.Task.GetAwaiter().GetResult());
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        Task firstLoad = context.ViewModel.LoadMoreReadersAsync();
        await WaitUntilAsync(() => context.ViewModel.IsLoadingMoreReaders);
        await context.ViewModel.LoadMoreReadersAsync();
        context.ReaderRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 2, 5), Times.Once);

        blockedPage.SetResult(Page(false, Readers(6)));
        await firstLoad;
        Assert.Equal(6, context.ViewModel.RecommendedReaders.Count);
    }

    [Fact]
    public async Task ReaderPagingDoesNotRequestBookPages()
    {
        var context = CreateContext((readers, books) =>
        {
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Readers(1, 2, 3, 4, 5)));
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 2, 5))
                .Returns(Page(false, Readers(6)));
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(new BorrowRecommendationPage<BorrowBookSuggestion>(Array.Empty<BorrowBookSuggestion>(), true));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        await context.ViewModel.LoadMoreReadersAsync();

        context.BookRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 2, 5), Times.Never);
        Assert.Equal(2, context.ViewModel.ReaderRecommendationPage);
        Assert.Equal(1, context.ViewModel.BookRecommendationPage);
    }

    [Fact]
    public async Task SelectingAndClearingReaderPreservesEligibilityAndReloadsDefaultSuggestions()
    {
        var reader = new Reader { ReaderId = 7, FullName = "Nguyễn An", ReaderType = "Student" };
        var context = CreateContext((readers, _) =>
        {
            readers.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(false, reader));
            readers.Setup(repository => repository.GetById(reader.ReaderId)).Returns(reader);
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.SelectReader(reader);
        Assert.Same(reader, context.ViewModel.SelectedReader);
        Assert.Contains("Đủ điều kiện mượn", context.ViewModel.EligibilityPreview);
        Assert.Contains("Student", context.ViewModel.PolicyPreview);

        context.ViewModel.ClearReaderSelection();
        await WaitUntilAsync(() => context.ReaderRepository.Invocations.Count(invocation =>
            invocation.Method.Name == nameof(ReaderRepository.SearchForBorrow) &&
            invocation.Arguments.Count == 3 &&
            Equals(invocation.Arguments[0], string.Empty)) >= 2);

        Assert.Null(context.ViewModel.SelectedReader);
        Assert.Single(context.ViewModel.RecommendedReaders);
    }

    private static TestContext CreateContext(
        Action<Mock<ReaderRepository>, Mock<BookRepository>>? configure = null)
    {
        var readerRepository = new Mock<ReaderRepository>();
        var bookRepository = new Mock<BookRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var copyRepository = new Mock<BookCopyRepository>();
        var policyRepository = new Mock<LoanPolicyRepository>();
        var dialog = new Mock<IUserDialogService>();

        readerRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<Reader>(Array.Empty<Reader>(), false));
        bookRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<BorrowBookSuggestion>(Array.Empty<BorrowBookSuggestion>(), false));
        borrowRepository.Setup(repository => repository.GetCurrentBorrowingPage(It.IsAny<CurrentBorrowingPageQuery>()))
            .Returns((CurrentBorrowingPageQuery query) => new CurrentBorrowingPage(
                Array.Empty<CurrentBorrowingRow>(), query.PageNumber, query.PageSize, 0));
        borrowRepository.Setup(repository => repository.GetEligibilityRecords(It.IsAny<int>()))
            .Returns(new List<BorrowRecord>());
        copyRepository.Setup(repository => repository.GetAvailableByBookId(It.IsAny<int>()))
            .Returns(new List<BookCopy>());
        policyRepository.Setup(repository => repository.GetActiveByReaderType(It.IsAny<string>()))
            .Returns((string readerType) => new LoanPolicy
            {
                ReaderType = readerType,
                LoanPeriodDays = 14,
                IsActive = true
            });
        configure?.Invoke(readerRepository, bookRepository);

        var borrowService = new BorrowService(bookRepository.Object, borrowRepository.Object,
            readerRepository.Object, copyRepository.Object);
        var bookService = new BookService(bookRepository.Object, borrowRepository.Object);
        var copyService = new BookCopyService(copyRepository.Object, bookRepository.Object);
        var readerService = new ReaderService(readerRepository.Object, borrowRepository.Object);
        var policyService = new LoanPolicyService(policyRepository.Object);
        var viewModel = new BorrowViewModel(dialog.Object, borrowService, bookService,
            copyService, readerService, policyService);

        return new TestContext(viewModel, readerRepository, bookRepository);
    }

    private static BorrowRecommendationPage<Reader> Page(bool hasMore, params Reader[] readers) => new(readers, hasMore);

    private static Reader[] Readers(params int[] ids) => ids
        .Select(id => new Reader { ReaderId = id, FullName = $"Reader {id}" })
        .ToArray();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(condition(), "Expected recommendation state was not reached before timeout.");
    }

    private sealed record TestContext(
        BorrowViewModel ViewModel,
        Mock<ReaderRepository> ReaderRepository,
        Mock<BookRepository> BookRepository);
}
