using System.Globalization;
using System.Collections.Concurrent;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public class BookSearchTests
{
    [Fact]
    public void GetPagedBooks_ForwardsCombinedFiltersSortingAndPaging()
    {
        var repository = new Mock<BookRepository>();
        BookSearchQuery? forwarded = null;
        var query = new BookSearchQuery
        {
            SearchText = "  clean code  ",
            Category = " Programming ",
            Author = " Robert C. Martin ",
            LanguageCode = " EN ",
            Publisher = " Prentice Hall ",
            PublishYearFrom = 2010,
            PublishYearTo = 2020,
            MinBookPrice = 20.00m,
            MaxBookPrice = 80.50m,
            Status = BookStatusFilter.Archived,
            PageNumber = 2,
            PageSize = 25,
            SortBy = "Title",
            SortDirection = "DESC"
        };
        var expected = new PagedResult<Book>(Array.Empty<Book>(), 31, 2, 25);
        repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()))
            .Callback<BookSearchQuery>(value => forwarded = value)
            .Returns(expected);

        var result = new BookService(repository.Object, new Mock<BorrowRepository>().Object).GetPagedBooks(query);

        Assert.Same(expected, result);
        repository.Verify(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()), Times.Once);
        Assert.NotNull(forwarded);
        Assert.Equal("clean code", forwarded.SearchText);
        Assert.Equal("Programming", forwarded.Category);
        Assert.Equal("Robert C. Martin", forwarded.Author);
        Assert.Equal("en", forwarded.LanguageCode);
        Assert.Equal("Prentice Hall", forwarded.Publisher);
        Assert.Equal(2010, forwarded.PublishYearFrom);
        Assert.Equal(2020, forwarded.PublishYearTo);
        Assert.Equal(20.00m, forwarded.MinBookPrice);
        Assert.Equal(80.50m, forwarded.MaxBookPrice);
        Assert.Equal(BookStatusFilter.Archived, forwarded.Status);
        Assert.Equal(2, forwarded.PageNumber);
        Assert.Equal(25, forwarded.PageSize);
        Assert.Equal("DESC", forwarded.SortDirection);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void GetPagedBooks_RejectsInvalidPageBounds(int pageNumber, int pageSize)
    {
        var repository = new Mock<BookRepository>();
        var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Throws<BusinessRuleException>(() => service.GetPagedBooks(new BookSearchQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        }));
        repository.Verify(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()), Times.Never);
    }

    [Theory]
    [InlineData("Title", "SIDEWAYS", 0)]
    [InlineData("Title; DROP TABLE Books", "ASC", 0)]
    [InlineData("Title", "ASC", 99)]
    public void GetPagedBooks_RejectsUnsupportedSortOrViewScope(string sortBy, string direction, int status)
    {
        var repository = new Mock<BookRepository>();
        var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Throws<BusinessRuleException>(() => service.GetPagedBooks(new BookSearchQuery
        {
            SortBy = sortBy,
            SortDirection = direction,
            Status = (BookStatusFilter)status
        }));
        repository.Verify(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()), Times.Never);
    }

    [Theory]
    [InlineData(2021, 2020, "", "")]
    [InlineData(null, null, "90.00", "80.00")]
    [InlineData(null, null, "-1.00", "")]
    [InlineData(null, null, "", "-0.01")]
    public void GetPagedBooks_RejectsInvalidRangesBeforeRepositoryCall(
        int? yearFrom, int? yearTo, string minPrice, string maxPrice)
    {
        var repository = new Mock<BookRepository>();
        var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Throws<BusinessRuleException>(() => service.GetPagedBooks(new BookSearchQuery
        {
            PublishYearFrom = yearFrom,
            PublishYearTo = yearTo,
            MinBookPrice = string.IsNullOrEmpty(minPrice) ? null : decimal.Parse(minPrice, CultureInfo.InvariantCulture),
            MaxBookPrice = string.IsNullOrEmpty(maxPrice) ? null : decimal.Parse(maxPrice, CultureInfo.InvariantCulture)
        }));
        repository.Verify(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 50, 1)]
    [InlineData(51, 25, 3)]
    [InlineData(100, 100, 1)]
    public void PagedResult_ComputesTotalPagesFromCountAndPageSize(int totalCount, int pageSize, int expectedPages)
    {
        var result = new PagedResult<Book>(Array.Empty<Book>(), totalCount, 1, pageSize);
        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void BooksViewModel_OnlyAppliesPopupDraftAfterApplyAndPreservesPagingSearchAndScope()
    {
        var (repository, queries) = CreateRepository();
        StaHelper.RunInSta(() =>
        {
            var viewModel = CreateViewModel(repository);
            viewModel.NextPageCommand.Execute(null);
            Assert.Equal(2, queries[^1].PageNumber);
            Assert.Equal(BookStatusFilter.Active, queries[^1].Status);
            Assert.Equal(0, viewModel.FilterCount);

            int queryCount = queries.Count;
            viewModel.SearchText = "clean";
            Assert.Equal(1, viewModel.PageNumber);
            Assert.Equal(queryCount, queries.Count);
            viewModel.IsFilterPanelOpen = true;
            viewModel.CategoryFilter = "Programming";
            viewModel.AuthorFilterText = "Robert C. Martin";
            viewModel.LanguageCodeFilter = " EN ";
            viewModel.PublisherFilter = "Prentice Hall";
            viewModel.PublishYearFromText = "2010";
            viewModel.PublishYearToText = "2020";
            viewModel.MinBookPriceText = "20";
            viewModel.MaxBookPriceText = "80.5";
            Assert.Equal(queryCount, queries.Count);
            Assert.Equal(0, viewModel.FilterCount);

            viewModel.SelectedSortOption = viewModel.SortOptions.Single(option => option.SortBy == "CreatedAt");
            viewModel.PageSize = 25;
            Assert.Equal(BookFilterCodes.All, queries[^1].Category);
            Assert.Null(queries[^1].Author);
            Assert.Equal(BookStatusFilter.Active, queries[^1].Status);

            viewModel.ApplyFiltersCommand.Execute(null);
            var combined = queries[^1];
            Assert.Equal("clean", combined.SearchText);
            Assert.Equal("Programming", combined.Category);
            Assert.Equal("Robert C. Martin", combined.Author);
            Assert.Equal("en", combined.LanguageCode);
            Assert.Equal("English", viewModel.ActiveFilterChips.Single(chip => chip.Key == "Language").Label);
            Assert.Equal("Prentice Hall", combined.Publisher);
            Assert.Equal(2010, combined.PublishYearFrom);
            Assert.Equal(2020, combined.PublishYearTo);
            Assert.Equal(20m, combined.MinBookPrice);
            Assert.Equal(80.5m, combined.MaxBookPrice);
            Assert.Equal(BookStatusFilter.Active, combined.Status);
            Assert.Equal("CreatedAt", combined.SortBy);
            Assert.Equal("DESC", combined.SortDirection);
            Assert.Equal(1, combined.PageNumber);
            Assert.Equal(25, combined.PageSize);
            Assert.Equal(6, viewModel.FilterCount);
            Assert.Contains(viewModel.ActiveFilterChips, chip => chip.Key == "PriceRange" && chip.Label == "Price $20.00–$80.50");
            Assert.False(viewModel.IsFilterPanelOpen);

            viewModel.ViewScope = BookStatusFilter.Archived;
            Assert.Equal(BookStatusFilter.Archived, queries[^1].Status);
            Assert.Equal(6, viewModel.FilterCount);
            Assert.DoesNotContain(viewModel.ActiveFilterChips, chip => chip.Key == "Status");
            viewModel.NextPageCommand.Execute(null);
            Assert.Equal(BookStatusFilter.Archived, queries[^1].Status);
            Assert.Equal("Robert C. Martin", queries[^1].Author);
            Assert.Equal(2, queries[^1].PageNumber);

            viewModel.RemoveFilterCommand.Execute("Author");
            Assert.Null(queries[^1].Author);
            Assert.Equal(1, queries[^1].PageNumber);
            Assert.Equal(5, viewModel.FilterCount);

            viewModel.ClearFiltersCommand.Execute(null);
            Assert.Equal("clean", queries[^1].SearchText);
            Assert.Equal(BookFilterCodes.All, queries[^1].Category);
            Assert.Null(queries[^1].Author);
            Assert.Equal(LanguageCatalog.AllFilterCode, queries[^1].LanguageCode);
            Assert.Null(queries[^1].Publisher);
            Assert.Null(queries[^1].PublishYearFrom);
            Assert.Null(queries[^1].PublishYearTo);
            Assert.Null(queries[^1].MinBookPrice);
            Assert.Null(queries[^1].MaxBookPrice);
            Assert.Equal(BookStatusFilter.Archived, queries[^1].Status);
            Assert.Equal("CreatedAt", queries[^1].SortBy);
            Assert.Equal("DESC", queries[^1].SortDirection);
            Assert.Equal(0, viewModel.FilterCount);
        });
    }

    [Fact]
    public void BooksViewModel_ClosingPopupDiscardsPendingChanges()
    {
        var (repository, queries) = CreateRepository();
        StaHelper.RunInSta(() =>
        {
            var viewModel = CreateViewModel(repository);
            viewModel.IsFilterPanelOpen = true;
            viewModel.AuthorFilterText = "Robert C. Martin";
            viewModel.MinBookPriceText = "20";
            int queryCount = queries.Count;
            viewModel.IsFilterPanelOpen = false;

            Assert.Equal(queryCount, queries.Count);
            Assert.Empty(viewModel.AuthorFilterText);
            Assert.Empty(viewModel.MinBookPriceText);
            Assert.Equal(0, viewModel.FilterCount);
            Assert.Null(queries[^1].Author);
            Assert.Null(queries[^1].MinBookPrice);
        });
    }

    [Fact]
    public void BooksViewModel_OffersDistinctAuthorSuggestionsAndRequiresAnExactAuthor()
    {
        var (repository, queries) = CreateRepository();
        StaHelper.RunInSta(() =>
        {
            var viewModel = CreateViewModel(repository);
            Assert.Equal(new[] { "George R. R. Martin", "Martin Fowler", "Robert C. Martin" }, viewModel.AuthorFilterOptions);
            viewModel.IsFilterPanelOpen = true;
            viewModel.AuthorFilterText = "mart";
            Assert.Equal(new[] { "George R. R. Martin", "Martin Fowler", "Robert C. Martin" }, viewModel.AuthorFilterOptions);
            Assert.True(viewModel.HasValidationError);
            Assert.False(viewModel.ApplyFiltersCommand.CanExecute(null));

            viewModel.AuthorFilterText = "Robert C. Martin";
            Assert.False(viewModel.HasValidationError);
            viewModel.ApplyFiltersCommand.Execute(null);
            Assert.Equal("Robert C. Martin", queries[^1].Author);
            Assert.Contains(viewModel.ActiveFilterChips, chip => chip.Key == "Author");
        });
    }

    [Fact]
    public void BooksViewModel_PriceTextAndDoubleThumbValuesStaySynchronizedAndRejectInvalidRange()
    {
        var (repository, queries) = CreateRepository();
        StaHelper.RunInSta(() =>
        {
            var viewModel = CreateViewModel(repository);
            viewModel.IsFilterPanelOpen = true;
            Assert.Equal(10d, viewModel.PriceSliderMinimum);
            Assert.Equal(100d, viewModel.PriceSliderMaximum);
            viewModel.PriceSliderLowerValue = 20d;
            viewModel.PriceSliderUpperValue = 80.5d;
            Assert.Equal("20", viewModel.MinBookPriceText);
            Assert.Equal("80.5", viewModel.MaxBookPriceText);

            viewModel.MinBookPriceText = "30.50";
            viewModel.MaxBookPriceText = "70.25";
            Assert.Equal(30.5d, viewModel.PriceSliderLowerValue, 2);
            Assert.Equal(70.25d, viewModel.PriceSliderUpperValue, 2);
            int queryCount = queries.Count;
            viewModel.MinBookPriceText = "90";
            Assert.True(viewModel.HasValidationError);
            Assert.Contains("less than or equal", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(viewModel.ApplyFiltersCommand.CanExecute(null));
            Assert.Equal(queryCount, queries.Count);

            viewModel.MinBookPriceText = "60";
            Assert.False(viewModel.HasValidationError);
            Assert.Equal(60d, viewModel.PriceSliderLowerValue, 2);
            Assert.Equal(70.25d, viewModel.PriceSliderUpperValue, 2);
        });
    }

    [Fact]
    public async Task BooksViewModel_ShowsEmptyAndErrorStatesWithoutRetainingOldRows()
    {
        var repository = new Mock<BookRepository>();
        SetupFilterOptions(repository);
        repository.Setup(repo => repo.GetActiveCatalogMetrics()).Returns(new BookCatalogMetrics(0, 0, 0));
        repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>()))
            .Returns(new PagedResult<Book>(Array.Empty<Book>(), 0, 1, 50));
        var viewModel = StaHelper.RunInSta(() => CreateViewModel(repository));

        StaHelper.RunInSta(() =>
        {
            Assert.True(viewModel.IsEmpty);
            Assert.False(viewModel.HasError);

            repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>())).Throws(new InvalidOperationException("query failed"));
            viewModel.SearchText = "unknown";
        });

        await Task.Delay(450);

        StaHelper.RunInSta(() =>
        {
            Assert.Empty(viewModel.Books);
            Assert.True(viewModel.HasError);
            Assert.False(viewModel.IsEmpty);
        });
    }

    [Fact]
    public async Task BooksViewModel_DebouncesRapidSearchAndResetsToFirstPage()
    {
        var (repository, queries) = CreateConcurrentRepository();
        var viewModel = StaHelper.RunInSta(() => CreateViewModel(repository));
        StaHelper.RunInSta(() => viewModel.NextPageCommand.Execute(null));
        Assert.Equal(2, viewModel.PageNumber);
        Assert.Equal(2, queries.Count);

        StaHelper.RunInSta(() =>
        {
            viewModel.SearchText = "c";
            viewModel.SearchText = "cl";
            viewModel.SearchText = "cle";
        });

        Assert.Equal(1, viewModel.PageNumber);
        Assert.Equal(2, queries.Count);
        await Task.Delay(100);
        Assert.Equal(2, queries.Count);

        await Task.Delay(450);

        var completedQueries = queries.ToArray();
        Assert.Equal(3, completedQueries.Length);
        Assert.Equal("cle", completedQueries[^1].SearchText);
        Assert.Equal(1, completedQueries[^1].PageNumber);
    }

    [Fact]
    public async Task BooksViewModel_EmptySearchKeepsAppliedFiltersScopeSortAndPageSize()
    {
        var (repository, queries) = CreateConcurrentRepository();
        var viewModel = StaHelper.RunInSta(() => CreateViewModel(repository));

        StaHelper.RunInSta(() =>
        {
            viewModel.IsFilterPanelOpen = true;
            viewModel.CategoryFilter = "Programming";
            viewModel.ApplyFiltersCommand.Execute(null);
            viewModel.ViewScope = BookStatusFilter.Archived;
            viewModel.SelectedSortOption = viewModel.SortOptions.Single(option => option.SortBy == "CreatedAt");
            viewModel.PageSize = 25;
            viewModel.NextPageCommand.Execute(null);
        });
        Assert.Equal(2, viewModel.PageNumber);
        int queryCount = queries.Count;

        StaHelper.RunInSta(() =>
        {
            viewModel.SearchText = "temporary";
            viewModel.SearchText = "  ";
        });
        Assert.Equal(1, viewModel.PageNumber);
        Assert.Equal(queryCount, queries.Count);

        await Task.Delay(450);

        var query = queries.ToArray()[^1];
        Assert.Null(query.SearchText);
        Assert.Equal("Programming", query.Category);
        Assert.Equal(BookStatusFilter.Archived, query.Status);
        Assert.Equal("CreatedAt", query.SortBy);
        Assert.Equal("DESC", query.SortDirection);
        Assert.Equal(25, query.PageSize);
        Assert.Equal(1, query.PageNumber);
    }

    [Fact]
    public async Task BooksViewModel_ExplicitActionsLoadImmediatelyAndCancelPendingTextSearch()
    {
        var (repository, queries) = CreateConcurrentRepository();
        var viewModel = StaHelper.RunInSta(() => CreateViewModel(repository));

        StaHelper.RunInSta(() =>
        {
            viewModel.SearchText = "before-apply";
            viewModel.IsFilterPanelOpen = true;
            viewModel.CategoryFilter = "Programming";
            viewModel.ApplyFiltersCommand.Execute(null);
            Assert.Equal("before-apply", queries.ToArray()[^1].SearchText);
            Assert.Equal("Programming", queries.ToArray()[^1].Category);

            viewModel.SearchText = "before-clear";
            viewModel.ClearFiltersCommand.Execute(null);
            Assert.Equal("before-clear", queries.ToArray()[^1].SearchText);
            Assert.Equal(BookFilterCodes.All, queries.ToArray()[^1].Category);

            viewModel.SearchText = "before-scope";
            viewModel.ViewScope = BookStatusFilter.Archived;
            Assert.Equal("before-scope", queries.ToArray()[^1].SearchText);
            Assert.Equal(BookStatusFilter.Archived, queries.ToArray()[^1].Status);

            viewModel.SearchText = "before-sort";
            viewModel.SelectedSortOption = viewModel.SortOptions.Single(option => option.SortBy == "CreatedAt");
            Assert.Equal("before-sort", queries.ToArray()[^1].SearchText);
            Assert.Equal("CreatedAt", queries.ToArray()[^1].SortBy);

            viewModel.SearchText = "before-page-size";
            viewModel.PageSize = 25;
            Assert.Equal("before-page-size", queries.ToArray()[^1].SearchText);
            Assert.Equal(25, queries.ToArray()[^1].PageSize);

            viewModel.SearchText = "before-next";
            viewModel.NextPageCommand.Execute(null);
            Assert.Equal("before-next", queries.ToArray()[^1].SearchText);
            Assert.Equal(2, queries.ToArray()[^1].PageNumber);

            viewModel.PreviousPageCommand.Execute(null);
            Assert.Equal(1, queries.ToArray()[^1].PageNumber);
        });

        int immediateQueryCount = queries.Count;
        await Task.Delay(450);
        Assert.Equal(immediateQueryCount, queries.Count);
    }

    private static BooksViewModel CreateViewModel(Mock<BookRepository> repository)
        => new(new BookService(repository.Object, new Mock<BorrowRepository>().Object));

    private static (Mock<BookRepository> Repository, List<BookSearchQuery> Queries) CreateRepository()
    {
        var repository = new Mock<BookRepository>();
        var queries = new List<BookSearchQuery>();
        SetupFilterOptions(repository);
        repository.Setup(repo => repo.GetDistinctAuthors()).Returns(new[]
        {
            "Robert C. Martin", "Martin Fowler", "George R. R. Martin"
        });
        repository.Setup(repo => repo.GetBookPriceRange()).Returns(new BookPriceRange(10m, 100m));
        repository.Setup(repo => repo.GetActiveCatalogMetrics()).Returns(new BookCatalogMetrics(57, 42, 1));
        repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>())).Returns((BookSearchQuery query) =>
        {
            queries.Add(query);
            return new PagedResult<Book>(Array.Empty<Book>(), 120, query.PageNumber, query.PageSize);
        });
        return (repository, queries);
    }

    private static (Mock<BookRepository> Repository, ConcurrentQueue<BookSearchQuery> Queries) CreateConcurrentRepository()
    {
        var repository = new Mock<BookRepository>();
        var queries = new ConcurrentQueue<BookSearchQuery>();
        SetupFilterOptions(repository);
        repository.Setup(repo => repo.GetActiveCatalogMetrics()).Returns(new BookCatalogMetrics(57, 42, 1));
        repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>())).Returns((BookSearchQuery query) =>
        {
            queries.Enqueue(query);
            return new PagedResult<Book>(Array.Empty<Book>(), 120, query.PageNumber, query.PageSize);
        });
        return (repository, queries);
    }

    private static void SetupFilterOptions(Mock<BookRepository> repository)
    {
        repository.Setup(repo => repo.GetCategoryFilterOptions()).Returns(new[]
        {
            new BookFilterOption(BookFilterCodes.All, "All categories"),
            new BookFilterOption(BookFilterCodes.Uncategorized, "Uncategorized"),
            new BookFilterOption("Programming", "Programming")
        });
        repository.Setup(repo => repo.GetPublisherFilterOptions()).Returns(new[]
        {
            new BookFilterOption(BookFilterCodes.All, "All publishers"),
            new BookFilterOption("Prentice Hall", "Prentice Hall")
        });
        repository.Setup(repo => repo.GetDistinctAuthors()).Returns(Array.Empty<string>());
        repository.Setup(repo => repo.GetBookPriceRange()).Returns(new BookPriceRange(null, null));
    }
}
