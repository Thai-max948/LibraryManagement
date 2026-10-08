using System.Collections.Generic;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ReaderBorrowSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SearchForBorrow_EmptyQueryReturnsEmptyWithoutLoadingReaders(string? query)
    {
        var repository = new Mock<ReaderRepository>();
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Empty(service.SearchForBorrow(query));

        repository.Verify(r => r.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_OneCharacterTextReturnsEmpty()
    {
        var repository = new Mock<ReaderRepository>();
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Empty(service.SearchForBorrow("a"));

        repository.Verify(r => r.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_TrimsQueryUsesDefaultLimitAndPreservesRepositoryOrder()
    {
        var repository = new Mock<ReaderRepository>();
        var expected = new List<Reader>
        {
            new() { ReaderId = 8, FullName = "Nguyễn Bình" },
            new() { ReaderId = 3, FullName = "Nguyễn An" }
        };
        repository.Setup(r => r.SearchForBorrow("nguyen", 10)).Returns(expected);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        var results = service.SearchForBorrow(" nguyen ");

        Assert.Equal(expected.Count, results.Count);
        Assert.Same(expected[0], results[0]);
        Assert.Same(expected[1], results[1]);
        repository.Verify(r => r.SearchForBorrow("nguyen", 10), Times.Once);
        repository.Verify(r => r.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_OversizedLimitIsClampedToTwenty()
    {
        var repository = new Mock<ReaderRepository>();
        repository.Setup(r => r.SearchForBorrow("an", 20)).Returns(new List<Reader>());
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        service.SearchForBorrow("an", 5000);

        repository.Verify(r => r.SearchForBorrow("an", 20), Times.Once);
        repository.Verify(r => r.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SearchForBorrow_AllowsSingleDigitExactIdentifier()
    {
        var repository = new Mock<ReaderRepository>();
        repository.Setup(r => r.SearchForBorrow("7", 10)).Returns(new List<Reader>());
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        service.SearchForBorrow("7");

        repository.Verify(r => r.SearchForBorrow("7", 10), Times.Once);
    }

    [Fact]
    public void PagedSearchForBorrow_EmptyQueryUsesBoundedDefaultRecommendationPage()
    {
        var repository = new Mock<ReaderRepository>();
        var expected = new BorrowRecommendationPage<Reader>(
            new List<Reader> { new() { ReaderId = 5, FullName = "Active Reader" } }, true);
        repository.Setup(r => r.SearchForBorrow(string.Empty, 1, 5)).Returns(expected);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        var result = service.SearchForBorrow("   ", pageNumber: 0, pageSize: 5);

        Assert.Same(expected, result);
        repository.Verify(r => r.SearchForBorrow(string.Empty, 1, 5), Times.Once);
        repository.Verify(r => r.GetAll(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void PagedSearchForBorrow_PreservesMinimumLengthAndExactNumericIdentifierRules()
    {
        var repository = new Mock<ReaderRepository>();
        var expected = new BorrowRecommendationPage<Reader>(new List<Reader>(), false);
        repository.Setup(r => r.SearchForBorrow("7", 2, 5)).Returns(expected);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        Assert.Empty(service.SearchForBorrow("a", 1, 5).Items);
        var result = service.SearchForBorrow("7", 2, 5);

        Assert.Same(expected, result);
        repository.Verify(r => r.SearchForBorrow("a", It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        repository.Verify(r => r.SearchForBorrow("7", 2, 5), Times.Once);
    }
}
