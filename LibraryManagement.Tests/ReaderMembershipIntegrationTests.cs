using System;
using System.Linq;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ReaderMembershipIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ExpiredMembership_BlocksBorrowBeforeCopyClaim()
    {
        int readerId = NewReader(DateTime.Today.AddDays(-1));
        int copyId = NewCopy();
        var result = new ReaderEligibilityService().CheckEligibility(readerId);
        Assert.Contains(result.Reasons, reason => reason.Contains("hết hạn"));
        Assert.Throws<BusinessRuleException>(() => new BorrowService().BorrowBook(readerId, copyId));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void MembershipValidThroughExpiryDay_AndNullPreserved()
    {
        int validId = NewReader(DateTime.Today);
        int legacyId = NewReader(null);
        Assert.Equal(DateTime.Today, new ReaderRepository().GetById(validId)!.MembershipExpiresOn);
        Assert.Null(new ReaderRepository().GetById(legacyId)!.MembershipExpiresOn);
        Assert.True(new ReaderEligibilityService().CheckEligibility(validId).IsEligible);
        Assert.True(new ReaderEligibilityService().CheckEligibility(legacyId).IsEligible);
        new BorrowService().BorrowBook(validId, NewCopy());
        new BorrowService().BorrowBook(legacyId, NewCopy());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void StaleMembershipPreview_IsRejectedAtBorrowTime()
    {
        int readerId = NewReader(DateTime.Today.AddDays(30));
        int copyId = NewCopy();
        Assert.True(new ReaderEligibilityService().CheckEligibility(readerId).IsEligible);
        var edited = new ReaderRepository().GetById(readerId)!;
        edited.MembershipExpiresOn = DateTime.Today.AddDays(-1);
        new ReaderService().UpdateReader(edited);
        Assert.Throws<BusinessRuleException>(() => new BorrowService().BorrowBook(readerId, copyId));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void FinancialStandingChangedAfterPreview_IsRejectedInsideTransaction()
    {
        int readerId = NewReader(DateTime.Today.AddDays(30));
        int copyId = NewCopy();
        var financial = new ChangedStandingProvider();
        var service = new BorrowService(new BookRepository(), new BorrowRepository(),
            new ReaderRepository(), new BookCopyRepository(), financial);
        Assert.True(service.GetReaderEligibility(readerId).IsEligible);
        var error = Assert.Throws<BusinessRuleException>(() => service.BorrowBook(readerId, copyId));
        Assert.Contains("phí", error.Message);
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
        Assert.DoesNotContain(new BorrowRepository().GetBorrowingRecords(), record => record.BookCopyId == copyId);
    }

    private static int NewReader(DateTime? expiry) => new ReaderService().AddReader(new Reader
    {
        FullName = "Membership reader", StudentId = $"MEM-{Guid.NewGuid():N}",
        Phone = "0901234567", MembershipExpiresOn = expiry
    });

    private static int NewCopy()
    {
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Membership book", Author = "Test", PublishYear = 2026, Quantity = 1
        });
        return new BookCopyService().GetCopies(bookId).Single().CopyId;
    }

    private static string CopyStatus(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT Status FROM dbo.BookCopies WHERE CopyId = @CopyId", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }

    private sealed class ChangedStandingProvider : IReaderFinancialStandingProvider
    {
        public ReaderFinancialStanding GetStanding(int readerId) => new();
        public ReaderFinancialStanding GetStanding(SqlConnection connection, SqlTransaction transaction, int readerId) =>
            new() { OutstandingAmount = 50000m, BlocksBorrowing = true };
    }
}
