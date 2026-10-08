using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class BorrowBatchIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task ThreeDifferentBookBatch_CreatesOneLoanFeeAndAuditPerCopyWithSharedPolicyDates()
    {
        int readerId = NewReader();
        int[] copyIds = Enumerable.Range(1, 3).Select(index => NewCopy($"Batch success {index}")).ToArray();

        IReadOnlyList<int> borrowIds = new BorrowService().BorrowBooks(readerId, copyIds);

        Assert.Equal(3, borrowIds.Count);
        var records = borrowIds.Select(id => new BorrowRepository().GetById(id)!).ToArray();
        Assert.Equal(copyIds.OrderBy(id => id), records.Select(record => record.BookCopyId!.Value).OrderBy(id => id));
        Assert.Equal(3, records.Select(record => record.BookId).Distinct().Count());
        Assert.All(records, record => Assert.Equal(readerId, record.ReaderId));
        Assert.Single(records.Select(record => record.BorrowDate).Distinct());
        Assert.Single(records.Select(record => record.DueDate).Distinct());
        Assert.Single(records.Select(record => record.LoanPeriodDaysApplied).Distinct());
        Assert.All(copyIds, copyId => Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId)));

        foreach (int borrowId in borrowIds)
        {
            IReadOnlyList<Fee> fees = await new FeeRepository().GetByBorrowIdAsync(borrowId);
            var fee = Assert.Single(fees);
            Assert.Equal(FeeType.Borrow, fee.FeeType);
            Assert.Equal(FeeStatus.Pending, fee.Status);
        }

        var auditEvents = new BorrowService().GetAuditEvents(borrowIds);
        Assert.Equal(3, auditEvents.Count);
        Assert.All(auditEvents, auditEvent => Assert.Equal(CirculationAuditEventType.BorrowCreated, auditEvent.EventType));
        Assert.Equal(borrowIds.OrderBy(id => id), auditEvents.Select(auditEvent => auditEvent.BorrowId).OrderBy(id => id));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task LecturerCanBorrowThreeDifferentBooksWithTwentyEightDaySnapshots()
    {
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Lecturer batch integration reader",
            ReaderType = "Lecturer",
            LecturerCode = "GV-BATCH-" + Guid.NewGuid().ToString("N"),
            Department = "Công nghệ thông tin",
            Phone = "0901234567"
        });
        int[] copyIds = Enumerable.Range(1, 3).Select(index => NewCopy($"Lecturer batch {index}")).ToArray();

        IReadOnlyList<int> borrowIds = new BorrowService().BorrowBooks(readerId, copyIds);

        var records = borrowIds.Select(id => new BorrowRepository().GetById(id)!).ToArray();
        Assert.Equal(3, records.Length);
        Assert.Equal(3, records.Select(record => record.BookId).Distinct().Count());
        Assert.All(records, record =>
        {
            Assert.Equal(28, record.LoanPeriodDaysApplied);
            Assert.Equal(record.BorrowDate.Date.AddDays(28), record.DueDate);
        });
        Assert.All(copyIds, copyId => Assert.Equal(BookCopyStatuses.Borrowed, CopyStatus(copyId)));

        foreach (int borrowId in borrowIds)
            Assert.Equal(FeeType.Borrow, Assert.Single(await new FeeRepository().GetByBorrowIdAsync(borrowId)).FeeType);

        Assert.Equal(3, new BorrowService().GetAuditEvents(borrowIds).Count);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task PendingBorrowFeeDoesNotBlockTwoCopyBatchAndLimitIsRechecked()
    {
        int readerId = NewReader();
        int existingCopyId = NewCopy("Batch pending fee existing");
        int[] batchCopyIds = { NewCopy("Batch pending fee two"), NewCopy("Batch pending fee three") };
        int fourthCopyId = NewCopy("Batch pending fee fourth");
        var service = new BorrowService();

        int existingBorrowId = service.BorrowBook(readerId, existingCopyId);
        var existingFee = Assert.Single(await new FeeRepository().GetByBorrowIdAsync(existingBorrowId));
        Assert.Equal(FeeStatus.Pending, existingFee.Status);

        IReadOnlyList<int> batchBorrowIds = service.BorrowBooks(readerId, batchCopyIds);
        Assert.Equal(2, batchBorrowIds.Count);
        Assert.Equal(3, service.GetReaderEligibility(readerId).CurrentLoans);

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(
            () => service.BorrowBooks(readerId, new[] { fourthCopyId }));
        Assert.Contains("giới hạn", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BookCopyStatuses.Available, CopyStatus(fourthCopyId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void OneCopyBorrowedBeforeConfirmation_RejectsWholeBatchWithoutClaimingOtherCopies()
    {
        int readerId = NewReader();
        int[] availableCopyIds = { NewCopy("Batch stale available one"), NewCopy("Batch stale available two") };
        int unavailableCopyId = NewCopy("Batch stale unavailable");
        new BorrowService().BorrowBook(NewReader(), unavailableCopyId);

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(() =>
            new BorrowService().BorrowBooks(readerId, availableCopyIds.Append(unavailableCopyId).ToArray()));

        Assert.Contains("hiện không thể mượn", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(availableCopyIds, copyId => Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId)));
        Assert.Equal(0, CountForCopies("BorrowRecords", "CopyId", availableCopyIds));
        Assert.Equal(0, CountForCopies("CirculationAuditEvents", "BookCopyId", availableCopyIds));
        Assert.Equal(0, CountForCopies("Fees", "BookCopyId", availableCopyIds));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void FailureDuringSecondAudit_RollsBackEveryLoanCopyFeeAndAuditInBatch()
    {
        int readerId = NewReader();
        int[] copyIds = { NewCopy("Batch rollback one"), NewCopy("Batch rollback two"), NewCopy("Batch rollback three") };
        var service = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new FailingSecondAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: new FeeService());

        Assert.Throws<InvalidOperationException>(() => service.BorrowBooks(readerId, copyIds));

        Assert.All(copyIds, copyId => Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId)));
        Assert.Equal(0, CountForCopies("BorrowRecords", "CopyId", copyIds));
        Assert.Equal(0, CountForCopies("CirculationAuditEvents", "BookCopyId", copyIds));
        Assert.Equal(0, CountForCopies("Fees", "BookCopyId", copyIds));
    }

    private static int NewCopy(string title)
    {
        int bookId = new BookService().AddBook(new Book
        {
            Title = title,
            Author = "Batch test author",
            PublishYear = 2026,
            Quantity = 1,
            ReplacementValue = 100m
        });
        return new BookCopyService().GetCopies(bookId).Single().CopyId;
    }

    private static int NewReader() => new ReaderService().AddReader(new Reader
    {
        FullName = "Batch integration reader",
        StudentId = "BATCH-" + Guid.NewGuid().ToString("N"),
        Phone = "0901234567"
    });

    private static string CopyStatus(int copyId) => new BookCopyRepository().GetById(copyId)!.Status;

    private static int CountForCopies(string table, string column, IReadOnlyList<int> copyIds)
    {
        string[] parameters = Enumerable.Range(0, copyIds.Count).Select(index => "@Copy" + index).ToArray();
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM dbo.{table} WHERE {column} IN ({string.Join(",", parameters)})", connection);
        for (int index = 0; index < copyIds.Count; index++)
            command.Parameters.AddWithValue(parameters[index], copyIds[index]);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class FailingSecondAuditRepository : CirculationAuditRepository
    {
        private int _calls;

        public override void Add(SqlConnection connection, SqlTransaction transaction, CirculationAuditEvent auditEvent)
        {
            if (++_calls == 2)
                throw new InvalidOperationException("Simulated second audit failure");
            base.Add(connection, transaction, auditEvent);
        }
    }
}
