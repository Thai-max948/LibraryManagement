using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;

namespace LibraryManagement.Tests;

public sealed class BorrowFinancialStandingIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task PendingBorrowFees_DoNotBlockThreeLoans_AndFourthIsBlockedByBorrowLimit()
    {
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Financial standing reader",
            StudentId = "FIN-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });

        int[] copyIds = Enumerable.Range(1, ReaderEligibilityService.BorrowLimit + 1)
            .Select(index =>
            {
                int bookId = new BookService().AddBook(new Book
                {
                    Title = $"Financial standing book {index}",
                    Author = "Test author",
                    PublishYear = 2026,
                    Quantity = 1,
                    ReplacementValue = 100m
                });
                return new BookCopyService().GetCopies(bookId).Single().CopyId;
            })
            .ToArray();

        var service = new BorrowService();
        Assert.True(service.GetReaderEligibility(readerId).IsEligible);

        for (int index = 0; index < ReaderEligibilityService.BorrowLimit; index++)
        {
            ReaderEligibilityResult preview = service.GetReaderEligibility(readerId);
            Assert.True(preview.IsEligible, preview.ReasonSummary);

            int borrowId = service.BorrowBook(readerId, copyIds[index]);
            Fee borrowFee = Assert.Single(await new FeeRepository().GetByBorrowIdAsync(borrowId));
            Assert.Equal(FeeType.Borrow, borrowFee.FeeType);
            Assert.Equal(FeeStatus.Pending, borrowFee.Status);
        }

        ReaderEligibilityResult atLimit = service.GetReaderEligibility(readerId);
        Assert.False(atLimit.IsEligible);
        Assert.Contains(atLimit.Reasons, reason => reason.Contains("giới hạn", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(atLimit.Reasons, reason => reason.Contains("phí", StringComparison.OrdinalIgnoreCase));

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(
            () => service.BorrowBook(readerId, copyIds[ReaderEligibilityService.BorrowLimit]));
        Assert.Contains("giới hạn", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phí", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
