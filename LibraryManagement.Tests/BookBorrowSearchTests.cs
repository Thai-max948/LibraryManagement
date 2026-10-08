using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BookBorrowSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SearchForBorrow_EmptyQueryReturnsEmptyWithoutLoadingCatalog(string? query)
    {
        var repository = new Mock<BookRepository>();
        var service = CreateService(repository);

        Assert.Empty(service.SearchForBorrow(query));

        repository.Verify(r => r.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.GetAll(), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_OneCharacterQueryReturnsEmptyWithoutLoadingCatalog()
    {
        var repository = new Mock<BookRepository>();
        var service = CreateService(repository);

        Assert.Empty(service.SearchForBorrow("c"));

        repository.Verify(r => r.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.GetAll(), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_TrimsQueryUsesDefaultLimitAndPreservesRepositoryOrder()
    {
        var repository = new Mock<BookRepository>();
        var expected = new List<BorrowBookSuggestion>
        {
            new(8, "Clean Code", "Robert C. Martin", "Programming", "9780132350884", 2),
            new(3, "Clean Architecture", "Robert C. Martin", "Programming", null, 1)
        };
        repository.Setup(r => r.SearchForBorrow("clean", 10)).Returns(expected);
        var service = CreateService(repository);

        var results = service.SearchForBorrow("  clean  ");

        Assert.Equal(expected.Count, results.Count);
        Assert.Same(expected[0], results[0]);
        Assert.Same(expected[1], results[1]);
        repository.Verify(r => r.SearchForBorrow("clean", 10), Times.Once);
        repository.Verify(r => r.GetAll(), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_ClampsLimitToMaximumOfTwenty()
    {
        var repository = new Mock<BookRepository>();
        repository.Setup(r => r.SearchForBorrow("clean", 20)).Returns(Array.Empty<BorrowBookSuggestion>());
        var service = CreateService(repository);

        service.SearchForBorrow("clean", 5000);

        repository.Verify(r => r.SearchForBorrow("clean", 20), Times.Once);
        repository.Verify(r => r.GetAll(), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_ClampsNonPositiveLimitToOne()
    {
        var repository = new Mock<BookRepository>();
        repository.Setup(r => r.SearchForBorrow("clean", 1)).Returns(Array.Empty<BorrowBookSuggestion>());
        var service = CreateService(repository);

        service.SearchForBorrow("clean", 0);

        repository.Verify(r => r.SearchForBorrow("clean", 1), Times.Once);
    }

    [Fact]
    public void PagedSearchForBorrow_EmptyQueryUsesBoundedDefaultRecommendationPage()
    {
        var repository = new Mock<BookRepository>();
        var expected = new BorrowRecommendationPage<BorrowBookSuggestion>(
            new[] { new BorrowBookSuggestion(5, "Available", "Author", "Category", null, 2) }, true);
        repository.Setup(r => r.SearchForBorrow(string.Empty, 1, 5)).Returns(expected);
        var service = CreateService(repository);

        var result = service.SearchForBorrow(" ", pageNumber: 0, pageSize: 5);

        Assert.Same(expected, result);
        repository.Verify(r => r.SearchForBorrow(string.Empty, 1, 5), Times.Once);
        repository.Verify(r => r.GetAll(), Times.Never);
    }

    [Fact]
    public void PagedSearchForBorrow_ClampsPageSizeAndKeepsShortTextRule()
    {
        var repository = new Mock<BookRepository>();
        repository.Setup(r => r.SearchForBorrow("clean", 2, 20))
            .Returns(new BorrowRecommendationPage<BorrowBookSuggestion>(Array.Empty<BorrowBookSuggestion>(), false));
        var service = CreateService(repository);

        Assert.Empty(service.SearchForBorrow("c", 1, 5).Items);
        service.SearchForBorrow(" clean ", 2, 5000);

        repository.Verify(r => r.SearchForBorrow("c", It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.SearchForBorrow("clean", 2, 20), Times.Once);
    }

    private static BookService CreateService(Mock<BookRepository> repository)
        => new(repository.Object, new Mock<BorrowRepository>().Object);
}
