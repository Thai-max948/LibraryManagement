using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;

namespace LibraryManagement.Tests;

public sealed class ReturnActiveLoanSearchTests
{
    [Fact]
    public void SearchActiveLoansForReturn_UsesDefaultBoundedLimitAndPreservesRepositoryOrder()
    {
        var context = CreateContext();
        var expected = new List<ActiveReturnLoanRow>
        {
            CreateRow(21, "Recent"),
            CreateRow(12, "Older")
        };
        context.BorrowRepository.Setup(repository => repository.SearchActiveLoansForReturn("", 100))
            .Returns(expected);

        var actual = context.Service.SearchActiveLoansForReturn(null);

        Assert.Same(expected, actual);
        Assert.Equal(new[] { 21, 12 }, actual.Select(row => row.BorrowId));
        context.BorrowRepository.Verify(repository => repository.SearchActiveLoansForReturn("", 100), Times.Once);
        VerifyNoFullCatalogReads(context);
    }

    [Fact]
    public void SearchActiveLoansForReturn_TrimsSearchTextBeforeCallingRepository()
    {
        var context = CreateContext();
        context.BorrowRepository.Setup(repository => repository.SearchActiveLoansForReturn("Clean Code", 100))
            .Returns(new List<ActiveReturnLoanRow>());

        context.Service.SearchActiveLoansForReturn("  Clean Code  ");

        context.BorrowRepository.Verify(repository => repository.SearchActiveLoansForReturn("Clean Code", 100), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchActiveLoansForReturn_AcceptsEmptySearchAsBoundedRecentList(string? searchText)
    {
        var context = CreateContext();
        context.BorrowRepository.Setup(repository => repository.SearchActiveLoansForReturn("", 100))
            .Returns(new List<ActiveReturnLoanRow>());

        context.Service.SearchActiveLoansForReturn(searchText);

        context.BorrowRepository.Verify(repository => repository.SearchActiveLoansForReturn("", 100), Times.Once);
    }

    [Fact]
    public void SearchActiveLoansForReturn_ClampsRequestedLimitToHardMaximum()
    {
        var context = CreateContext();
        context.BorrowRepository.Setup(repository => repository.SearchActiveLoansForReturn("barcode", 200))
            .Returns(new List<ActiveReturnLoanRow>());

        context.Service.SearchActiveLoansForReturn("barcode", 1000);

        context.BorrowRepository.Verify(repository => repository.SearchActiveLoansForReturn("barcode", 200), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-8)]
    public void SearchActiveLoansForReturn_NormalizesNonPositiveLimit(int requestedLimit)
    {
        var context = CreateContext();
        context.BorrowRepository.Setup(repository => repository.SearchActiveLoansForReturn("", 1))
            .Returns(new List<ActiveReturnLoanRow>());

        context.Service.SearchActiveLoansForReturn(null, requestedLimit);

        context.BorrowRepository.Verify(repository => repository.SearchActiveLoansForReturn("", 1), Times.Once);
    }

    private static ActiveReturnLoanRow CreateRow(int borrowId, string bookTitle) => new()
    {
        BorrowId = borrowId,
        ReaderId = borrowId + 1,
        ReaderName = "Reader",
        BookId = 3,
        BookTitle = bookTitle,
        BookCopyId = borrowId,
        Barcode = $"BK-{borrowId:D6}",
        BorrowDate = new DateTime(2026, 10, 7).AddDays(-borrowId),
        DueDate = new DateTime(2026, 10, 21),
    };

    private static TestContext CreateContext()
    {
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        var readerRepository = new Mock<ReaderRepository>();
        var service = new BorrowService(bookRepository.Object, borrowRepository.Object,
            readerRepository.Object, new Mock<BookCopyRepository>().Object);
        return new TestContext(service, borrowRepository, bookRepository, readerRepository);
    }

    private static void VerifyNoFullCatalogReads(TestContext context)
    {
        context.BorrowRepository.Verify(repository => repository.GetAll(), Times.Never);
        context.BorrowRepository.Verify(repository => repository.GetBorrowingRecords(), Times.Never);
        context.BookRepository.Verify(repository => repository.GetAll(), Times.Never);
        context.ReaderRepository.Verify(repository => repository.GetAll(It.IsAny<bool>()), Times.Never);
    }

    private sealed record TestContext(BorrowService Service,
        Mock<BorrowRepository> BorrowRepository,
        Mock<BookRepository> BookRepository,
        Mock<ReaderRepository> ReaderRepository);
}
