using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;

namespace LibraryManagement.Tests;

public sealed class BorrowCurrentBorrowingPageServiceTests
{
    [Fact]
    public void GetCurrentBorrowingPageForwardsFixedPageQueryToRepository()
    {
        var repository = new Mock<BorrowRepository>();
        var query = new CurrentBorrowingPageQuery(2);
        var expected = new CurrentBorrowingPage(
            Array.Empty<CurrentBorrowingRow>(), 2, CurrentBorrowingPageQuery.FixedPageSize, 12);
        repository.Setup(repo => repo.GetCurrentBorrowingPage(query)).Returns(expected);
        var service = new BorrowService(new BookRepository(), repository.Object,
            new ReaderRepository(), new BookCopyRepository());

        var actual = service.GetCurrentBorrowingPage(query);

        Assert.Same(expected, actual);
        repository.Verify(repo => repo.GetCurrentBorrowingPage(query), Times.Once);
    }
}
