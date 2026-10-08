using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;

namespace LibraryManagement.Tests;

public sealed class ReaderProfileBaselineTests
{
    [Fact]
    public void ProfileWithNoBorrowHistoryHasZeroSummaryAndEmptyRecentHistory()
    {
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        readerRepository.Setup(repository => repository.GetById(17))
            .Returns(new Reader { ReaderId = 17, Status = "Active" });
        borrowRepository.Setup(repository => repository.GetReaderProfileBorrowData(17, It.IsAny<DateTime>(), 5))
            .Returns(new ReaderProfileBorrowData());

        var service = new ReaderService(readerRepository.Object, borrowRepository.Object, bookRepository.Object);

        var profile = service.GetReaderProfile(17);

        Assert.Empty(profile.BorrowingHistory);
        Assert.Equal(0, profile.TotalBorrowed);
        Assert.Equal(0, profile.CurrentlyBorrowing);
        Assert.Equal(0, profile.OverdueCount);
        Assert.True(profile.Eligibility.IsEligible);
        borrowRepository.Verify(repository => repository.GetReaderProfileBorrowData(17, DateTime.Today, 5), Times.Once);
        borrowRepository.Verify(repository => repository.GetHistory(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
        bookRepository.Verify(repository => repository.GetById(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void ProfileCountsAllHistoryButKeepsOnlyFiveRecentItemsAndExcludesReturnedOverdueFromCurrentOverdue()
    {
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        readerRepository.Setup(repository => repository.GetById(18))
            .Returns(new Reader { ReaderId = 18, Status = "Active" });
        var activeLoans = new List<BorrowRecord>
        {
            new() { BorrowId = 1, ReaderId = 18, DueDate = DateTime.Today.AddDays(-1), Status = "Borrowing" },
            new() { BorrowId = 2, ReaderId = 18, DueDate = DateTime.Today.AddDays(-2), Status = "Borrowing" }
        };
        var recentHistory = Enumerable.Range(1, 5)
            .Select(id => new ReaderBorrowHistoryItem
            {
                BorrowId = id,
                BookTitle = $"Book {id}",
                BorrowDate = DateTime.Today.AddDays(-id),
                DueDate = DateTime.Today.AddDays(-id),
                ReturnDate = id == 3 ? DateTime.Today.AddDays(-1) : null,
                Status = id <= 2 ? "Borrowing" : "Returned"
            })
            .ToList();
        var borrowData = new ReaderProfileBorrowData
        {
            RecentHistory = recentHistory,
            EligibilityRecords = activeLoans,
            CurrentlyBorrowing = 2,
            TotalBorrowed = 10,
            OverdueCount = 2
        };
        borrowRepository.Setup(repository => repository.GetReaderProfileBorrowData(18, It.IsAny<DateTime>(), 5))
            .Returns(borrowData);

        var service = new ReaderService(readerRepository.Object, borrowRepository.Object, bookRepository.Object);

        var profile = service.GetReaderProfile(18);

        Assert.Equal(10, profile.TotalBorrowed);
        Assert.Equal(2, profile.CurrentlyBorrowing);
        Assert.Equal(2, profile.OverdueCount);
        Assert.Equal([1, 2, 3, 4, 5], profile.BorrowingHistory.Select(item => item.BorrowId));
        Assert.Same(recentHistory, profile.BorrowingHistory);
        Assert.Equal(2, profile.Eligibility.CurrentLoans);
        Assert.Equal(2, profile.Eligibility.OverdueLoans);
        borrowRepository.Verify(repository => repository.GetReaderProfileBorrowData(18, DateTime.Today, 5), Times.Once);
        borrowRepository.Verify(repository => repository.GetHistory(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
        borrowRepository.Verify(repository => repository.GetEligibilityRecords(18), Times.Never);
        readerRepository.Verify(repository => repository.GetById(18), Times.Once);
        bookRepository.Verify(repository => repository.GetById(It.IsAny<int>()), Times.Never);
        borrowRepository.Verify(repository => repository.GetReaderProfileBorrowData(18, DateTime.Today, 5), Times.Once);
        borrowRepository.Verify(repository => repository.GetHistory(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
        bookRepository.Verify(repository => repository.GetById(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void ProfileUsesProvidedFallbackTitleWithoutBookLookup()
    {
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        readerRepository.Setup(repository => repository.GetById(19))
            .Returns(new Reader { ReaderId = 19, Status = "Active" });
        borrowRepository.Setup(repository => repository.GetReaderProfileBorrowData(19, It.IsAny<DateTime>(), 5))
            .Returns(new ReaderProfileBorrowData
            {
                RecentHistory = [new ReaderBorrowHistoryItem { BorrowId = 21, BookTitle = "Book #404", Status = "Returned" }],
                TotalBorrowed = 1
            });

        var service = new ReaderService(readerRepository.Object, borrowRepository.Object, bookRepository.Object);

        var profile = service.GetReaderProfile(19);

        Assert.Equal("Book #404", Assert.Single(profile.BorrowingHistory).BookTitle);
        bookRepository.Verify(repository => repository.GetById(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void MissingReaderThrowsTheExistingBusinessRuleBeforeLoadingBorrowData()
    {
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        readerRepository.Setup(repository => repository.GetById(20)).Returns((Reader?)null);
        var service = new ReaderService(readerRepository.Object, borrowRepository.Object, bookRepository.Object);

        var exception = Assert.Throws<BusinessRuleException>(() => service.GetReaderProfile(20));

        Assert.Equal("Độc giả không tồn tại.", exception.Message);
        borrowRepository.Verify(repository => repository.GetReaderProfileBorrowData(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void DeletedReaderThrowsTheExistingBusinessRuleBeforeLoadingBorrowData()
    {
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var bookRepository = new Mock<BookRepository>();
        readerRepository.Setup(repository => repository.GetById(21))
            .Returns(new Reader { ReaderId = 21, IsDeleted = true });
        var service = new ReaderService(readerRepository.Object, borrowRepository.Object, bookRepository.Object);

        var exception = Assert.Throws<BusinessRuleException>(() => service.GetReaderProfile(21));

        Assert.Equal("Độc giả không tồn tại.", exception.Message);
        borrowRepository.Verify(repository => repository.GetReaderProfileBorrowData(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }
}
