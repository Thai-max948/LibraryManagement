using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ReaderFinancialStandingEligibilityTests
{
    [Fact]
    public void Provider_UsesBlockingBalanceForPreviewAndTransactionPaths()
    {
        var fees = new Mock<FeeRepository>();
        fees.Setup(repository => repository.GetBlockingOutstandingBalance(7)).Returns(0m);
        fees.Setup(repository => repository.GetBlockingOutstandingBalance(
            It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), 8)).Returns(25m);
        var provider = new FeeFinancialStandingProvider(fees.Object);
        using var connection = new SqlConnection();

        ReaderFinancialStanding preview = provider.GetStanding(7);
        ReaderFinancialStanding transactional = provider.GetStanding(connection, null!, 8);

        Assert.False(preview.BlocksBorrowing);
        Assert.Equal(0m, preview.OutstandingAmount);
        Assert.True(transactional.BlocksBorrowing);
        Assert.Equal(25m, transactional.OutstandingAmount);
        fees.Verify(repository => repository.GetOutstandingBalance(7), Times.Never);
        fees.Verify(repository => repository.GetBlockingOutstandingBalance(7), Times.Once);
        fees.Verify(repository => repository.GetBlockingOutstandingBalance(
            It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), 8), Times.Once);
    }

    [Fact]
    public void CanBorrow_ThreePendingBorrowFeesRemainAllowedUntilBorrowLimit()
    {
        var outstandingFees = new List<(FeeType Type, FeeStatus Status, decimal Amount, decimal PaidAmount)>();
        var fees = new Mock<FeeRepository>();
        fees.Setup(repository => repository.GetBlockingOutstandingBalance(7))
            .Returns(() => outstandingFees
                .Where(fee => FeeFinancialStandingRules.IsBlockingOutstandingFee(
                    fee.Type, fee.Status, fee.Amount, fee.PaidAmount))
                .Sum(fee => fee.Amount - fee.PaidAmount));
        var activeLoans = new List<BorrowRecord>();
        var books = new Mock<BookRepository>();
        var borrows = new Mock<BorrowRepository>();
        var readers = new Mock<ReaderRepository>();
        readers.Setup(repository => repository.GetById(7))
            .Returns(new Reader { ReaderId = 7, Status = "Active" });
        books.Setup(repository => repository.GetById(It.IsAny<int>()))
            .Returns((int bookId) => new Book { BookId = bookId, AvailableQuantity = 1 });
        borrows.Setup(repository => repository.GetEligibilityRecords(7))
            .Returns(() => activeLoans.ToList());
        var service = new BorrowService(books.Object, borrows.Object, readers.Object,
            new BookCopyRepository(), new FeeFinancialStandingProvider(fees.Object));

        for (int attempt = 1; attempt <= ReaderEligibilityService.BorrowLimit + 1; attempt++)
        {
            ReaderEligibilityResult preview = service.GetReaderEligibility(7);
            bool allowed = service.CanBorrow(7, attempt, out string reason);

            if (attempt <= ReaderEligibilityService.BorrowLimit)
            {
                Assert.True(preview.IsEligible, preview.ReasonSummary);
                Assert.True(allowed);
                Assert.Empty(reason);
                activeLoans.Add(new BorrowRecord { ReaderId = 7, BookId = attempt, Status = "Borrowing" });
                outstandingFees.Add((FeeType.Borrow, FeeStatus.Pending, 1m, 0m));
            }
            else
            {
                Assert.False(preview.IsEligible);
                Assert.Contains(preview.Reasons, item => item.Contains("giới hạn", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(preview.Reasons, item => item.Contains("phí", StringComparison.OrdinalIgnoreCase));
                Assert.False(allowed);
                Assert.Contains("giới hạn", reason);
                Assert.DoesNotContain("phí", reason);
            }
        }

        Assert.Equal(3, activeLoans.Count);
        Assert.Equal(3, outstandingFees.Count);
    }

    [Fact]
    public void CanBorrow_PaymentOfBlockingFeeRestoresEligibility()
    {
        var outstandingFees = new List<(FeeType Type, FeeStatus Status, decimal Amount, decimal PaidAmount)>
        {
            (FeeType.Late, FeeStatus.Pending, 10m, 0m)
        };
        var fees = new Mock<FeeRepository>();
        fees.Setup(repository => repository.GetBlockingOutstandingBalance(7))
            .Returns(() => outstandingFees
                .Where(fee => FeeFinancialStandingRules.IsBlockingOutstandingFee(
                    fee.Type, fee.Status, fee.Amount, fee.PaidAmount))
                .Sum(fee => fee.Amount - fee.PaidAmount));
        var books = new Mock<BookRepository>();
        var borrows = new Mock<BorrowRepository>();
        var readers = new Mock<ReaderRepository>();
        readers.Setup(repository => repository.GetById(7))
            .Returns(new Reader { ReaderId = 7, Status = "Active" });
        books.Setup(repository => repository.GetById(1))
            .Returns(new Book { BookId = 1, AvailableQuantity = 1 });
        borrows.Setup(repository => repository.GetEligibilityRecords(7)).Returns([]);
        var service = new BorrowService(books.Object, borrows.Object, readers.Object,
            new BookCopyRepository(), new FeeFinancialStandingProvider(fees.Object));

        Assert.False(service.CanBorrow(7, 1, out string blockedReason));
        Assert.Contains("chưa thanh toán", blockedReason);

        outstandingFees[0] = (FeeType.Late, FeeStatus.Paid, 10m, 10m);

        Assert.True(service.CanBorrow(7, 1, out string clearReason));
        Assert.Empty(clearReason);
    }
}
