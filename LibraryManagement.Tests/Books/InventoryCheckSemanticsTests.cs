using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;

namespace LibraryManagement.Tests;

public sealed class InventoryCheckSemanticsTests
{
    [Fact]
    public void EvaluateIncludesBookWithNoCopiesOrLoans()
    {
        var row = InventoryCheckRepository.Evaluate(1, "Empty", 0, 0,
            Array.Empty<BookCopy>(), Array.Empty<BorrowRecord>());

        Assert.Equal(1, row.BookId);
        Assert.Equal("Empty", row.Title);
        Assert.Equal(0, row.TotalCurrent);
        Assert.Equal(0, row.AvailableCurrent);
        Assert.Equal(0, row.UnresolvedLegacyLoans);
        Assert.Equal(0, row.InconsistentCopies);
        Assert.False(row.NeedsReview);
    }

    [Fact]
    public void AvailableCopyWithoutActiveLoanIsConsistent()
    {
        var copies = new[] { Copy(10, 1, BookCopyStatuses.Available) };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 1, 1, copies, Array.Empty<BorrowRecord>());

        Assert.Equal(0, row.InconsistentCopies);
        Assert.Equal(1, row.DerivedQuantity);
        Assert.Equal(1, row.DerivedAvailable);
    }

    [Fact]
    public void BorrowedCopyWithExactlyOneActiveLoanIsConsistent()
    {
        var copies = new[] { Copy(10, 1, BookCopyStatuses.Borrowed) };
        var loans = new[] { Loan(1, 10) };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 1, 0, copies, loans);

        Assert.Equal(0, row.InconsistentCopies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedCopyWithOtherThanOneActiveLoanCountsOneInconsistentCopy(int activeLoanCount)
    {
        var copies = new[] { Copy(10, 1, BookCopyStatuses.Borrowed) };
        var loans = Enumerable.Range(0, activeLoanCount).Select(_ => Loan(1, 10)).ToArray();

        var row = InventoryCheckRepository.Evaluate(1, "Book", 1, 0, copies, loans);

        Assert.Equal(1, row.InconsistentCopies);
    }

    [Fact]
    public void AvailableCopyWithActiveLoanIsInconsistent()
    {
        var copies = new[] { Copy(10, 1, BookCopyStatuses.Available) };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 1, 1, copies, [Loan(1, 10)]);

        Assert.Equal(1, row.InconsistentCopies);
    }

    [Fact]
    public void ActiveLoanWithoutCopyIdIsUnresolvedLegacyLoan()
    {
        var row = InventoryCheckRepository.Evaluate(1, "Book", 0, 0,
            Array.Empty<BookCopy>(), [Loan(1, null)]);

        Assert.Equal(1, row.UnresolvedLegacyLoans);
        Assert.Equal(0, row.InconsistentCopies);
    }

    [Fact]
    public void ActiveLoanReferencingMissingOrOtherBooksCopyIsInconsistent()
    {
        var copies = new[] { Copy(77, 2, BookCopyStatuses.Borrowed) };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 0, 0, copies, [Loan(1, 77)]);

        Assert.Equal(1, row.InconsistentCopies);
    }

    [Fact]
    public void DamagedAndUnderRepairAreCombinedAndRetiredIsExcludedFromDerivedQuantity()
    {
        var copies = new[]
        {
            Copy(10, 1, BookCopyStatuses.Damaged),
            Copy(11, 1, BookCopyStatuses.UnderRepair),
            Copy(12, 1, BookCopyStatuses.Available),
            Copy(13, 1, BookCopyStatuses.Retired)
        };

        var row = InventoryCheckRepository.Evaluate(1, "Book", 3, 1, copies, Array.Empty<BorrowRecord>());

        Assert.Equal(4, row.TotalCurrent);
        Assert.Equal(1, row.AvailableCurrent);
        Assert.Equal(2, row.DamagedUnderRepair);
        Assert.Equal(1, row.Retired);
        Assert.Equal(3, row.DerivedQuantity);
        Assert.Equal(1, row.DerivedAvailable);
    }

    [Fact]
    public void ServiceUsesAggregatedReportOnceAndFiltersInRepositoryOrder()
    {
        var repository = new ReportPathProbeRepository();
        var service = new InventoryCheckService(repository);

        var report = service.Check();

        Assert.Equal(2, report.Count);
        Assert.Same(repository.FirstReviewRow, report[0]);
        Assert.Same(repository.SecondReviewRow, report[1]);
        Assert.Equal(0, repository.LegacyCalls);
        Assert.Equal(1, repository.AggregatedCalls);
    }

    private static BookCopy Copy(int copyId, int bookId, string status) =>
        new() { CopyId = copyId, BookId = bookId, Status = status };

    private static BorrowRecord Loan(int bookId, int? copyId) =>
        new() { BookId = bookId, BookCopyId = copyId, Status = "Borrowing" };

    private sealed class ReportPathProbeRepository : InventoryCheckRepository
    {
        public int LegacyCalls { get; private set; }
        public int AggregatedCalls { get; private set; }
        public InventoryCheckRow FirstReviewRow { get; } = new()
        {
            BookId = 3,
            Title = "First review row",
            StoredQuantity = 1,
            DerivedQuantity = 0
        };
        public InventoryCheckRow CleanRow { get; } = new()
        {
            BookId = 2,
            Title = "No review needed",
            StoredQuantity = 4,
            DerivedQuantity = 4
        };
        public InventoryCheckRow SecondReviewRow { get; } = new()
        {
            BookId = 1,
            Title = "Second review row",
            StoredQuantity = 0,
            DerivedQuantity = 1
        };

        public override List<InventoryCheckRow> GetReport()
        {
            LegacyCalls++;
            return [];
        }

        public override List<InventoryCheckRow> GetAggregatedReport()
        {
            AggregatedCalls++;
            return [FirstReviewRow, CleanRow, SecondReviewRow];
        }
    }
}
