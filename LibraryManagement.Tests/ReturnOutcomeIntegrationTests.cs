using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ReturnOutcomeIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task NormalReturn_ReleasesCopyAndStoresOutcome()
    {
        var (loanId, copyId, bookId, _) = NewLoan();
        var service = new BorrowService();
        ReturnResult result = service.ReturnBook(loanId, ReturnCondition.Normal);
        Assert.Equal(0, result.LateDays);
        Assert.DoesNotContain(await new FeeService().GetFeesByBorrowAsync(loanId), fee => fee.FeeType == FeeType.Late);
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Returned", loan.Status);
        Assert.Equal("Normal", loan.ReturnCondition);
        Assert.NotNull(loan.ReturnDate);
        Assert.Null(loan.LostDate);
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Throws<BusinessRuleException>(() => service.ReturnBook(loanId, ReturnCondition.Normal));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Migration_DoesNotInventConditionForHistoricalReturns()
    {
        var historical = new BorrowRepository().GetById(1)!;
        Assert.Equal("Returned", historical.Status);
        DateTime? returnDate = historical.ReturnDate;
        ReturnOutcomeMigration.Apply();
        var after = new BorrowRepository().GetById(1)!;
        Assert.Equal(returnDate, after.ReturnDate);
        Assert.Null(after.ReturnCondition);
        Assert.Null(after.LostDate);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void DamagedReturn_QuarantinesCopyAndPreservesCondition()
    {
        var (loanId, copyId, bookId, _) = NewLoan();
        var service = new BorrowService();
        service.ReturnBook(loanId, ReturnCondition.Damaged, "Rách trang 120");
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Returned", loan.Status);
        Assert.Equal("Damaged", loan.ReturnCondition);
        Assert.Equal("Rách trang 120", loan.ConditionNote);
        Assert.NotNull(loan.ReturnDate);
        Assert.Equal(BookCopyStatuses.Damaged, CopyStatus(copyId));
        Assert.Equal(0, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Throws<BusinessRuleException>(() => service.ReturnBook(loanId, ReturnCondition.Normal));
        var copies = new BookCopyService();
        copies.ChangeStatus(copyId, BookCopyStatuses.UnderRepair);
        copies.ChangeStatus(copyId, BookCopyStatuses.Available);
        Assert.Equal("Damaged", new BorrowRepository().GetById(loanId)!.ReturnCondition);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Lost_HasNoPhysicalReturnAndBlocksReader()
    {
        var (loanId, copyId, bookId, readerId) = NewLoan(replacementValue: 100m);
        var service = new BorrowService();
        service.MarkAsLost(loanId, "Độc giả báo thất lạc");
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Lost", loan.Status);
        Assert.NotNull(loan.LostDate);
        Assert.Equal("Độc giả báo thất lạc", loan.LostNote);
        Assert.Null(loan.ReturnDate);
        Assert.Null(loan.ReturnCondition);
        Assert.Equal(BookCopyStatuses.Lost, CopyStatus(copyId));
        Assert.Equal(0, new BookRepository().GetById(bookId)!.AvailableQuantity);
        var eligibility = service.GetReaderEligibility(readerId);
        Assert.False(eligibility.IsEligible);
        Assert.Contains(eligibility.Reasons, reason => reason.Contains("chưa thanh toán"));
        Assert.Throws<BusinessRuleException>(() => service.ReturnBook(loanId, ReturnCondition.Normal));
        Assert.Throws<BusinessRuleException>(() => service.MarkAsLost(loanId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void CopyTransitionFailure_RollsBackLoanOutcome()
    {
        var (loanId, copyId, _, _) = NewLoan();
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(
                "UPDATE dbo.BookCopies SET Status = 'Available' WHERE CopyId = @CopyId", connection);
            command.Parameters.AddWithValue("@CopyId", copyId);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Throws<BusinessRuleException>(() => new BorrowService().ReturnBook(loanId, ReturnCondition.Normal));
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.ReturnDate);
        Assert.Null(loan.ReturnCondition);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentReturns_ExactlyOneSucceeds()
    {
        var (loanId, copyId, _, _) = NewLoan();
        Task<Exception?> Attempt() => Task.Run(() =>
        {
            try { new BorrowService().ReturnBook(loanId, ReturnCondition.Normal); return null; }
            catch (Exception error) { return error; }
        });
        var results = await Task.WhenAll(Attempt(), Attempt());
        Assert.Single(results, error => error == null);
        Assert.Single(results.OfType<BusinessRuleException>());
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
        Assert.Equal("Returned", new BorrowRepository().GetById(loanId)!.Status);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void LostCopyTransitionFailure_RollsBackLoanOutcome()
    {
        var (loanId, copyId, _, _) = NewLoan();
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(
                "UPDATE dbo.BookCopies SET Status = 'Available' WHERE CopyId = @CopyId", connection);
            command.Parameters.AddWithValue("@CopyId", copyId);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Throws<BusinessRuleException>(() => new BorrowService().MarkAsLost(loanId, "Không tìm thấy"));
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.LostDate);
        Assert.Null(loan.LostNote);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void NeedsRepair_PersistsDispositionAuditAndFeeResult()
    {
        var (loanId, copyId, bookId, readerId) = NewLoan();
        var result = new BorrowService().ReturnBook(loanId, ReturnCondition.NeedsRepair, "Cần đóng lại gáy");
        Assert.Equal(ReturnCondition.NeedsRepair, result.Disposition);
        Assert.True(result.RequiresFeeProcessing);
        Assert.Equal(readerId, result.ReaderId);
        Assert.Equal(copyId, result.BookCopyId);
        Assert.Equal("NeedsRepair", new BorrowRepository().GetById(loanId)!.ReturnCondition);
        Assert.Equal(BookCopyStatuses.UnderRepair, CopyStatus(copyId));
        Assert.Equal(0, new BookRepository().GetById(bookId)!.AvailableQuantity);
        var audit = new BorrowService().GetAuditEvents(new[] { loanId });
        var returned = Assert.Single(audit, x => x.EventType == CirculationAuditEventType.ReturnedNeedsRepair);
        Assert.Equal(1, returned.ActorUserId);
        Assert.Equal(copyId, returned.BookCopyId);
        Assert.Equal("Cần đóng lại gáy", returned.Note);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task OverdueReturn_SucceedsAndProducesFiveDaysForFee()
    {
        var (loanId, copyId, _, _) = NewLoan();
        using (var conn = Database.GetConnection())
        {
            conn.Open();
            using var command = new SqlCommand("UPDATE dbo.BorrowRecords SET BorrowDate = @Borrowed, DueDate = @Due WHERE BorrowId = @Id", conn);
            command.Parameters.AddWithValue("@Borrowed", DateTime.Today.AddDays(-20));
            command.Parameters.AddWithValue("@Due", DateTime.Today.AddDays(-5));
            command.Parameters.AddWithValue("@Id", loanId);
            command.ExecuteNonQuery();
        }
        var result = new BorrowService().ReturnBook(loanId, ReturnCondition.Normal);
        Assert.Equal(5, result.LateDays);
        Assert.True(result.IsOverdue);
        Assert.True(result.RequiresFeeProcessing);
        Assert.Contains(result.FeeWarnings, warning => warning.Contains("Replacement Value", StringComparison.Ordinal));
        Assert.DoesNotContain(await new FeeService().GetFeesByBorrowAsync(loanId), fee => fee.FeeType == FeeType.Late);
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BarcodeReturn_RevalidatesWholeExactBarcodeInsideTransaction()
    {
        var (loanId, copyId, bookId, readerId) = NewLoan();
        string barcode = new BookCopyService().GetCopies(bookId).Single().Barcode;
        var service = new BorrowService();
        Assert.Throws<BusinessRuleException>(() => service.ReturnBookByBarcode(barcode.ToLowerInvariant(), ReturnCondition.Normal));
        Assert.Throws<BusinessRuleException>(() => service.ReturnBookByBarcode(barcode + " ", ReturnCondition.Normal));
        var result = service.ReturnBookByBarcode(barcode, ReturnCondition.Normal);
        Assert.Equal(loanId, result.BorrowId);
        Assert.Equal(readerId, result.ReaderId);
        Assert.Equal(bookId, result.BookId);
        Assert.Equal(copyId, result.BookCopyId);
        Assert.Equal("Returned", new BorrowRepository().GetById(loanId)!.Status);
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
        Assert.Throws<BusinessRuleException>(() => service.ReturnBookByBarcode(barcode, ReturnCondition.Normal));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Return_RejectsPersistedLoanBookCopyMismatchWithoutMutation()
    {
        var (loanId, copyId, _, _) = NewLoan();
        int otherBookId = new BookService().AddBook(new Book
        {
            Title = "Unrelated return book", Author = "Test", PublishYear = 2026, Quantity = 1
        });
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand("UPDATE dbo.BorrowRecords SET BookId = @OtherBookId WHERE BorrowId = @BorrowId", connection);
            command.Parameters.AddWithValue("@OtherBookId", otherBookId);
            command.Parameters.AddWithValue("@BorrowId", loanId);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Throws<BusinessRuleException>(() => new BorrowService().ReturnBook(loanId, ReturnCondition.Normal));
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.ReturnDate);
        Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BarcodeLookup_RejectsPrefixUnknownAndNonActiveCopy()
    {
        var (loanId, copyId, bookId, _) = NewLoan();
        string barcode = new BookCopyService().GetCopies(bookId).Single().Barcode;
        var service = new BorrowService();
        Assert.Equal(loanId, service.FindActiveReturnByBarcode(barcode).BorrowId);
        Assert.Equal(copyId, service.FindActiveReturnByBarcode(barcode).BookCopyId);
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(barcode[..^1]));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(barcode.ToLowerInvariant()));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(barcode + " "));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode("' OR 1=1 --"));
        service.ReturnBook(loanId, ReturnCondition.Normal);
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(barcode));
        Assert.Throws<BusinessRuleException>(() => service.ReturnBook(int.MaxValue, ReturnCondition.Normal));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task TwoCopiesForSameReader_ReturnConcurrentlyWithoutReaderWideUpdateLock()
    {
        var (firstLoan, firstCopy, bookId, readerId) = NewLoan();
        int secondCopy = new BookCopyService().AddCopy(bookId);
        int secondLoan = new BorrowService().BorrowBook(readerId, secondCopy);
        using var start = new System.Threading.Barrier(2);
        Task<Exception?> Attempt(int loan) => Task.Run(() =>
        {
            start.SignalAndWait();
            try { new BorrowService().ReturnBook(loan, ReturnCondition.Normal); return null; }
            catch (Exception error) { return error; }
        });
        var results = await Task.WhenAll(Attempt(firstLoan), Attempt(secondLoan));
        Assert.All(results, error => Assert.Null(error));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(firstCopy));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(secondCopy));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void TwoCopiesOfSameBook_ReturnIndependently()
    {
        var (loanId, copyId, bookId, readerId) = NewLoan();
        int secondId = new BookCopyService().AddCopy(bookId);
        int secondLoan = new BorrowService().BorrowBook(readerId, secondId);
        new BorrowService().ReturnBook(loanId, ReturnCondition.Damaged, "Rách bìa");
        Assert.Equal("Borrowing", new BorrowRepository().GetById(secondLoan)!.Status);
        Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(secondId));
        new BorrowService().ReturnBook(secondLoan, ReturnCondition.Normal);
        Assert.Equal(BookCopyStatuses.Damaged, CopyStatus(copyId));
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(secondId));
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ConditionalCopyUpdateFailure_RollsBackLoanAndAudit()
    {
        var (loanId, copyId, _, _) = NewLoan();
        var service = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(), new RejectReturnCopyRepository());
        Assert.Throws<BusinessRuleException>(() => service.ReturnBook(loanId, ReturnCondition.Normal));
        Assert.Equal("Borrowing", new BorrowRepository().GetById(loanId)!.Status);
        Assert.Null(new BorrowRepository().GetById(loanId)!.ReturnDate);
        Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId));
        Assert.Single(new BorrowService().GetAuditEvents(new[] { loanId }));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void AuditFailure_RollsBackLoanAndCopyTogether()
    {
        var (loanId, copyId, _, _) = NewLoan();
        var service = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new NoFinancialStandingProvider(), new RejectReturnAuditRepository(), new AuthServiceCurrentUserContext());
        Assert.Throws<InvalidOperationException>(() => service.ReturnBook(loanId, ReturnCondition.Normal));
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.ReturnDate);
        Assert.Null(loan.ReturnCondition);
        Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId));
        Assert.Single(new BorrowService().GetAuditEvents(new[] { loanId }));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void UpgradeOldOutcomeConstraints_AllowsNeedsRepairAndPreservesHistory()
    {
        var (loanId, _, _, _) = NewLoan();
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CK_BorrowRecords_ReturnCondition;
                ALTER TABLE dbo.BorrowRecords WITH NOCHECK ADD CONSTRAINT CK_BorrowRecords_ReturnCondition
                    CHECK (ReturnCondition IS NULL OR ReturnCondition IN ('Normal', 'Damaged'));
                ALTER TABLE dbo.CirculationAuditEvents DROP CONSTRAINT CK_CirculationAudit_EventType;
                ALTER TABLE dbo.CirculationAuditEvents WITH NOCHECK ADD CONSTRAINT CK_CirculationAudit_EventType
                    CHECK (EventType IN ('BorrowCreated','LegacyCopyMapped','ReturnedNormal','ReturnedDamaged','MarkedLost'));", connection);
            command.ExecuteNonQuery();
        }
        new BorrowService().ReturnBook(loanId, ReturnCondition.NeedsRepair, "Cần sửa bìa");
        Assert.Equal("NeedsRepair", new BorrowRepository().GetById(loanId)!.ReturnCondition);
        Assert.Null(new BorrowRepository().GetById(1)!.ReturnCondition);
        ReturnOutcomeMigration.Apply();
        CirculationAuditMigration.Apply();
        Assert.Equal("NeedsRepair", new BorrowRepository().GetById(loanId)!.ReturnCondition);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void UpgradeOldAuditConstraint_AllowsMarkedLost()
    {
        var (loanId, copyId, _, _) = NewLoan(replacementValue: 100m);
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                ALTER TABLE dbo.CirculationAuditEvents DROP CONSTRAINT CK_CirculationAudit_EventType;
                ALTER TABLE dbo.CirculationAuditEvents WITH NOCHECK ADD CONSTRAINT CK_CirculationAudit_EventType
                    CHECK (EventType IN ('BorrowCreated','LegacyCopyMapped','Returned','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair'));", connection);
            command.ExecuteNonQuery();
        }

        new BorrowService().MarkAsLost(loanId, "Không tìm thấy");

        Assert.Equal("Lost", new BorrowRepository().GetById(loanId)!.Status);
        Assert.Equal(BookCopyStatuses.Lost, CopyStatus(copyId));
        Assert.Single(new BorrowService().GetAuditEvents(new[] { loanId }),
            auditEvent => auditEvent.EventType == CirculationAuditEventType.MarkedLost);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void UpgradeOldBookCopyStatusConstraint_AllowsLost()
    {
        var (loanId, copyId, _, _) = NewLoan(replacementValue: 100m);
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                ALTER TABLE dbo.BookCopies DROP CONSTRAINT CK_BookCopies_Status;
                ALTER TABLE dbo.BookCopies WITH NOCHECK ADD CONSTRAINT CK_BookCopies_Status
                    CHECK (Status IN ('Available','Borrowed','Damaged','UnderRepair','Retired'));", connection);
            command.ExecuteNonQuery();
        }

        BookCopyMigration.Apply();
        new BorrowService().MarkAsLost(loanId, "Không tìm thấy");

        Assert.Equal("Lost", new BorrowRepository().GetById(loanId)!.Status);
        Assert.Equal(BookCopyStatuses.Lost, CopyStatus(copyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void UpgradeOldBorrowStatusConstraint_AllowsLost()
    {
        var (loanId, _, _, _) = NewLoan(replacementValue: 100m);
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using var command = new SqlCommand(@"
                ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CHK_BorrowRecords_Status;
                ALTER TABLE dbo.BorrowRecords WITH NOCHECK ADD CONSTRAINT CHK_BorrowRecords_Status
                    CHECK (Status IN ('Borrowing','Returned','Overdue'));", connection);
            command.ExecuteNonQuery();
        }

        new BorrowService().MarkAsLost(loanId, "Không tìm thấy");

        Assert.Equal("Lost", new BorrowRepository().GetById(loanId)!.Status);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentReturnAndLost_ExactlyOneOutcomeCommits()
    {
        var (loanId, copyId, _, _) = NewLoan();
        using var start = new System.Threading.Barrier(2);
        Task<Exception?> Attempt(ReturnCondition condition) => Task.Run(() =>
        {
            start.SignalAndWait();
            try { new BorrowService().ReturnBook(loanId, condition); return null; }
            catch (Exception error) { return error; }
        });
        var results = await Task.WhenAll(Attempt(ReturnCondition.Normal), Attempt(ReturnCondition.Lost));
        Assert.Single(results, error => error == null);
        Assert.Single(results.OfType<BusinessRuleException>());
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal(loan.Status == "Lost" ? BookCopyStatuses.Lost : BookCopyStatuses.Available, CopyStatus(copyId));
        Assert.Equal(2, new BorrowService().GetAuditEvents(new[] { loanId }).Count);
    }

    private sealed class RejectReturnAuditRepository : CirculationAuditRepository
    {
        public override void Add(SqlConnection connection, SqlTransaction transaction, CirculationAuditEvent auditEvent) =>
            throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed class RejectReturnCopyRepository : BookCopyRepository
    {
        public override bool UpdateStatus(SqlConnection connection, SqlTransaction? transaction, int copyId, string expectedStatus, string newStatus) => false;
    }

    private static (int LoanId, int CopyId, int BookId, int ReaderId) NewLoan(decimal? replacementValue = null)
    {
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Return outcome reader", StudentId = $"RET-{Guid.NewGuid():N}", Phone = "0901234567"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Return outcome book", Author = "Test", PublishYear = 2026, Quantity = 1,
            ReplacementValue = replacementValue
        });
        int copyId = new BookCopyService().GetCopies(bookId).Single().CopyId;
        int loanId = new BorrowService().BorrowBook(readerId, copyId);
        return (loanId, copyId, bookId, readerId);
    }

    private static string CopyStatus(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT Status FROM dbo.BookCopies WHERE CopyId = @CopyId", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }
}
