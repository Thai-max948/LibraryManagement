using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;

namespace LibraryManagement.Tests;

public sealed class BorrowCurrentBorrowingRowsTests
{
    [Fact]
    public void GetCurrentBorrowingRows_UsesDefaultLimit()
    {
        var (service, borrowingRepository, _, _) = CreateService();
        borrowingRepository.Setup(repository => repository.GetCurrentBorrowingRows(BorrowService.DefaultCurrentBorrowingRowsLimit))
            .Returns(new List<CurrentBorrowingRow>());

        service.GetCurrentBorrowingRows();

        borrowingRepository.Verify(repository => repository.GetCurrentBorrowingRows(100), Times.Once);
    }

    [Fact]
    public void GetCurrentBorrowingRows_ClampsLimitToMaximum()
    {
        var (service, borrowingRepository, _, _) = CreateService();
        borrowingRepository.Setup(repository => repository.GetCurrentBorrowingRows(BorrowService.MaximumCurrentBorrowingRowsLimit))
            .Returns(new List<CurrentBorrowingRow>());

        service.GetCurrentBorrowingRows(5000);

        borrowingRepository.Verify(repository => repository.GetCurrentBorrowingRows(200), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void GetCurrentBorrowingRows_NormalizesNonPositiveLimit(int requestedLimit)
    {
        var (service, borrowingRepository, _, _) = CreateService();
        borrowingRepository.Setup(repository => repository.GetCurrentBorrowingRows(1))
            .Returns(new List<CurrentBorrowingRow>());

        service.GetCurrentBorrowingRows(requestedLimit);

        borrowingRepository.Verify(repository => repository.GetCurrentBorrowingRows(1), Times.Once);
    }

    [Fact]
    public void GetCurrentBorrowingRows_PreservesRepositoryOrder_AndDoesNotLoadFullCatalogs()
    {
        var (service, borrowingRepository, bookRepository, readerRepository) = CreateService();
        var expected = new List<CurrentBorrowingRow>
        {
            new() { BorrowId = 42, ReaderId = 7, ReaderName = "Reader", BookId = 3, BookTitle = "Recent" },
            new() { BorrowId = 12, ReaderId = 8, ReaderName = "Reader 2", BookId = 4, BookTitle = "Older" }
        };
        borrowingRepository.Setup(repository => repository.GetCurrentBorrowingRows(100)).Returns(expected);

        var actual = service.GetCurrentBorrowingRows();

        Assert.Same(expected, actual);
        Assert.Equal(new[] { 42, 12 }, actual.Select(row => row.BorrowId));
        borrowingRepository.Verify(repository => repository.GetCurrentBorrowingRows(100), Times.Once);
        bookRepository.Verify(repository => repository.GetAll(), Times.Never);
        readerRepository.Verify(repository => repository.GetAll(It.IsAny<bool>()), Times.Never);
    }

    private static (BorrowService Service, Mock<BorrowRepository> BorrowingRepository,
        Mock<BookRepository> BookRepository, Mock<ReaderRepository> ReaderRepository) CreateService()
    {
        var borrowingRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        var readerRepository = new Mock<ReaderRepository>();
        var copyRepository = new Mock<BookCopyRepository>();
        var service = new BorrowService(bookRepository.Object, borrowingRepository.Object,
            readerRepository.Object, copyRepository.Object);
        return (service, borrowingRepository, bookRepository, readerRepository);
    }
}
