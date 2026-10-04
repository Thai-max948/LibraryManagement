using LibraryManagement.Models;
using LibraryManagement.Data;
using LibraryManagement.Services;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Tests;

public sealed class FeeFoundationTests
{
    [Fact]
    public void BookServiceRejectsNegativeReplacementValueBeforePersistence()
    {
        var book = new Book { Title = "Test", Author = "Author", PublishYear = 2020,
            Quantity = 0, ReplacementValue = -1m };
        Assert.Throws<BusinessRuleException>(() => new BookService().AddBook(book));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    [InlineData(1.001, 0)]
    public void InvalidAmountsAreRejected(decimal amount, decimal paid) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeRules.DetermineStatus(amount, paid));

    [Theory]
    [InlineData(10, 0, FeeStatus.Pending)]
    [InlineData(10, 5, FeeStatus.Partial)]
    [InlineData(10, 10, FeeStatus.Paid)]
    [InlineData(0, 0, FeeStatus.Paid)]
    public void StatusMatchesAmounts(decimal amount, decimal paid, FeeStatus expected) =>
        Assert.Equal(expected, FeeRules.DetermineStatus(amount, paid));

    [Fact]
    public void NewFeeCannotHaveZeroAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeRules.ValidateNewAmount(0m));
        Assert.Equal(FeeStatus.Pending, FeeRules.ValidateNewAmount(0.01m));
    }

    [Fact]
    public void LateFeeUsesDecimalRateCapAndCentRounding()
    {
        var policy = new FeePolicy(LateFeeRatePerDay: 0.01m, MaxLateFeePercent: 0.03m);
        Assert.Equal(30m, FeeCalculator.CalculateLate(1000m, 10, policy));
        Assert.Equal(10m, FeeCalculator.CalculateLate(1000m, 1, policy));
        Assert.Equal(6000m, FeeCalculator.CalculateLate(300000m, 1,
            new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1.00m)));
        FeeCalculationSnapshot capped = FeeCalculator.CalculateLateSnapshot(300000m, 60,
            new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1.00m));
        Assert.Equal(360000m, capped.UncappedAmount);
        Assert.Equal(300000m, capped.CapAmount);
        Assert.Equal(300000m, capped.FinalAmount);
        var rounding = new FeePolicy(LateFeeRatePerDay: 0.005m, MaxLateFeePercent: 1m);
        Assert.Equal(0.01m, FeeCalculator.CalculateLate(1m, 1, rounding));
    }

    [Fact]
    public void LateDaysUseCalendarDatesAndIgnoreTimeOfDayAcrossBoundaries()
    {
        Assert.Equal(0, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 1, 23, 59, 0)));
        Assert.Equal(1, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 2, 0, 1, 0)));
        Assert.Equal(5, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 6, 23, 59, 0)));
        Assert.Equal(1, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 1, 31), new DateTime(2026, 2, 1)));
        Assert.Equal(1, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 12, 31), new DateTime(2027, 1, 1)));
        Assert.Equal(0, FeeCalculator.CalculateLateDays(
            new DateTime(2026, 10, 8), new DateTime(2026, 10, 7, 23, 59, 0)));
    }

    [Fact]
    public void LateCalculationRoundsOnlyAfterComparingRawAmountWithRawCap()
    {
        FeeCalculationSnapshot snapshot = FeeCalculator.CalculateLateSnapshot(199999.99m, 5,
            new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1m));

        Assert.Equal(199999.99m, snapshot.BaseAmount);
        Assert.Equal(0.02m, snapshot.AppliedRate);
        Assert.Equal(5m, snapshot.Units);
        Assert.Equal(199999.99m, snapshot.CapAmount);
        Assert.Equal(20000.00m, snapshot.UncappedAmount);
        Assert.Equal(20000.00m, snapshot.FinalAmount);
    }

    [Fact]
    public void CalculationSnapshotCapturesInputsAndFinalAmountWithoutRepricingHistory()
    {
        FeeCalculationSnapshot damage = FeeCalculator.CalculateDamageSnapshot(300000m, major: true,
            new FeePolicy(MajorDamageRate: 0.30m));
        Assert.Equal(300000m, damage.BaseAmount);
        Assert.Equal(0.30m, damage.AppliedRate);
        Assert.Equal(90000m, damage.UncappedAmount);
        Assert.Equal(90000m, damage.FinalAmount);

        FeeCalculationSnapshot late = FeeCalculator.CalculateLateSnapshot(300000m, 5,
            new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1m));
        Assert.Equal(5m, late.Units);
        Assert.Equal(300000m, late.CapAmount);
        Assert.Equal(30000m, late.UncappedAmount);
        Assert.Equal(30000m, late.FinalAmount);

        var historical = new Fee(1, 1, 1, null, FeeType.Damage, damage.FinalAmount, 0m,
            FeeStatus.Pending, "Damage", null, "Return", "87", "Reader", "Book", null,
            300000m, DateTime.UtcNow, null, null, null, AppliedRate: damage.AppliedRate,
            BaseAmount: damage.BaseAmount, UncappedAmount: damage.UncappedAmount);
        var historicalLate = new Fee(2, 1, 1, null, FeeType.Late, late.FinalAmount, 0m,
            FeeStatus.Pending, "Late", null, "Return", "88", "Reader", "Book", null,
            300000m, DateTime.UtcNow, null, null, null, LateDays: 5, AppliedRate: late.AppliedRate,
            AppliedCapRate: 1m, BaseAmount: late.BaseAmount, Units: late.Units,
            CapAmount: late.CapAmount, UncappedAmount: late.UncappedAmount,
            DueDateSnapshot: new DateTime(2026, 10, 1), ResolvedAtSnapshot: new DateTime(2026, 10, 6));
        _ = FeeCalculator.CalculateDamageSnapshot(300000m, major: true, new FeePolicy(MajorDamageRate: 0.40m));
        _ = FeeCalculator.CalculateLateSnapshot(300000m, 5,
            new FeePolicy(LateFeeRatePerDay: 0.01m, MaxLateFeePercent: 0.50m));
        Assert.Equal(0.30m, historical.CalculationSnapshot!.AppliedRate);
        Assert.Equal(90000m, historical.CalculationSnapshot.FinalAmount);
        Assert.Equal(0.02m, historicalLate.CalculationSnapshot!.AppliedRate);
        Assert.Equal(30000m, historicalLate.Amount);
        Assert.Equal(new DateTime(2026, 10, 1), historicalLate.DueDateSnapshot);
        Assert.Equal(new DateTime(2026, 10, 6), historicalLate.ResolvedAtSnapshot);
    }

    [Fact]
    public void StateRulesPermitOnlyForwardPaymentAndTerminalActions()
    {
        var pending = TestFee(FeeStatus.Pending, 0m);
        var partial = TestFee(FeeStatus.Partial, 40000m);
        var paid = TestFee(FeeStatus.Paid, 100000m);
        var waived = TestFee(FeeStatus.Waived, 40000m) with { WaivedAmount = 60000m };
        var cancelled = TestFee(FeeStatus.Cancelled, 0m);

        Assert.True(FeeStateRules.CanRecordPayment(pending));
        Assert.True(FeeStateRules.CanRecordPayment(partial));
        Assert.False(FeeStateRules.CanRecordPayment(paid));
        Assert.False(FeeStateRules.CanRecordPayment(waived));
        Assert.False(FeeStateRules.CanRecordPayment(cancelled));
        Assert.True(FeeStateRules.CanWaive(pending));
        Assert.True(FeeStateRules.CanWaive(partial));
        Assert.False(FeeStateRules.CanWaive(paid));
        Assert.True(FeeStateRules.CanCancel(pending));
        Assert.False(FeeStateRules.CanCancel(partial));
        Assert.False(FeeStateRules.CanCancel(paid));
        Assert.Equal(100000m, waived.Amount);
        Assert.Equal(40000m, waived.PaidAmount);
        Assert.Equal(60000m, waived.WaivedAmount);
        Assert.Equal(0m, waived.Remaining);
    }

    [Fact]
    public void PaymentAttemptReusesKeyForRetryAndStartsNewOperationAfterSuccess()
    {
        var attempt = new FeePaymentAttemptKey();
        Guid first = attempt.GetOrCreate(9, 40m, "cash");
        Assert.Equal(first, attempt.GetOrCreate(9, 40m, "cash"));
        Assert.NotEqual(first, attempt.GetOrCreate(9, 41m, "cash"));
        attempt.Complete();
        Assert.NotEqual(first, attempt.GetOrCreate(9, 40m, "cash"));
    }

    private static Fee TestFee(FeeStatus status, decimal paidAmount) => new(
        1, 1, 1, null, FeeType.Other, 100000m, paidAmount, status, "Test", null,
        "Test", "1", "Reader", "Book", null, null, DateTime.UtcNow, null, null, null);

    [Fact]
    public void DamageLostReplacementAndRenewalUseConfiguredRates()
    {
        var policy = new FeePolicy(MinorDamageRate: 0.10m, MajorDamageRate: 0.30m,
            LostRate: 0.50m, ReplacementRate: 0.60m, RenewalFeeRate: 0.20m);
        Assert.Equal(20m, FeeCalculator.CalculateDamage(200m, major: false, policy));
        Assert.Equal(60m, FeeCalculator.CalculateDamage(200m, major: true, policy));
        Assert.Equal(100m, FeeCalculator.CalculateLost(200m, policy));
        Assert.Equal(120m, FeeCalculator.CalculateReplacement(200m, policy));
        Assert.Equal(2m, FeeCalculator.CalculateRenewal(10m, policy));
    }

    [Fact]
    public void ProductionDamagePolicyUsesTwentyAndFiftyPercentOfReplacementValue()
    {
        FeePolicy policy = new ConfiguredFeePolicyProvider().GetCurrent();
        Assert.Equal(0.20m, policy.MinorDamageRate);
        Assert.Equal(0.50m, policy.MajorDamageRate);

        FeeCalculationSnapshot minor = FeeCalculator.CalculateDamageSnapshot(300000m, major: false, policy);
        Assert.Equal(60000m, minor.FinalAmount);
        Assert.Equal(300000m, minor.BaseAmount);
        Assert.Equal(0.20m, minor.AppliedRate);
        Assert.Equal(60000m, minor.UncappedAmount);

        FeeCalculationSnapshot major = FeeCalculator.CalculateDamageSnapshot(300000m, major: true, policy);
        Assert.Equal(150000m, major.FinalAmount);
        Assert.Equal(0.50m, major.AppliedRate);
    }

    [Theory]
    [InlineData(300000)]
    [InlineData(450000)]
    public void ProductionReplacementPolicyCompensatesCurrentBookValueAtOneHundredPercent(decimal replacementValue)
    {
        FeePolicy policy = new ConfiguredFeePolicyProvider().GetCurrent();
        Assert.Equal(1.00m, policy.ReplacementRate);

        FeeCalculationSnapshot snapshot = FeeCalculator.CalculateReplacementSnapshot(replacementValue, policy);
        Assert.Equal(replacementValue, snapshot.BaseAmount);
        Assert.Equal(1.00m, snapshot.AppliedRate);
        Assert.Equal(1m, snapshot.Units);
        Assert.Equal(replacementValue, snapshot.UncappedAmount);
        Assert.Equal(replacementValue, snapshot.FinalAmount);
    }

    [Fact]
    public void MissingReplacementRateFailsClosedWithPolicyName()
    {
        FeePolicyNotConfiguredException error = Assert.Throws<FeePolicyNotConfiguredException>(
            () => FeeCalculator.CalculateReplacementSnapshot(300000m, new FeePolicy()));
        Assert.Contains("FeePolicy.ReplacementRate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroReplacementValueProducesZeroAmountAndSettledStatus()
    {
        FeeCalculationSnapshot snapshot = FeeCalculator.CalculateReplacementSnapshot(0m,
            new FeePolicy(ReplacementRate: 1m));
        Assert.Equal(0m, snapshot.FinalAmount);
        Assert.Equal(FeeStatus.Paid, FeeRules.DetermineStatus(snapshot.FinalAmount, 0m));
    }

    [Fact]
    public void MissingRatesBlockAssessmentAndNoLateDaysNeedsNoRate()
    {
        FeePolicyNotConfiguredException minor = Assert.Throws<FeePolicyNotConfiguredException>(
            () => FeeCalculator.CalculateDamage(100m, false, new FeePolicy()));
        Assert.Contains("FeePolicy.MinorDamageRate", minor.Message, StringComparison.Ordinal);
        FeePolicyNotConfiguredException major = Assert.Throws<FeePolicyNotConfiguredException>(
            () => FeeCalculator.CalculateDamage(100m, true, new FeePolicy()));
        Assert.Contains("FeePolicy.MajorDamageRate", major.Message, StringComparison.Ordinal);
        Assert.Equal(0m, FeeCalculator.CalculateLate(100m, 0, new FeePolicy()));
        Assert.Throws<BusinessRuleException>(() => FeeCalculator.CalculateLate(100m, -1, new FeePolicy()));
    }

    [Theory]
    [InlineData(1.01)]
    [InlineData(-0.01)]
    [InlineData(0.1234567)]
    public void InvalidPolicyRatesAreRejected(decimal rate) =>
        Assert.Throws<BusinessRuleException>(() => FeeCalculator.CalculateDamage(100m, false,
            new FeePolicy(MinorDamageRate: rate)));

    [Fact]
    public void TerminalFeesHaveNoOutstandingBalance()
    {
        var template = new Fee(1, 1, 1, null, FeeType.Other, 10m, 2m, FeeStatus.Partial,
            "reason", null, "test", "1", "Reader", "Book", null, null,
            DateTime.UtcNow, null, null, null);
        Assert.Equal(8m, template.Remaining);
        Assert.Equal(0m, (template with { Status = FeeStatus.Waived }).Remaining);
        Assert.Equal(0m, (template with { Status = FeeStatus.Cancelled }).Remaining);
    }
}

public sealed class FeeSqlIntegrationTests
{
    [IntegrationFact]
    public void StartupMigrationAddsBookValueBeforeFeeSchemaAndIsIdempotent()
    {
        using var fixture = new SqlIntegrationFixture();
        using var connection = Database.GetConnection();
        connection.Open();
        using (var legacy = new SqlCommand(@"
            ALTER TABLE dbo.Books DROP CONSTRAINT CK_Books_RentalPrice;
            ALTER TABLE dbo.Books DROP COLUMN RentalPrice;
            ALTER TABLE dbo.Books DROP CONSTRAINT CK_Books_ReplacementValue;
            ALTER TABLE dbo.Books DROP COLUMN ReplacementValue;", connection))
            legacy.ExecuteNonQuery();
        using (var before = new SqlCommand("SELECT COUNT(*) FROM dbo.Books", connection))
        {
            int count = Convert.ToInt32(before.ExecuteScalar());
            Assert.True(count > 0);
            LibraryDatabaseStartupMigration.Apply();
            LibraryDatabaseStartupMigration.Apply();
            Assert.True(BookValueMigration.HasSchema(connection));
            Assert.True(BookValueMigration.HasRentalPriceSchema(connection));
            using var feeTable = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.Fees', 'U') IS NULL THEN 0 ELSE 1 END", connection);
            Assert.Equal(1, Convert.ToInt32(feeTable.ExecuteScalar()));
            using var replacementValueQuery = new SqlCommand("SELECT TOP (1) ReplacementValue FROM dbo.Books", connection);
            _ = replacementValueQuery.ExecuteScalar();
            using var after = new SqlCommand("SELECT COUNT(*) FROM dbo.Books WHERE ReplacementValue IS NULL", connection);
            Assert.Equal(count, Convert.ToInt32(after.ExecuteScalar()));
            using var rentals = new SqlCommand("SELECT COUNT(*) FROM dbo.Books WHERE RentalPrice IS NULL", connection);
            Assert.Equal(count, Convert.ToInt32(rentals.ExecuteScalar()));
        }
    }

    [IntegrationFact]
    public async Task FeeMigrationAddsLateDateSnapshotsToExistingSchemaIdempotently()
    {
        using var fixture = new SqlIntegrationFixture();
        await FeeMigration.ApplyAsync();
        await FeeMigration.ApplyAsync();
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var drop = new SqlCommand(
                "ALTER TABLE dbo.Fees DROP COLUMN DueDateSnapshot, ResolvedAtSnapshot", connection);
            await drop.ExecuteNonQueryAsync();
        }

        await FeeMigration.ApplyAsync();
        await FeeMigration.ApplyAsync();
        await using var verifyConnection = Database.GetConnection();
        await verifyConnection.OpenAsync();
        await using var verify = new SqlCommand(@"SELECT
            CASE WHEN COL_LENGTH('dbo.Fees', 'DueDateSnapshot') IS NULL THEN 0 ELSE 1 END,
            CASE WHEN COL_LENGTH('dbo.Fees', 'ResolvedAtSnapshot') IS NULL THEN 0 ELSE 1 END", verifyConnection);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    [IntegrationFact]
    public async Task MigrationCreationSnapshotsDuplicateAndOutstanding()
    {
        using var fixture = new SqlIntegrationFixture();
        await FeeMigration.ApplyAsync();
        await FeeMigration.ApplyAsync();
        var bookService = new BookService();
        int newBookId = bookService.AddBook(new Book { Title = "Priced test book", Author = "Author",
            Category = "Test", PublishYear = 2020, Quantity = 0, ReplacementValue = 150000m });
        var editable = bookService.GetBookById(newBookId)!;
        Assert.Equal(150000m, editable.ReplacementValue);
        editable.ReplacementValue = 180000m;
        bookService.UpdateBook(editable);
        Assert.Equal(180000m, bookService.GetBookById(newBookId)!.ReplacementValue);
        var service = new FeeService();
        int borrowId;
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT TOP (1) BorrowId FROM dbo.BorrowRecords ORDER BY BorrowId", connection);
            borrowId = Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        var request = new CreateFeeRequest(borrowId, FeeType.Other, 12.34m, "Foundation test", "FeeTest",
            Guid.NewGuid().ToString("N"), AppliedCapRate: 0.25m);
        int bookId;
        int copyId;
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT BookId FROM dbo.BorrowRecords WHERE BorrowId = @Id", connection);
            command.Parameters.AddWithValue("@Id", borrowId);
            bookId = Convert.ToInt32(await command.ExecuteScalarAsync());
            await using var copyCommand = new SqlCommand("SELECT TOP (1) CopyId FROM dbo.BookCopies WHERE BookId = @BookId ORDER BY CopyId", connection);
            copyCommand.Parameters.AddWithValue("@BookId", bookId);
            copyId = Convert.ToInt32(await copyCommand.ExecuteScalarAsync());
            await using var link = new SqlCommand("UPDATE dbo.BorrowRecords SET CopyId=@CopyId WHERE BorrowId=@BorrowId", connection);
            link.Parameters.AddWithValue("@CopyId", copyId);
            link.Parameters.AddWithValue("@BorrowId", borrowId);
            await link.ExecuteNonQueryAsync();
        }
        var books = new BookRepository();
        var book = books.GetById(bookId)!;
        string originalTitle = book.Title;
        book.ReplacementValue = 150000m;
        Assert.True(books.Update(book));
        Assert.Equal(150000m, books.GetById(bookId)!.ReplacementValue);
        Fee created = await service.CreateFeeAsync(request);
        Fee loaded = (await service.GetFeeAsync(created.FeeId))!;
        Assert.Equal(12.34m, loaded.Amount);
        Assert.Equal(FeeType.Other, loaded.FeeType);
        Assert.Equal(FeeStatus.Pending, loaded.Status);
        Assert.Equal(created.ReaderNameSnapshot, loaded.ReaderNameSnapshot);
        Assert.Equal(created.BookTitleSnapshot, loaded.BookTitleSnapshot);
        Assert.Equal(created.BarcodeSnapshot, loaded.BarcodeSnapshot);
        Assert.Equal(created.CreatedAt, loaded.CreatedAt);
        Assert.Equal(150000m, loaded.BookPriceSnapshot);
        Assert.Equal(0.25m, loaded.AppliedCapRate);
        Assert.Equal(12.34m, loaded.BaseAmount);
        Assert.Equal(12.34m, loaded.UncappedAmount);
        Assert.Equal(12.34m, loaded.CalculationSnapshot!.FinalAmount);
        Assert.NotNull(loaded.BarcodeSnapshot);
        Assert.Equal(12.34m, await service.GetOutstandingBalanceAsync(loaded.ReaderId));
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() => service.CreateFeeAsync(request));
        book.ReplacementValue = 180000m;
        book.Title = "Renamed after fee";
        Assert.True(books.Update(book));
        Assert.Equal(180000m, books.GetById(bookId)!.ReplacementValue);
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("UPDATE dbo.BookCopies SET Barcode=@Barcode WHERE CopyId=@CopyId", connection);
            command.Parameters.AddWithValue("@Barcode", "CHANGED-" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("@CopyId", copyId);
            await command.ExecuteNonQueryAsync();
        }
        Fee future = await service.CreateFeeAsync(request with { SourceId = Guid.NewGuid().ToString("N") });
        Assert.Equal(180000m, future.BookPriceSnapshot);
        Assert.Equal(150000m, (await service.GetFeeAsync(created.FeeId))!.BookPriceSnapshot);
        Assert.Equal(originalTitle, (await service.GetFeeAsync(created.FeeId))!.BookTitleSnapshot);
        Assert.NotEqual((await service.GetFeeAsync(future.FeeId))!.BarcodeSnapshot, loaded.BarcodeSnapshot);
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("UPDATE dbo.Readers SET FullName = 'Changed test reader' WHERE ReaderId = @Id", connection);
            command.Parameters.AddWithValue("@Id", loaded.ReaderId);
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(created.ReaderNameSnapshot, (await service.GetFeeAsync(created.FeeId))!.ReaderNameSnapshot);
    }

    [IntegrationFact]
    public async Task PaymentsAreIdempotentAndWaiveCancelPreserveAudit()
    {
        using var fixture = new SqlIntegrationFixture();
        await FeeMigration.ApplyAsync();
        var service = new FeeService();
        int borrowId;
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT TOP (1) BorrowId FROM dbo.BorrowRecords ORDER BY BorrowId", connection);
            borrowId = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        Fee fee = await service.CreateFeeAsync(new CreateFeeRequest(borrowId, FeeType.Other, 15.75m,
            "Payment test", "PaymentTest", Guid.NewGuid().ToString("N")));
        Guid key = Guid.NewGuid();
        var paymentRequest = new RecordFeePaymentRequest(fee.FeeId, 5.25m, key, "cash desk");
        FeePayment payment = await service.RecordPaymentAsync(paymentRequest);
        FeePayment retry = await service.RecordPaymentAsync(paymentRequest);
        Assert.Equal(payment.PaymentId, retry.PaymentId);
        Assert.Single(await service.GetPaymentHistoryAsync(fee.FeeId));
        Assert.Equal(FeeStatus.Partial, (await service.GetFeeAsync(fee.FeeId))!.Status);
        Assert.Equal(10.50m, await service.GetOutstandingBalanceAsync(fee.ReaderId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            new RecordFeePaymentRequest(fee.FeeId, 10.51m, Guid.NewGuid())));
        await Assert.ThrowsAsync<FeePaymentIdempotencyConflictException>(() => service.RecordPaymentAsync(
            paymentRequest with { Amount = 5.24m }));

        await service.RecordPaymentAsync(new RecordFeePaymentRequest(fee.FeeId, 10.50m, Guid.NewGuid()));
        Fee paid = (await service.GetFeeAsync(fee.FeeId))!;
        Assert.Equal(FeeStatus.Paid, paid.Status);
        Assert.Equal(paid.Amount, paid.PaidAmount);
        Assert.NotNull(paid.PaidAt);
        Assert.Equal(0m, await service.GetOutstandingBalanceAsync(fee.ReaderId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.WaiveAsync(paid.FeeId, "Paid fee"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(paid.FeeId, "Paid fee"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            new RecordFeePaymentRequest(paid.FeeId, 1m, Guid.NewGuid())));

        Fee partial = await service.CreateFeeAsync(new CreateFeeRequest(borrowId, FeeType.Other, 8m,
            "Waive test", "PaymentTest", Guid.NewGuid().ToString("N")));
        await service.RecordPaymentAsync(new RecordFeePaymentRequest(partial.FeeId, 2m, Guid.NewGuid()));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(partial.FeeId, "Cannot cancel collected amount"));
        Fee waived = await service.WaiveAsync(partial.FeeId, "Approved by librarian");
        Assert.Equal(FeeStatus.Waived, waived.Status);
        Assert.Equal(8m, waived.Amount);
        Assert.Equal(2m, waived.PaidAmount);
        Assert.Equal(6m, waived.WaivedAmount);
        Assert.Equal("Approved by librarian", waived.WaiveReason);
        Assert.NotNull(waived.WaivedAt);
        Assert.Single(await service.GetPaymentHistoryAsync(partial.FeeId));
        Assert.Equal(0m, await service.GetOutstandingBalanceAsync(waived.ReaderId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            new RecordFeePaymentRequest(waived.FeeId, 1m, Guid.NewGuid())));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(waived.FeeId, "Cannot cancel waived fee"));

        Fee pending = await service.CreateFeeAsync(new CreateFeeRequest(borrowId, FeeType.Other, 3m,
            "Cancel test", "PaymentTest", Guid.NewGuid().ToString("N")));
        Fee cancelled = await service.CancelAsync(pending.FeeId, "Entered against wrong event");
        Assert.Equal(FeeStatus.Cancelled, cancelled.Status);
        Assert.Equal("Entered against wrong event", cancelled.CancelReason);
        Assert.NotNull(cancelled.CancelledAt);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(cancelled.FeeId, "Already cancelled"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.WaiveAsync(cancelled.FeeId, "Already cancelled"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordPaymentAsync(
            new RecordFeePaymentRequest(cancelled.FeeId, 1m, Guid.NewGuid())));
    }

    [IntegrationFact]
    public async Task BorrowUsesPersistedRentalPriceAndReturnReportsMissingFeePolicy()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy()));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Fee integration book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 100000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Fee integration reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });

        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        Fee borrowFee = Assert.Single(await new FeeService().GetFeesByBorrowAsync(borrowId));
        Assert.Equal(FeeType.Borrow, borrowFee.FeeType);
        Assert.Equal(5000m, borrowFee.Amount);
        Assert.Equal(5000m, borrowFee.RentalPriceSnapshot);
        Assert.Equal(FeeStatus.Pending, borrowFee.Status);

        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(@"UPDATE dbo.BorrowRecords
                SET BorrowDate=DATEADD(day,-10,GETDATE()), DueDate=DATEADD(day,-2,GETDATE())
                WHERE BorrowId=@Id", connection);
            command.Parameters.AddWithValue("@Id", borrowId);
            await command.ExecuteNonQueryAsync();
        }

        ReturnResult returned = circulation.ReturnBook(borrowId, ReturnCondition.Normal);
        Assert.Equal(2, returned.LateDays);
        Assert.True(returned.IsOverdue);
        Assert.Contains(returned.FeeWarnings, warning => warning.Contains("FeePolicy.LateFeeRatePerDay", StringComparison.Ordinal));
        Assert.Single(await new FeeService().GetFeesByBorrowAsync(borrowId));
    }

    [IntegrationFact]
    public async Task CancelledSourceFeeCanBeCorrectedAndMultipleTypesShareOneReturnSource()
    {
        using var fixture = new SqlIntegrationFixture();
        await FeeMigration.ApplyAsync();
        int borrowId = await GetAnyBorrowIdAsync();
        await SetBorrowBookValueAsync(borrowId, 300000m);
        var service = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1m,
                MajorDamageRate: 0.30m)));
        string returnEventId = "return-event-" + Guid.NewGuid().ToString("N");

        Fee late = (await service.CreateLateFeeAsync(borrowId, 5, "Return", returnEventId))!;
        Fee damage = (await service.CreateDamageFeeAsync(borrowId, true, "Needs repair", "Return", returnEventId))!;
        Assert.Equal(late.SourceId, damage.SourceId);
        Assert.NotEqual(late.FeeType, damage.FeeType);
        Assert.Equal(300000m, damage.BaseAmount);
        Assert.Equal(0.30m, damage.AppliedRate);
        Assert.Equal(90000m, damage.Amount);
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            service.CreateLateFeeAsync(borrowId, 5, "Return", returnEventId));
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            service.CreateDamageFeeAsync(borrowId, true, "Needs repair", "Return", returnEventId));

        Fee cancelled = await service.CancelAsync(damage.FeeId, "Correcting assessment");
        Fee corrected = (await service.CreateDamageFeeAsync(borrowId, true, "Corrected assessment", "Return", returnEventId))!;
        Assert.Equal(FeeStatus.Cancelled, cancelled.Status);
        Assert.Equal(FeeStatus.Pending, corrected.Status);
        IReadOnlyList<Fee> history = await service.GetFeesByBorrowAsync(borrowId);
        Assert.Contains(history, fee => fee.FeeId == cancelled.FeeId && fee.Status == FeeStatus.Cancelled);
        Assert.Contains(history, fee => fee.FeeId == corrected.FeeId && fee.Status == FeeStatus.Pending);

        await using var connection = Database.GetConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(@"SELECT has_filter, filter_definition FROM sys.indexes
            WHERE object_id=OBJECT_ID('dbo.Fees') AND name='UX_Fees_Source'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Contains("Status", reader.GetString(1), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5", reader.GetString(1));
    }

    [IntegrationFact]
    public async Task ReturnUsesReturnAuditIdentityAndCurrentBookValueForAllReturnFees()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1m,
                MajorDamageRate: 0.50m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Fee return snapshot book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Fee return reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });
        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        Fee borrowFee = Assert.Single(await feeService.GetFeesByBorrowAsync(borrowId), fee => fee.FeeType == FeeType.Borrow);
        Assert.Equal("Borrow", borrowFee.SourceType);
        Assert.Equal(borrowId.ToString(System.Globalization.CultureInfo.InvariantCulture), borrowFee.SourceId);

        Book currentBook = new BookService().GetBookById(bookId)!;
        currentBook.ReplacementValue = 350000m;
        new BookService().UpdateBook(currentBook);
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var dueDate = new SqlCommand(@"UPDATE dbo.BorrowRecords
                SET BorrowDate=DATEADD(day,-15,GETDATE()), DueDate=DATEADD(day,-5,GETDATE())
                WHERE BorrowId=@Id", connection);
            dueDate.Parameters.AddWithValue("@Id", borrowId);
            await dueDate.ExecuteNonQueryAsync();
        }
        DateTime dueDateSnapshot = new BorrowRepository().GetById(borrowId)!.DueDate;

        ReturnResult result = circulation.ReturnBook(borrowId, ReturnCondition.NeedsRepair, "Major damage");
        Assert.True(result.LateDays > 0);
        Assert.True(result.ReturnEventId is > 0);
        Assert.Equal(2, result.FeesCreated);
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var audit = new SqlCommand(@"SELECT COUNT(*) FROM dbo.CirculationAuditEvents
                WHERE AuditEventId=@EventId AND BorrowId=@BorrowId AND EventType='ReturnedNeedsRepair'", connection);
            audit.Parameters.AddWithValue("@EventId", result.ReturnEventId!.Value);
            audit.Parameters.AddWithValue("@BorrowId", borrowId);
            Assert.Equal(1, Convert.ToInt32(await audit.ExecuteScalarAsync()));
        }
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Fee late = Assert.Single(fees, fee => fee.FeeType == FeeType.Late);
        Fee damage = Assert.Single(fees, fee => fee.FeeType == FeeType.Damage);
        Assert.Equal("Return", late.SourceType);
        Assert.Equal(late.SourceId, damage.SourceId);
        Assert.Equal(result.ReturnEventId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), late.SourceId);
        Assert.Equal(350000m, damage.BookPriceSnapshot);
        Assert.Equal(350000m, damage.BaseAmount);
        Assert.Equal("Major", damage.DamageLevel);
        Assert.Equal(0.50m, damage.AppliedRate);
        Assert.Equal(175000m, damage.Amount);
        Assert.Equal(350000m, late.BaseAmount);
        Assert.Equal(result.LateDays, late.LateDays);
        Assert.Equal((decimal)result.LateDays!.Value, late.Units);
        Assert.Equal(dueDateSnapshot, late.DueDateSnapshot);
        Assert.Equal(result.ResolvedAt, late.ResolvedAtSnapshot);
        Assert.Equal(350000m, late.CapAmount);
        Assert.Equal(Math.Round(350000m * 0.02m * result.LateDays.Value, 2, MidpointRounding.AwayFromZero), late.Amount);
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            feeService.CreateLateFeeAsync(borrowId, result.LateDays.Value, "Return", late.SourceId));
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            feeService.CreateDamageFeeAsync(borrowId, true, "Major damage", "Return", damage.SourceId));
    }

    [IntegrationFact]
    public async Task DamagedReturnCreatesSeparateMinorFeeFromCurrentReplacementValueAndKeepsSnapshot()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(MinorDamageRate: 0.20m, MajorDamageRate: 0.50m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Minor damage snapshot book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 200000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Minor damage reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });

        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        Fee borrowFee = Assert.Single(await feeService.GetFeesByBorrowAsync(borrowId), fee => fee.FeeType == FeeType.Borrow);
        Assert.Equal(10000m, borrowFee.Amount);

        Book currentBook = books.GetBookById(bookId)!;
        currentBook.ReplacementValue = 300000m;
        books.UpdateBook(currentBook);
        ReturnResult returned = circulation.ReturnBook(borrowId, ReturnCondition.Damaged, "Minor cover damage");

        Assert.Equal(DamageSeverity.Minor, returned.DamageLevel);
        Assert.Equal(1, returned.FeesCreated);
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Assert.Equal(2, fees.Count);
        Assert.Equal(10000m, Assert.Single(fees, fee => fee.FeeType == FeeType.Borrow).Amount);
        Fee damage = Assert.Single(fees, fee => fee.FeeType == FeeType.Damage);
        Assert.Equal("Minor", damage.DamageLevel);
        Assert.Equal("Return", damage.SourceType);
        Assert.Equal(returned.ReturnEventId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), damage.SourceId);
        Assert.Equal(300000m, damage.BookPriceSnapshot);
        Assert.Equal(300000m, damage.BaseAmount);
        Assert.Equal(0.20m, damage.AppliedRate);
        Assert.Equal(60000m, damage.UncappedAmount);
        Assert.Equal(60000m, damage.Amount);

        currentBook.ReplacementValue = 400000m;
        books.UpdateBook(currentBook);
        Fee reloaded = (await feeService.GetFeesByBorrowAsync(borrowId)).Single(fee => fee.FeeType == FeeType.Damage);
        Assert.Equal(300000m, reloaded.BaseAmount);
        Assert.Equal(0.20m, reloaded.AppliedRate);
        Assert.Equal(60000m, reloaded.Amount);
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            feeService.CreateDamageFeeAsync(borrowId, false, "Retry same return", "Return", damage.SourceId));
    }

    [IntegrationFact]
    public async Task LostReturnCreatesReplacementFeeFromCurrentValueAndKeepsBorrowFeeSeparate()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(LostRate: 0.25m, ReplacementRate: 1.00m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Lost replacement snapshot book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m, RentalPrice = 15000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Lost replacement reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });

        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        Book currentBook = books.GetBookById(bookId)!;
        currentBook.ReplacementValue = 350000m;
        books.UpdateBook(currentBook);

        ReturnResult returned = circulation.MarkAsLost(borrowId, "Độc giả báo mất sách");

        Assert.Equal(1, returned.FeesCreated);
        Assert.True(returned.ReturnEventId is > 0);
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Fee borrowFee = Assert.Single(fees, fee => fee.FeeType == FeeType.Borrow);
        Fee replacement = Assert.Single(fees, fee => fee.FeeType == FeeType.Replacement);
        Assert.Equal(15000m, borrowFee.Amount);
        Assert.DoesNotContain(fees, fee => fee.FeeType is FeeType.Lost or FeeType.Damage);
        Assert.Equal("Return", replacement.SourceType);
        Assert.Equal(returned.ReturnEventId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), replacement.SourceId);
        Assert.Equal(350000m, replacement.BookPriceSnapshot);
        Assert.Equal(350000m, replacement.BaseAmount);
        Assert.Equal(1.00m, replacement.AppliedRate);
        Assert.Equal(1m, replacement.Units);
        Assert.Equal(350000m, replacement.UncappedAmount);
        Assert.Equal(350000m, replacement.Amount);
        Assert.Equal(FeeStatus.Pending, replacement.Status);
        Assert.Equal("Đền bù sách mất", replacement.FeeTypeLabel);
        Assert.Equal(BookCopyStatuses.Lost, new BookCopyService().GetByBarcode(copy.Barcode)!.Status);

        currentBook.ReplacementValue = 400000m;
        books.UpdateBook(currentBook);
        Fee reloaded = (await feeService.GetFeesByBorrowAsync(borrowId)).Single(fee => fee.FeeType == FeeType.Replacement);
        Assert.Equal(350000m, reloaded.BookPriceSnapshot);
        Assert.Equal(350000m, reloaded.BaseAmount);
        Assert.Equal(1.00m, reloaded.AppliedRate);
        Assert.Equal(350000m, reloaded.Amount);
        await Assert.ThrowsAsync<DuplicateFeeSourceException>(() =>
            feeService.CreateReplacementFeeAsync(borrowId, "Return", replacement.SourceId));

        int otherReaderId = new ReaderService().AddReader(new Reader
        {
            FullName = "Other reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234568"
        });
        Assert.Throws<BusinessRuleException>(() => circulation.BorrowBook(otherReaderId, copy.CopyId));
    }

    [IntegrationFact]
    public async Task LostReturnWithMissingReplacementValueFailsAndRollsBackOutcome()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(ReplacementRate: 1.00m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Unpriced lost book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = null
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Unpriced lost reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });
        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);

        FeePolicyNotConfiguredException error = Assert.Throws<FeePolicyNotConfiguredException>(
            () => circulation.MarkAsLost(borrowId, "Độc giả báo mất"));
        Assert.Contains("ReplacementValue", error.Message, StringComparison.Ordinal);
        Assert.Contains("cập nhật", error.Message, StringComparison.OrdinalIgnoreCase);
        BorrowRecord loan = new BorrowRepository().GetById(borrowId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.LostDate);
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Assert.Empty(fees);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetByBarcode(copy.Barcode)!.Status);
        Assert.Single(circulation.GetAuditEvents(new[] { borrowId }));
    }

    [IntegrationFact]
    public async Task LostReturnWithMissingReplacementRateFailsAndRollsBackOutcome()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy()));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Unconfigured lost book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Unconfigured lost reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234569"
        });
        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);

        FeePolicyNotConfiguredException error = Assert.Throws<FeePolicyNotConfiguredException>(
            () => circulation.MarkAsLost(borrowId, "Độc giả báo mất"));
        Assert.Contains("FeePolicy.ReplacementRate", error.Message, StringComparison.Ordinal);
        Assert.Contains("cấu hình", error.Message, StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Assert.Contains(fees, fee => fee.FeeType == FeeType.Borrow);
        Assert.DoesNotContain(fees, fee => fee.FeeType is FeeType.Lost or FeeType.Replacement or FeeType.Damage);
        Assert.Equal("Borrowing", new BorrowRepository().GetById(borrowId)!.Status);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetByBarcode(copy.Barcode)!.Status);
        Assert.Single(circulation.GetAuditEvents(new[] { borrowId }));
    }

    [IntegrationFact]
    public async Task LostReturnRollsBackOutcomeCopyAndAuditWhenReplacementFeeInsertFails()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(ReplacementRate: 1.00m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Rollback lost fee book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Rollback lost fee reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234570"
        });
        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        const string triggerName = "TR_FeeFoundation_RejectReplacementInsert";
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($@"CREATE TRIGGER dbo.{triggerName} ON dbo.Fees AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE FeeType = {(int)FeeType.Replacement})
                        THROW 51090, 'Injected replacement fee insert failure.', 1;
                END;", connection);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            Assert.Throws<SqlException>(() => circulation.MarkAsLost(borrowId, "Độc giả báo mất"));
        }
        finally
        {
            await using var connection = Database.GetConnection();
            await connection.OpenAsync();
            await using var command = new SqlCommand($"DROP TRIGGER IF EXISTS dbo.{triggerName};", connection);
            await command.ExecuteNonQueryAsync();
        }

        BorrowRecord loan = new BorrowRepository().GetById(borrowId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.LostDate);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetByBarcode(copy.Barcode)!.Status);
        Assert.Single(circulation.GetAuditEvents(new[] { borrowId }));
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Assert.Contains(fees, fee => fee.FeeType == FeeType.Borrow);
        Assert.DoesNotContain(fees, fee => fee.FeeType == FeeType.Replacement);
    }

    [IntegrationFact]
    public async Task OverdueReturnRollsBackOutcomeCopyAndAuditWhenLateFeeInsertFails()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(LateFeeRatePerDay: 0.02m, MaxLateFeePercent: 1.00m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Rollback late fee book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Rollback late fee reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234571"
        });
        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                @"UPDATE dbo.BorrowRecords SET BorrowDate=DATEADD(day,-15,GETDATE()),
                    DueDate=DATEADD(day,-5,GETDATE()) WHERE BorrowId=@Id", connection);
            command.Parameters.AddWithValue("@Id", borrowId);
            await command.ExecuteNonQueryAsync();
        }

        const string triggerName = "TR_FeeFoundation_RejectLateInsert";
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($@"CREATE TRIGGER dbo.{triggerName} ON dbo.Fees AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE FeeType = {(int)FeeType.Late})
                        THROW 51091, 'Injected late fee insert failure.', 1;
                END;", connection);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            Assert.Throws<SqlException>(() => circulation.ReturnBook(borrowId, ReturnCondition.Normal));
        }
        finally
        {
            await using var connection = Database.GetConnection();
            await connection.OpenAsync();
            await using var command = new SqlCommand($"DROP TRIGGER IF EXISTS dbo.{triggerName};", connection);
            await command.ExecuteNonQueryAsync();
        }

        BorrowRecord loan = new BorrowRepository().GetById(borrowId)!;
        Assert.Equal("Borrowing", loan.Status);
        Assert.Null(loan.ReturnDate);
        Assert.Equal(BookCopyStatuses.Borrowed, new BookCopyService().GetByBarcode(copy.Barcode)!.Status);
        Assert.Single(circulation.GetAuditEvents(new[] { borrowId }));
        IReadOnlyList<Fee> fees = await feeService.GetFeesByBorrowAsync(borrowId);
        Assert.Contains(fees, fee => fee.FeeType == FeeType.Borrow);
        Assert.DoesNotContain(fees, fee => fee.FeeType == FeeType.Late);
    }

    [IntegrationFact]
    public async Task DamagedReturnWithMissingDamagePolicyReportsWarningAndKeepsReturnOutcome()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy()));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Missing damage rate book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = 300000m
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Missing rate reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });

        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        ReturnResult returned = circulation.ReturnBook(borrowId, ReturnCondition.Damaged, "Minor damage");

        Assert.Equal(0, returned.FeesCreated);
        Assert.Contains(returned.FeeWarnings, warning => warning.Contains("FeePolicy.MinorDamageRate", StringComparison.Ordinal));
        Assert.Contains(await feeService.GetFeesByBorrowAsync(borrowId), fee => fee.FeeType == FeeType.Borrow);
        Assert.DoesNotContain(await feeService.GetFeesByBorrowAsync(borrowId), fee => fee.FeeType == FeeType.Damage);
    }

    [IntegrationFact]
    public async Task DamagedReturnWithMissingReplacementValueReportsActionableWarningWithoutInventingPrice()
    {
        using var fixture = new SqlIntegrationFixture();
        var feeService = new FeeService(new FeeRepository(), new AuthServiceCurrentUserContext(),
            new StaticFeePolicyProvider(new FeePolicy(MinorDamageRate: 0.20m, MajorDamageRate: 0.50m)));
        var circulation = new BorrowService(new BookRepository(), new BorrowRepository(), new ReaderRepository(),
            new BookCopyRepository(), new FeeFinancialStandingProvider(), new CirculationAuditRepository(),
            new AuthServiceCurrentUserContext(), feeService: feeService);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Missing replacement value book", Author = "Author", PublishYear = 2026,
            Quantity = 1, ReplacementValue = null
        });
        var copy = Assert.Single(new BookCopyService().GetCopies(bookId));
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Missing value reader", StudentId = "FEE-" + Guid.NewGuid().ToString("N"), Phone = "0901234567"
        });

        int borrowId = circulation.BorrowBook(readerId, copy.CopyId);
        ReturnResult returned = circulation.ReturnBook(borrowId, ReturnCondition.Damaged, "Minor damage");

        Assert.Equal(0, returned.FeesCreated);
        Assert.Contains(returned.FeeWarnings, warning => warning.Contains("Replacement Value", StringComparison.Ordinal));
        Assert.Contains(returned.FeeWarnings, warning => warning.Contains("cập nhật ReplacementValue", StringComparison.Ordinal));
        Assert.DoesNotContain(await feeService.GetFeesByBorrowAsync(borrowId), fee => fee.FeeType == FeeType.Damage);
    }

    [IntegrationFact]
    public async Task ConcurrentPaymentsCannotOverpayOneFee()
    {
        using var fixture = new SqlIntegrationFixture();
        await FeeMigration.ApplyAsync();
        int borrowId = await GetAnyBorrowIdAsync();
        var service = new FeeService();
        Fee fee = await service.CreateFeeAsync(new CreateFeeRequest(borrowId, FeeType.Other, 100m,
            "Concurrent payment", "PaymentTest", Guid.NewGuid().ToString("N")));

        async Task<bool> TryPay(Guid idempotencyKey)
        {
            try
            {
                await service.RecordPaymentAsync(new RecordFeePaymentRequest(fee.FeeId, 70m, idempotencyKey));
                return true;
            }
            catch (BusinessRuleException) { return false; }
        }

        bool[] results = await Task.WhenAll(TryPay(Guid.NewGuid()), TryPay(Guid.NewGuid()));
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);
        Fee persisted = (await service.GetFeeAsync(fee.FeeId))!;
        Assert.Equal(70m, persisted.PaidAmount);
        Assert.Equal(FeeStatus.Partial, persisted.Status);
        Assert.Equal(30m, await service.GetOutstandingBalanceAsync(fee.ReaderId));
    }

    private static async Task<int> GetAnyBorrowIdAsync()
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TOP (1) BorrowId FROM dbo.BorrowRecords ORDER BY BorrowId", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SetBorrowBookValueAsync(int borrowId, decimal value)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(@"UPDATE dbo.Books SET ReplacementValue=@Value
            WHERE BookId=(SELECT BookId FROM dbo.BorrowRecords WHERE BorrowId=@BorrowId)", connection);
        command.Parameters.Add("@Value", System.Data.SqlDbType.Decimal).Value = value;
        command.Parameters["@Value"].Precision = 18;
        command.Parameters["@Value"].Scale = 2;
        command.Parameters.AddWithValue("@BorrowId", borrowId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed class StaticFeePolicyProvider(FeePolicy value) : IFeePolicyProvider
    {
        public FeePolicy GetCurrent() => value;
    }
}
