using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;

namespace LibraryManagement.Tests;

public sealed class DueSoonNotificationCheckerTests
{
    private static readonly DateTime Today = new(2026, 10, 4);

    [Theory]
    [InlineData(2, "Borrowing", false, 1)]
    [InlineData(3, "Borrowing", false, 0)]
    [InlineData(1, "Borrowing", false, 0)]
    [InlineData(0, "Borrowing", false, 0)]
    [InlineData(-1, "Borrowing", false, 0)]
    [InlineData(2, "Returned", true, 0)]
    [InlineData(2, "Cancelled", false, 0)]
    public async Task CheckDispatchesOnlyOpenBorrowingsDueOnTargetCalendarDay(int dayOffset, string status, bool returned, int expected)
    {
        var loans = new MemoryLoanRepository();
        loans.Items.Add(new LoanRow(new DueSoonLoan(1, 3, "Clean Code", Today.AddDays(dayOffset).AddHours(23.5)),
            status, returned ? Today : null));
        var events = new RecordingDispatcher();
        var checker = new DueSoonNotificationChecker(loans, events, new FixedTimeProvider(Today.AddHours(21)));

        int count = await checker.CheckAsync();

        Assert.Equal(expected, count);
        Assert.Equal(expected, events.Events.Count);
        Assert.Equal(Today.AddDays(2), loans.LastTargetDate);
    }

    [Fact]
    public async Task CheckTwiceUsesPersistenceIdempotencyToPreventDuplicates()
    {
        var loans = new MemoryLoanRepository();
        loans.Items.Add(new LoanRow(new DueSoonLoan(22, 8, "Book A", Today.AddDays(2).AddHours(8)), "Borrowing", null));
        var service = new NotificationService(new NotificationTests.MemoryRepository());
        var handler = new LoanDueSoonNotificationHandler(service);
        var dispatcher = new NotificationApplicationEventDispatcher(handler);
        var checker = new DueSoonNotificationChecker(loans, dispatcher, new FixedTimeProvider(Today));

        await checker.CheckAsync();
        await checker.CheckAsync();

        var notifications = await service.GetAllAsync();
        Assert.Single(notifications);
        Assert.Equal("DueSoon:22:2026-10-06", notifications[0].IdempotencyKey);
    }

    [Fact]
    public async Task RenewedDueDateCreatesNewReminderAndKeepsOldHistory()
    {
        var loans = new MemoryLoanRepository();
        var row = new LoanRow(new DueSoonLoan(12, 4, "Book B", Today.AddDays(2)), "Borrowing", null);
        loans.Items.Add(row);
        var service = new NotificationService(new NotificationTests.MemoryRepository());
        var dispatcher = new NotificationApplicationEventDispatcher(new LoanDueSoonNotificationHandler(service));
        var clock = new FixedTimeProvider(Today);
        var checker = new DueSoonNotificationChecker(loans, dispatcher, clock);

        await checker.CheckAsync();
        row.Loan = row.Loan with { DueDate = Today.AddDays(9) };
        clock.SetNow(Today.AddDays(7));
        await checker.CheckAsync();

        var notifications = await service.GetAllAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, item => item.IdempotencyKey == "DueSoon:12:2026-10-06");
        Assert.Contains(notifications, item => item.IdempotencyKey == "DueSoon:12:2026-10-13");
    }

    [Fact]
    public async Task MultipleAndMixedLoansOnlyDispatchActiveLoansOnTargetDate()
    {
        var loans = new MemoryLoanRepository();
        loans.Items.Add(new LoanRow(new DueSoonLoan(1, 1, "A", Today.AddDays(2).AddHours(8)), "Borrowing", null));
        loans.Items.Add(new LoanRow(new DueSoonLoan(2, 2, "B", Today.AddDays(2).AddHours(23).AddMinutes(30)), "Borrowing", null));
        loans.Items.Add(new LoanRow(new DueSoonLoan(3, 3, "C", Today.AddDays(2)), "Returned", Today));
        loans.Items.Add(new LoanRow(new DueSoonLoan(4, 4, "D", Today.AddDays(1)), "Borrowing", null));
        loans.Items.Add(new LoanRow(new DueSoonLoan(5, 5, "E", Today.AddDays(3)), "Borrowing", null));
        var events = new RecordingDispatcher();
        var checker = new DueSoonNotificationChecker(loans, events, new FixedTimeProvider(Today));

        int count = await checker.CheckAsync();

        Assert.Equal(2, count);
        Assert.Equal(new[] { 1, 2 }, events.Events.Select(item => item.BorrowId));
    }

    [Fact]
    public async Task CheckerContinuesWhenOneNotificationPublishFails()
    {
        var loans = new MemoryLoanRepository();
        loans.Items.Add(new LoanRow(new DueSoonLoan(1, 1, "A", Today.AddDays(2)), "Borrowing", null));
        loans.Items.Add(new LoanRow(new DueSoonLoan(2, 2, "B", Today.AddDays(2)), "Borrowing", null));
        var events = new RecordingDispatcher { FailBorrowId = 1 };
        var checker = new DueSoonNotificationChecker(loans, events, new FixedTimeProvider(Today));

        int count = await checker.CheckAsync();

        Assert.Equal(1, count);
        Assert.Equal(2, events.Events.Count);
    }

    private sealed class LoanRow(DueSoonLoan loan, string status, DateTime? returnDate)
    {
        public DueSoonLoan Loan { get; set; } = loan;
        public string Status { get; } = status;
        public DateTime? ReturnDate { get; } = returnDate;
    }

    private sealed class MemoryLoanRepository : IDueSoonLoanRepository
    {
        public List<LoanRow> Items { get; } = new();
        public DateTime? LastTargetDate { get; private set; }

        public Task<IReadOnlyList<DueSoonLoan>> GetActiveLoansDueOnAsync(DateTime targetDate, CancellationToken cancellationToken = default)
        {
            LastTargetDate = targetDate.Date;
            IReadOnlyList<DueSoonLoan> result = Items
                .Where(row => row.Status == "Borrowing" && row.ReturnDate == null &&
                    row.Loan.DueDate >= targetDate.Date && row.Loan.DueDate < targetDate.Date.AddDays(1))
                .Select(row => row.Loan).ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingDispatcher : IApplicationEventDispatcher
    {
        public List<LoanDueSoonEvent> Events { get; } = new();
        public int? FailBorrowId { get; init; }

        public Task PublishAsync(LoanDueSoonEvent applicationEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(applicationEvent);
            if (applicationEvent.BorrowId == FailBorrowId) throw new InvalidOperationException("Simulated publish failure");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedTimeProvider(DateTime now) => _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetNow(DateTime now) => _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }
}
