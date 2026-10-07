using LibraryManagement.Models;
using LibraryManagement.Services;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BookSearchIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    public BookSearchIntegrationTests(SqlIntegrationFixture _) { }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BookSearch_CombinesFiltersPagingStableSortAndGlobalMetrics()
    {
        var service = new BookService();
        var created = new List<Book>();
        for (int index = 1; index <= 60; index++)
        {
            var book = new Book
            {
                Title = $"Page Book {index:D3}",
                Author = index % 2 == 0 ? "Robert C. Martin" : "Martin Fowler",
                Category = index <= 55 ? "Programming" : "Fiction",
                Language = index % 2 == 0 ? "en" : "vi",
                Publisher = index % 3 == 0 ? "Publisher A" : "Publisher B",
                PublishYear = 1990 + index,
                Quantity = 1,
                ReplacementValue = index % 4 == 1 ? null : index % 2 == 0 ? 10m : 80m
            };
            book.BookId = service.AddBook(book);
            created.Add(book);
        }

        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Paging integration reader",
            StudentId = $"BOOK-PAGE-{Guid.NewGuid():N}",
            Phone = "0901234567"
        });
        var copyId = new BookCopyService().GetCopies(created[1].BookId).Single().CopyId;
        new BorrowService().BorrowBook(readerId, copyId);
        var copies = new BookCopyService();
        copies.RetireCopy(copies.GetCopies(created[59].BookId).Single().CopyId);
        service.ArchiveBook(created[59].BookId);

        var first = service.GetPagedBooks(new BookSearchQuery { PageNumber = 1, PageSize = 25 });
        var second = service.GetPagedBooks(new BookSearchQuery { PageNumber = 2, PageSize = 25 });
        var last = service.GetPagedBooks(new BookSearchQuery { PageNumber = 3, PageSize = 25 });

        Assert.Equal(59, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(2, second.PageNumber);
        Assert.Equal("Page Book 026", second.Items[0].Title);
        Assert.Equal(3, last.PageNumber);
        Assert.Equal(9, last.Items.Count);

        var pagePastEnd = service.GetPagedBooks(new BookSearchQuery { PageNumber = 99, PageSize = 25 });
        Assert.Equal(3, pagePastEnd.PageNumber);
        Assert.Equal(9, pagePastEnd.Items.Count);

        var combined = service.GetPagedBooks(new BookSearchQuery
        {
            SearchText = "Page Book 0",
            Category = "Programming",
            LanguageCode = "en",
            PageSize = 50,
            SortBy = "Title",
            SortDirection = "ASC"
        });
        Assert.Equal(27, combined.TotalCount);
        Assert.All(combined.Items, book =>
        {
            Assert.Equal("Programming", book.Category);
            Assert.Equal("en", book.Language);
            Assert.Equal("Robert C. Martin", book.Author);
        });

        Assert.Equal(59, service.GetPagedBooks(new BookSearchQuery { Status = BookStatusFilter.Active }).TotalCount);
        Assert.Equal(1, service.GetPagedBooks(new BookSearchQuery { Status = BookStatusFilter.Archived }).TotalCount);
        Assert.Equal(60, service.GetPagedBooks(new BookSearchQuery { Status = BookStatusFilter.All }).TotalCount);
        Assert.Equal(29, service.GetPagedBooks(new BookSearchQuery { LanguageCode = "en" }).TotalCount);
        Assert.Equal(19, service.GetPagedBooks(new BookSearchQuery { Publisher = "Publisher A" }).TotalCount);
        Assert.Equal(29, service.GetPagedBooks(new BookSearchQuery { Author = "Robert C. Martin" }).TotalCount);
        Assert.Equal(30, service.GetPagedBooks(new BookSearchQuery { Author = "Martin Fowler" }).TotalCount);
        Assert.Equal(40, service.GetPagedBooks(new BookSearchQuery { PublishYearFrom = 2010 }).TotalCount);
        Assert.Equal(15, service.GetPagedBooks(new BookSearchQuery { PublishYearTo = 2005 }).TotalCount);
        Assert.Equal(11, service.GetPagedBooks(new BookSearchQuery { PublishYearFrom = 2010, PublishYearTo = 2020 }).TotalCount);
        Assert.Equal(15, service.GetPagedBooks(new BookSearchQuery { MinBookPrice = 20m }).TotalCount);
        Assert.Equal(29, service.GetPagedBooks(new BookSearchQuery { MaxBookPrice = 10m }).TotalCount);
        Assert.Equal(29, service.GetPagedBooks(new BookSearchQuery { MinBookPrice = 10m, MaxBookPrice = 10m }).TotalCount);
        Assert.Equal(44, service.GetPagedBooks(new BookSearchQuery { MinBookPrice = 0m }).TotalCount);
        Assert.Equal(new[] { "Publisher A", "Publisher B" }, service.GetPublisherFilterOptions().Skip(1).Select(option => option.Value));
        Assert.Equal(new[] { "Martin Fowler", "Robert C. Martin" }, service.GetDistinctAuthors());
        Assert.Equal(new BookPriceRange(10m, 80m), service.GetBookPriceRange());

        var allFiltersCombined = service.GetPagedBooks(new BookSearchQuery
        {
            SearchText = "Page Book 024",
            Category = "Programming",
            Author = "Robert C. Martin",
            LanguageCode = "en",
            Publisher = "Publisher A",
            PublishYearFrom = 2010,
            PublishYearTo = 2020,
            Status = BookStatusFilter.Active,
            MinBookPrice = 10m,
            MaxBookPrice = 10m
        });
        Assert.Equal(created[23].BookId, Assert.Single(allFiltersCombined.Items).BookId);

        var byYear = service.GetPagedBooks(new BookSearchQuery
        {
            PageSize = 100,
            SortBy = "PublishYear",
            SortDirection = "ASC"
        });
        Assert.Equal(byYear.Items.OrderBy(book => book.PublishYear).ThenBy(book => book.BookId).Select(book => book.BookId),
            byYear.Items.Select(book => book.BookId));

        var byRecent = service.GetPagedBooks(new BookSearchQuery
        {
            PageSize = 100,
            SortBy = "CreatedAt",
            SortDirection = "DESC"
        });
        var expectedRecent = byRecent.Items
            .OrderByDescending(book => book.CreatedAt.HasValue)
            .ThenByDescending(book => book.CreatedAt)
            .ThenByDescending(book => book.BookId)
            .Select(book => book.BookId);
        Assert.Equal(expectedRecent, byRecent.Items.Select(book => book.BookId));

        var empty = service.GetPagedBooks(new BookSearchQuery { SearchText = "No matching book title" });
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(1, empty.TotalPages);

        var metrics = service.GetActiveCatalogMetrics();
        Assert.Equal(59, metrics.TotalBooks);
        Assert.Equal(58, metrics.AvailableBooks);
        Assert.Equal(1, metrics.BorrowedBooks);
    }
}
