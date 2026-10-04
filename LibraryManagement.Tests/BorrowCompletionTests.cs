using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;

namespace LibraryManagement.Tests;

public sealed class BorrowCompletionTests
{
    [Fact]
    public void Eligibility_UsesInjectedClockForMembershipBoundary()
    {
        var clock = new FixedClock();
        var service = new ReaderEligibilityService(new Mock<ReaderRepository>().Object,
            new Mock<BorrowRepository>().Object, new NoFinancialStandingProvider(), clock);
        var reader = new Reader { Status = "Active", MembershipExpiresOn = new DateTime(2026, 10, 1) };
        Assert.True(service.Evaluate(reader, Array.Empty<BorrowRecord>()).IsEligible);
        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.False(service.Evaluate(reader, Array.Empty<BorrowRecord>()).IsEligible);
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 1, 23, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void InventoryCheck_DistinguishesCounterDriftFromCirculationMismatch()
    {
        var copies = new[] { new BookCopy { CopyId = 3, BookId = 1, Status = BookCopyStatuses.Borrowed } };
        var loans = new[] { new BorrowRecord { BookId = 1, BookCopyId = 3, Status = "Borrowing" } };
        var row = InventoryCheckRepository.Evaluate(1, "Book", 5, 5, copies, loans);
        Assert.True(row.CounterDrift);
        Assert.Equal(1, row.DerivedQuantity);
        Assert.Equal(0, row.DerivedAvailable);
        Assert.Equal(0, row.InconsistentCopies);
        Assert.Equal(BookCopyStatuses.Borrowed, copies[0].Status);
    }

    [Fact]
    public void InventoryCheck_ReportsCurrentCopyCountsForRequestedStatuses()
    {
        var copies = new[]
        {
            new BookCopy { CopyId = 1, BookId = 1, Status = BookCopyStatuses.Available },
            new BookCopy { CopyId = 2, BookId = 1, Status = BookCopyStatuses.Borrowed },
            new BookCopy { CopyId = 3, BookId = 1, Status = BookCopyStatuses.UnderRepair },
            new BookCopy { CopyId = 4, BookId = 1, Status = BookCopyStatuses.Lost },
            new BookCopy { CopyId = 5, BookId = 1, Status = BookCopyStatuses.Retired },
            new BookCopy { CopyId = 6, BookId = 1, Status = BookCopyStatuses.Damaged }
        };
        var loans = new[] { new BorrowRecord { BookId = 1, BookCopyId = 2, Status = "Borrowing" } };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 5, 1, copies, loans);

        Assert.Equal(6, row.TotalCurrent);
        Assert.Equal(1, row.AvailableCurrent);
        Assert.Equal(1, row.Borrowed);
        Assert.Equal(2, row.DamagedUnderRepair);
        Assert.Equal(1, row.Lost);
        Assert.Equal(1, row.Retired);
    }

    [Fact]
    public void InventoryCheck_ReportsOrphanAndLegacyLoansWithoutAssigningCopies()
    {
        var copies = new[] { new BookCopy { CopyId = 3, BookId = 1, Status = BookCopyStatuses.Available } };
        var loans = new[]
        {
            new BorrowRecord { BookId = 1, BookCopyId = 3, Status = "Borrowing" },
            new BorrowRecord { BookId = 1, BookCopyId = null, Status = "Borrowing" }
        };
        var row = InventoryCheckRepository.Evaluate(1, "Book", 1, 1, copies, loans);
        Assert.False(row.CounterDrift);
        Assert.Equal(1, row.InconsistentCopies);
        Assert.Equal(1, row.UnresolvedLegacyLoans);
        Assert.Null(loans[1].BookCopyId);
        Assert.Equal(BookCopyStatuses.Available, copies[0].Status);
    }

    [Theory]
    [InlineData("Borrowed")]
    [InlineData("UnderRepair")]
    [InlineData("Retired")]
    [InlineData("Lost")]
    [InlineData("Damaged")]
    public void BarcodePreview_RejectsUnavailableStates(string status) =>
        Assert.Contains(status, BookCopyService.GetBorrowBlockReason(new BookCopy { Status = status, Condition = "Good" }));

    [Fact]
    public void BarcodePreview_RejectsUnverifiedEvenIfStatusWasManuallyChanged()
    {
        Assert.Contains("LegacyUnverified", BookCopyService.GetBorrowBlockReason(new BookCopy
        { Status = BookCopyStatuses.Available, Condition = "LegacyUnverified" }));
        Assert.Null(BookCopyService.GetBorrowBlockReason(new BookCopy { Status = BookCopyStatuses.Available, Condition = "Good" }));
    }

    [Fact]
    public void BarcodeLookup_QueriesExactTrimmedBarcodeAndPreservesCopyIdentity()
    {
        var repository = new Mock<BookCopyRepository>();
        var expected = new BookCopy { CopyId = 42, Barcode = "BK-000042", BookId = 10, Status = "Available" };
        repository.Setup(repo => repo.GetByBarcode("BK-000042")).Returns(expected);
        var service = new BookCopyService(repository.Object);
        Assert.Same(expected, service.GetByBarcode("  BK-000042\r\n"));
        Assert.Throws<BusinessRuleException>(() => service.GetByBarcode(" "));
        Assert.Throws<BusinessRuleException>(() => service.GetByBarcode(new string('X', 101)));
        repository.Verify(repo => repo.GetByBarcode("BK-000042"), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public void NewLoan_RejectsRepositoryInsertOutsideTransaction()
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection();
        Assert.Throws<InvalidOperationException>(() => new BorrowRepository().Add(connection, null,
            new BorrowRecord { BookCopyId = 1, Status = "Borrowing" }));
    }
}
