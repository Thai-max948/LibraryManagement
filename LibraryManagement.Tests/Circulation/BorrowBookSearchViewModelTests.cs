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

public sealed class BorrowBookSearchViewModelTests
{
    [Fact]
    public async Task OpeningBorrowLoadsFirstFiveAvailableBookSuggestionsWithoutCatalogPreload()
    {
        var suggestions = Suggestions(1, 2, 3, 4, 5);
        var context = CreateContext(books =>
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, suggestions)));

        await context.ViewModel.InitialRecommendationsLoaded;

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));
        Assert.Equal(1, context.ViewModel.BookRecommendationPage);
        Assert.True(context.ViewModel.BookRecommendationHasMore);
        Assert.Equal(5, context.ViewModel.RecommendedBooks[0].AvailableQuantity);
        context.BookRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 1, 5), Times.Once);
        context.BookRepository.Verify(repository => repository.GetAll(), Times.Never);
    }

    [Fact]
    public async Task BookLoadMoreAppendsFiveAtATimeAndStopsAtLastPage()
    {
        var context = CreateContext(books =>
            books.Setup(repository => repository.SearchForBorrow(string.Empty, It.IsAny<int>(), 5))
                .Returns((string? _, int pageNumber, int _) => pageNumber switch
                {
                    1 => Page(true, Suggestions(1, 2, 3, 4, 5)),
                    2 => Page(true, Suggestions(6, 7, 8, 9, 10)),
                    3 => Page(false, Suggestions(11, 12)),
                    _ => Page(false)
                }));
        await context.ViewModel.InitialRecommendationsLoaded;

        Assert.Equal(5, context.ViewModel.RecommendedBooks.Count);
        await context.ViewModel.LoadMoreBooksAsync();
        Assert.Equal(10, context.ViewModel.RecommendedBooks.Count);
        Assert.Equal(2, context.ViewModel.BookRecommendationPage);

        await context.ViewModel.LoadMoreBooksAsync();
        Assert.Equal(12, context.ViewModel.RecommendedBooks.Count);
        Assert.Equal(3, context.ViewModel.BookRecommendationPage);
        Assert.False(context.ViewModel.BookRecommendationHasMore);

        await context.ViewModel.LoadMoreBooksAsync();
        Assert.Equal(12, context.ViewModel.RecommendedBooks.Count);
        context.BookRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 4, 5), Times.Never);
    }

    [Fact]
    public async Task BookSearchReplacesFirstPageAfterDebounceAndClearingRestoresDefaults()
    {
        var context = CreateContext(books =>
        {
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Suggestions(1, 2, 3, 4, 5)));
            books.Setup(repository => repository.SearchForBorrow("clean", 1, 5))
                .Returns(Page(true, Suggestions(20, 21, 22, 23, 24)));
            books.Setup(repository => repository.SearchForBorrow("clean", 2, 5))
                .Returns(Page(false, Suggestions(25, 26)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.BookSearchQuery = "clean";
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));
        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 20);
        Assert.Equal(new[] { 20, 21, 22, 23, 24 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));
        Assert.Equal(1, context.ViewModel.BookRecommendationPage);

        await context.ViewModel.LoadMoreBooksAsync();
        Assert.Equal(new[] { 20, 21, 22, 23, 24, 25, 26 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));

        context.ViewModel.BookSearchQuery = string.Empty;
        Assert.Equal(20, context.ViewModel.RecommendedBooks[0].BookId);
        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 1);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));

        context.BookRepository.Verify(repository => repository.SearchForBorrow("clean", 1, 5), Times.Once);
        context.BookRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 1, 5), Times.Exactly(2));
    }

    [Fact]
    public async Task RapidBookTypingRunsOnlyTheLatestDebouncedQuery()
    {
        var context = CreateContext(books =>
            books.Setup(repository => repository.SearchForBorrow("clean", 1, 5))
                .Returns(Page(false, Suggestions(9))));
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.BookSearchQuery = "c";
        context.ViewModel.BookSearchQuery = "cl";
        context.ViewModel.BookSearchQuery = "clean";

        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 9);

        context.BookRepository.Verify(repository => repository.SearchForBorrow("clean", 1, 5), Times.Once);
        context.BookRepository.Verify(repository => repository.SearchForBorrow("c", 1, 5), Times.Never);
        context.BookRepository.Verify(repository => repository.SearchForBorrow("cl", 1, 5), Times.Never);
    }

    [Fact]
    public async Task StaleBookLoadMoreCannotAppendAfterSearchChanges()
    {
        var blockedPage = new TaskCompletionSource<BorrowRecommendationPage<BorrowBookSuggestion>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = CreateContext(books =>
        {
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Suggestions(1, 2, 3, 4, 5)));
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 2, 5))
                .Returns(() => blockedPage.Task.GetAwaiter().GetResult());
            books.Setup(repository => repository.SearchForBorrow("clean", 1, 5))
                .Returns(Page(false, Suggestions(20, 21)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        Task pendingLoad = context.ViewModel.LoadMoreBooksAsync();
        await WaitUntilAsync(() => context.ViewModel.IsLoadingMoreBooks);
        context.ViewModel.BookSearchQuery = "clean";
        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 20);

        blockedPage.SetResult(Page(false, Suggestions(6, 7, 8, 9, 10)));
        await pendingLoad;

        Assert.Equal(new[] { 20, 21 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));
        Assert.Equal(1, context.ViewModel.BookRecommendationPage);
    }

    [Fact]
    public async Task BookPagingDoesNotRequestReaderPages()
    {
        var context = CreateContext(books =>
        {
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(true, Suggestions(1, 2, 3, 4, 5)));
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 2, 5))
                .Returns(Page(false, Suggestions(6)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        await context.ViewModel.LoadMoreBooksAsync();

        context.ReaderRepository.Verify(repository => repository.SearchForBorrow(string.Empty, 2, 5), Times.Never);
        Assert.Equal(2, context.ViewModel.BookRecommendationPage);
        Assert.Equal(1, context.ViewModel.ReaderRecommendationPage);
    }

    [Fact]
    public async Task BarcodeEnterCancelsPendingSearchAndSelectsExactCopyImmediately()
    {
        var copy = new BookCopy
        {
            CopyId = 123,
            BookId = 17,
            Barcode = "BK-000123",
            Status = BookCopyStatuses.Available
        };
        var context = CreateContext(books => books.Setup(repository => repository.GetById(17))
            .Returns(new Book
            {
                BookId = 17,
                Title = "Clean Code",
                Author = "Robert C. Martin",
                Category = "Programming",
                Status = BookStatuses.Active
            }));
        await context.ViewModel.InitialRecommendationsLoaded;
        context.CopyRepository.Setup(repository => repository.GetByBarcode("BK-000123")).Returns(copy);
        var reader = new Reader { ReaderId = 9, FullName = "Barcode reader", ReaderType = "Student" };
        context.ReaderRepository.Setup(repository => repository.GetById(9)).Returns(reader);
        context.ViewModel.SelectedReader = reader;

        context.ViewModel.BookSearchQuery = "clean";
        context.ViewModel.BookSearchQuery = "BK-000123";
        context.ViewModel.SearchBookInputCommand.Execute(null);

        Assert.Equal(17, context.ViewModel.SelectedBook?.BookId);
        Assert.Same(copy, context.ViewModel.SelectedCopy);
        Assert.Single(context.ViewModel.AvailableCopies);
        Assert.Collection(context.ViewModel.SelectedBorrowCopies,
            selected =>
            {
                Assert.Equal(copy.CopyId, selected.CopyId);
                Assert.Equal(copy.Barcode, selected.Barcode);
                Assert.Equal("Clean Code", selected.Title);
            });
        await Task.Delay(400);
        context.CopyRepository.Verify(repository => repository.GetByBarcode("BK-000123"), Times.Once);
        context.BookRepository.Verify(repository => repository.SearchForBorrow("clean", 1, 5), Times.Never);
        context.BookRepository.Verify(repository => repository.GetAll(), Times.Never);
    }

    [Fact]
    public async Task BrowsingAndRecommendationPagingKeepCopiesSelectedAcrossDifferentBookIds()
    {
        var context = CreateContext(books =>
        {
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(false, new BorrowBookSuggestion(10, "Clean Code", "Author A", "Programming", null, 1)));
            books.Setup(repository => repository.SearchForBorrow("design", 1, 5))
                .Returns(Page(true, new BorrowBookSuggestion(25, "Design Patterns", "Author B", "Programming", null, 1)));
            books.Setup(repository => repository.SearchForBorrow("csharp", 1, 5))
                .Returns(Page(true, new BorrowBookSuggestion(48, "C# in Depth", "Author C", "Programming", null, 1)));
            books.Setup(repository => repository.SearchForBorrow("csharp", 2, 5))
                .Returns(Page(false, new BorrowBookSuggestion(49, "Another Book", "Author D", "Programming", null, 1)));
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        var reader = new Reader { ReaderId = 9, FullName = "Multi-book reader", ReaderType = "Student" };
        context.ReaderRepository.Setup(repository => repository.GetById(9)).Returns(reader);
        context.ViewModel.SelectedReader = reader;
        SetupAvailableCopy(context, 10, 101, "BK-000101");
        SetupAvailableCopy(context, 25, 251, "BK-000251");
        SetupAvailableCopy(context, 48, 481, "BK-000481");

        context.ViewModel.SelectBook(context.ViewModel.RecommendedBooks.Single());
        context.ViewModel.AddSelectedCopyCommand.Execute(null);
        Assert.Equal(10, context.ViewModel.SelectedBook?.BookId);

        context.ViewModel.BookSearchQuery = "design";
        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 25);
        context.ViewModel.SelectBook(context.ViewModel.RecommendedBooks.Single());
        Assert.Equal("design", context.ViewModel.BookSearchQuery);
        Assert.Equal(new[] { 101 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.CopyId));
        context.ViewModel.AddSelectedCopyCommand.Execute(null);

        context.ViewModel.BookSearchQuery = "csharp";
        await WaitUntilAsync(() => context.ViewModel.RecommendedBooks.FirstOrDefault()?.BookId == 48);
        context.ViewModel.SelectBook(context.ViewModel.RecommendedBooks.Single());
        context.ViewModel.AddSelectedCopyCommand.Execute(null);
        await context.ViewModel.LoadMoreBooksAsync();

        Assert.Equal(new[] { 101, 251, 481 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.CopyId));
        Assert.Equal(new[] { 10, 25, 48 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.BookId));
        Assert.Equal("C# in Depth", context.ViewModel.SelectedBorrowCopies[2].Title);
        Assert.Equal(new[] { 48, 49 }, context.ViewModel.RecommendedBooks.Select(book => book.BookId));
    }

    [Fact]
    public async Task BarcodeEnterAppendsCopiesFromDifferentBookIds()
    {
        var copies = new[]
        {
            new BookCopy { CopyId = 101, BookId = 10, Barcode = "BK-000101", Status = BookCopyStatuses.Available },
            new BookCopy { CopyId = 251, BookId = 25, Barcode = "BK-000251", Status = BookCopyStatuses.Available },
            new BookCopy { CopyId = 481, BookId = 48, Barcode = "BK-000481", Status = BookCopyStatuses.Available }
        };
        var context = CreateContext(books =>
        {
            foreach (var copy in copies)
            {
                books.Setup(repository => repository.GetById(copy.BookId)).Returns(new Book
                {
                    BookId = copy.BookId,
                    Title = copy.BookId switch { 10 => "Clean Code", 25 => "Design Patterns", _ => "C# in Depth" },
                    Status = BookStatuses.Active
                });
            }
        });
        await context.ViewModel.InitialRecommendationsLoaded;

        var reader = new Reader { ReaderId = 9, FullName = "Barcode multi-book reader", ReaderType = "Student" };
        context.ReaderRepository.Setup(repository => repository.GetById(9)).Returns(reader);
        context.ViewModel.SelectedReader = reader;
        foreach (var copy in copies)
            context.CopyRepository.Setup(repository => repository.GetByBarcode(copy.Barcode)).Returns(copy);

        foreach (var copy in copies)
        {
            context.ViewModel.BookSearchQuery = copy.Barcode;
            context.ViewModel.SearchBookInputCommand.Execute(null);
        }

        Assert.Equal(new[] { 101, 251, 481 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.CopyId));
        Assert.Equal(new[] { 10, 25, 48 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.BookId));
    }

    [Fact]
    public async Task SelectingAndClearingBookRestoresDefaultSuggestionsAndAuthoritativeCopies()
    {
        var suggestion = Suggestions(17).Single();
        var copies = new List<BookCopy>
        {
            new() { CopyId = 101, BookId = 17, Barcode = "BK-000101", Status = BookCopyStatuses.Available },
            new() { CopyId = 102, BookId = 17, Barcode = "BK-000102", Status = BookCopyStatuses.Available }
        };
        var context = CreateContext(books =>
        {
            books.Setup(repository => repository.SearchForBorrow(string.Empty, 1, 5))
                .Returns(Page(false, suggestion));
            books.Setup(repository => repository.GetById(17)).Returns(new Book
            {
                BookId = 17,
                Title = "Clean Code",
                Author = "Robert C. Martin",
                Category = "Programming",
                Status = BookStatuses.Active
            });
        });
        context.CopyRepository.Setup(repository => repository.GetAvailableByBookId(17)).Returns(copies);
        await context.ViewModel.InitialRecommendationsLoaded;

        context.ViewModel.SelectBook(context.ViewModel.RecommendedBooks.Single());
        Assert.Equal(17, context.ViewModel.SelectedBook?.BookId);
        Assert.Equal(2, context.ViewModel.AvailableCopies.Count);
        Assert.Same(copies[0], context.ViewModel.SelectedCopy);

        context.ViewModel.ClearBookSelection();
        await WaitUntilAsync(() => context.BookRepository.Invocations.Count(invocation =>
            invocation.Method.Name == nameof(BookRepository.SearchForBorrow) &&
            invocation.Arguments.Count == 3 &&
            Equals(invocation.Arguments[0], string.Empty)) >= 2);

        Assert.Null(context.ViewModel.SelectedBook);
        Assert.Null(context.ViewModel.SelectedCopy);
        Assert.Single(context.ViewModel.RecommendedBooks);
    }

    private static TestContext CreateContext(Action<Mock<BookRepository>>? configureBooks = null)
    {
        var bookRepository = new Mock<BookRepository>();
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var copyRepository = new Mock<BookCopyRepository>();
        var policyRepository = new Mock<LoanPolicyRepository>();
        var dialog = new Mock<IUserDialogService>();

        bookRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<BorrowBookSuggestion>(Array.Empty<BorrowBookSuggestion>(), false));
        readerRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<Reader>(Array.Empty<Reader>(), false));
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
        configureBooks?.Invoke(bookRepository);

        var borrowService = new BorrowService(bookRepository.Object, borrowRepository.Object,
            readerRepository.Object, copyRepository.Object);
        var bookService = new BookService(bookRepository.Object, borrowRepository.Object);
        var copyService = new BookCopyService(copyRepository.Object, bookRepository.Object);
        var readerService = new ReaderService(readerRepository.Object, borrowRepository.Object);
        var policyService = new LoanPolicyService(policyRepository.Object);
        var viewModel = new BorrowViewModel(dialog.Object, borrowService, bookService,
            copyService, readerService, policyService);

        return new TestContext(viewModel, bookRepository, readerRepository, copyRepository);
    }

    private static BorrowRecommendationPage<BorrowBookSuggestion> Page(
        bool hasMore, params BorrowBookSuggestion[] suggestions) => new(suggestions, hasMore);

    private static BorrowBookSuggestion[] Suggestions(params int[] ids) => ids
        .Select(id => new BorrowBookSuggestion(id, $"Book {id}", "Author", "Category", null, 5))
        .ToArray();

    private static void SetupAvailableCopy(TestContext context, int bookId, int copyId, string barcode) =>
        context.CopyRepository.Setup(repository => repository.GetAvailableByBookId(bookId))
            .Returns(new List<BookCopy>
            {
                new() { CopyId = copyId, BookId = bookId, Barcode = barcode, Status = BookCopyStatuses.Available }
            });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(condition(), "Expected recommendation state was not reached before timeout.");
    }

    private sealed record TestContext(
        BorrowViewModel ViewModel,
        Mock<BookRepository> BookRepository,
        Mock<ReaderRepository> ReaderRepository,
        Mock<BookCopyRepository> CopyRepository);
}
