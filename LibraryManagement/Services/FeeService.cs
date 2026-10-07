using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services;

public interface IFeeService
{
    void EnsureSchema();
    Fee? CreateBorrowFee(SqlConnection connection, SqlTransaction transaction, int borrowId);
    FeeAssessmentResult AssessReturnFees(SqlConnection connection, SqlTransaction transaction, ReturnResult result);
    Task<Fee> CreateFeeAsync(CreateFeeRequest request, CancellationToken cancellationToken = default);
    Task<Fee?> CreateLateFeeAsync(int borrowId, int lateDays, string sourceType, string sourceId, CancellationToken cancellationToken = default);
    Task<Fee?> CreateDamageFeeAsync(int borrowId, bool major, string reason, string sourceType, string sourceId, CancellationToken cancellationToken = default);
    Task<Fee?> CreateReplacementFeeAsync(int borrowId, string sourceType, string sourceId, CancellationToken cancellationToken = default);
    Fee? CreateLateFee(SqlConnection connection, SqlTransaction transaction, int borrowId, int lateDays, string sourceType, string sourceId);
    Fee? CreateDamageFee(SqlConnection connection, SqlTransaction transaction, int borrowId, bool major, string reason, string sourceType, string sourceId);
    Fee? CreateReplacementFee(SqlConnection connection, SqlTransaction transaction, int borrowId, string sourceType, string sourceId);
    int GetLateDays(DateTime dueDate, DateTime resolvedAt);
    Task<Fee?> GetFeeAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Fee>> GetFeesByBorrowAsync(int borrowId, CancellationToken cancellationToken = default);
    Task<FeePage> GetFeesAsync(FeeSearchQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeePayment>> GetPaymentHistoryAsync(int feeId, CancellationToken cancellationToken = default);
    Task<decimal> GetOutstandingBalanceAsync(int readerId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalOutstandingBalanceAsync(CancellationToken cancellationToken = default);
    Task<FeePayment> RecordPaymentAsync(RecordFeePaymentRequest request, CancellationToken cancellationToken = default);
    Task<Fee> WaiveAsync(int feeId, string reason, CancellationToken cancellationToken = default);
    Task<Fee> CancelAsync(int feeId, string reason, CancellationToken cancellationToken = default);
}

public sealed class DuplicateFeeSourceException : BusinessRuleException
{
    public DuplicateFeeSourceException() : base("Nguồn phát sinh khoản phí này đã được ghi nhận.") { }
}

public sealed class FeePaymentIdempotencyConflictException : BusinessRuleException
{
    public FeePaymentIdempotencyConflictException() : base("Idempotency key đã được dùng cho một yêu cầu thanh toán khác.") { }
}

public sealed class FeeService : IFeeService
{
    private sealed record FeeCalculation(FeeCalculationSnapshot Snapshot, int? LateDays = null,
        string? DamageLevel = null, decimal? AppliedCapRate = null)
    {
        public decimal Amount => Snapshot.FinalAmount;
    }
    private readonly FeeRepository _repository;
    private readonly ICurrentUserContext _currentUser;
    private readonly IFeePolicyProvider _policyProvider;

    public FeeService() : this(new FeeRepository(), new AuthServiceCurrentUserContext(), new ConfiguredFeePolicyProvider()) { }

    public FeeService(FeeRepository repository, ICurrentUserContext currentUser, IFeePolicyProvider? policyProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _policyProvider = policyProvider ?? new ConfiguredFeePolicyProvider();
    }

    public void EnsureSchema()
    {
        BookValueMigration.Apply();
        FeeMigration.Apply();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        // Book remains the owner of both current replacement and rental values.
        BookValueMigration.Apply();
        await FeeMigration.ApplyAsync(cancellationToken);
    }

    // This compatibility entry point intentionally accepts only a manually assessed Other fee.
    // All calculated fee types go through command-specific methods below.
    public async Task<Fee> CreateFeeAsync(CreateFeeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FeeType != FeeType.Other)
            throw new BusinessRuleException("Khoản phí này phải được tạo qua lệnh nghiệp vụ tương ứng.");
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            Fee fee = await CreateInTransactionAsync(request, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeeCreated, fee.FeeId.ToString()));
            return fee;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Fee> CreateInTransactionAsync(CreateFeeRequest request, SqlConnection connection,
        SqlTransaction transaction, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);
        FeeStatus status = FeeRules.ValidateNewAmount(request.Amount);

        FeeSourceSnapshot snapshot = _repository.GetSourceSnapshot(connection, transaction, request.BorrowId)
            ?? throw new BusinessRuleException("Phiếu mượn hoặc dữ liệu tham chiếu không tồn tại.");
        ValidateSnapshot(snapshot);
        var fee = BuildFee(request, snapshot, status,
            new FeeCalculationSnapshot(request.Amount, null, null, null, request.Amount, request.Amount));
        try
        {
            return await _repository.CreateAsync(connection, transaction, fee, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new DuplicateFeeSourceException();
        }
    }

    // BorrowService owns the circulation transaction. This command joins that transaction so
    // a rental fee and the BorrowRecord commit or roll back together.
    public Fee? CreateBorrowFee(SqlConnection connection, SqlTransaction transaction, int borrowId)
    {
        FeeSourceSnapshot snapshot = _repository.GetSourceSnapshot(connection, transaction, borrowId)
            ?? throw new BusinessRuleException("Không thể snapshot khoản phí mượn vì không tìm thấy phiếu mượn.");
        ValidateSnapshot(snapshot);
        if (snapshot.RentalPrice is null || snapshot.RentalPrice <= 0m)
            return null; // Legacy Book rows can legitimately have no configured rental price.

        var request = new CreateFeeRequest(borrowId, FeeType.Borrow, snapshot.RentalPrice.Value,
            "Phí mượn theo Rental Price đã lưu trên đầu sách.", "Borrow",
            borrowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        FeeRules.ValidateNewAmount(request.Amount);
        var fee = BuildFee(request, snapshot, FeeStatus.Pending,
            new FeeCalculationSnapshot(snapshot.RentalPrice.Value, null, null, null,
                snapshot.RentalPrice.Value, snapshot.RentalPrice.Value));
        try { return _repository.Create(connection, transaction, fee); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new DuplicateFeeSourceException(); }
    }

    // BorrowService owns the transaction. This method joins it so circulation and all assessed
    // fees commit or roll back together. Missing optional policy/value remains an explicit warning.
    public FeeAssessmentResult AssessReturnFees(SqlConnection connection, SqlTransaction transaction, ReturnResult result)
    {
        var created = new List<Fee>();
        var warnings = new List<string>();
        int lateDays = FeeCalculator.CalculateLateDays(result.DueDate, result.ResolvedAt);
        if (lateDays == 0 && result.Disposition == ReturnCondition.Normal)
            return new FeeAssessmentResult(created, warnings) { LateDays = 0 };

        FeeSourceSnapshot snapshot = _repository.GetSourceSnapshot(connection, transaction, result.BorrowId)
            ?? throw new BusinessRuleException("Không thể snapshot phí trả sách vì không tìm thấy phiếu mượn.");
        if (result.ReturnEventId is not > 0)
            throw new BusinessRuleException("Không thể ghi phí vì sự kiện trả sách chưa có mã định danh ổn định.");
        ValidateSnapshot(snapshot);
        FeePolicy policy = _policyProvider.GetCurrent();

        if (lateDays > 0)
        {
            if (!snapshot.BookPrice.HasValue)
                warnings.Add("Chưa phát sinh phí trễ hạn: sách chưa có Replacement Value được cấu hình.");
            else
            {
                try
                {
                    FeeCalculation calculation = new(FeeCalculator.CalculateLateSnapshot(
                        snapshot.BookPrice.Value, lateDays, policy), lateDays,
                        AppliedCapRate: policy.MaxLateFeePercent);
                    if (calculation.Amount > 0m)
                        created.Add(CreateReturnFee(connection, transaction, result, snapshot, FeeType.Late,
                            calculation, $"Trả trễ {lateDays} ngày."));
                }
                catch (FeePolicyNotConfiguredException ex) { warnings.Add(ex.Message); }
            }
        }

        if (result.Disposition is ReturnCondition.Damaged or ReturnCondition.NeedsRepair)
        {
            DamageSeverity damageLevel = result.DamageLevel
                ?? throw new BusinessRuleException("Outcome trả sách hư hỏng chưa xác định DamageLevel.");
            bool major = damageLevel == DamageSeverity.Major;
            if (!snapshot.BookPrice.HasValue)
                warnings.Add($"Chưa tạo Damage Fee ({damageLevel}): sách chưa có Replacement Value. " +
                    "Hãy cập nhật ReplacementValue trong Book và xử lý khoản phí của Return này theo quy trình được cấp quyền.");
            else
            {
                try
                {
                    FeeCalculation calculation = new(FeeCalculator.CalculateDamageSnapshot(
                        snapshot.BookPrice.Value, major, policy), DamageLevel: major ? "Major" : "Minor");
                    if (calculation.Amount > 0m)
                        created.Add(CreateReturnFee(connection, transaction, result, snapshot, FeeType.Damage,
                            calculation, major ? "Sách cần sửa chữa; áp dụng mức hư hỏng nặng." : "Sách hư hỏng; áp dụng mức hư hỏng nhẹ."));
                }
                catch (FeePolicyNotConfiguredException ex)
                {
                    warnings.Add($"Chưa tạo Damage Fee ({damageLevel}): {ex.Message}");
                }
            }
        }

        if (result.IsLost)
        {
            if (!snapshot.BookPrice.HasValue)
                throw new FeePolicyNotConfiguredException(
                    "Không thể hoàn tất báo mất sách vì Book chưa cấu hình ReplacementValue. " +
                    "Hãy cập nhật ReplacementValue trong Book rồi thử lại.");
            else
            {
                try
                {
                    FeeCalculation calculation = new(FeeCalculator.CalculateReplacementSnapshot(snapshot.BookPrice.Value, policy));
                    created.Add(CreateReturnFee(connection, transaction, result, snapshot, FeeType.Replacement,
                        calculation, "Sách được ghi nhận thất lạc; áp dụng ReplacementValue tại thời điểm ghi nhận."));
                }
                catch (FeePolicyNotConfiguredException ex)
                {
                    throw new FeePolicyNotConfiguredException(
                        $"Không thể hoàn tất báo mất sách. {ex.Message} " +
                        "Hãy cấu hình FeePolicy.ReplacementRate trong appsettings.json rồi thử lại.");
                }
            }
        }

        return new FeeAssessmentResult(created, warnings) { LateDays = lateDays };
    }

    public int GetLateDays(DateTime dueDate, DateTime resolvedAt) =>
        FeeCalculator.CalculateLateDays(dueDate, resolvedAt);

    private Fee CreateReturnFee(SqlConnection connection, SqlTransaction transaction, ReturnResult result,
        FeeSourceSnapshot snapshot, FeeType type, FeeCalculation calculation, string reason)
    {
        var request = new CreateFeeRequest(result.BorrowId, type, calculation.Amount, reason, "Return",
            result.ReturnEventId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LateDays: calculation.LateDays, AppliedRate: calculation.Snapshot.AppliedRate,
            DamageLevel: calculation.DamageLevel, AppliedCapRate: calculation.AppliedCapRate);
        Fee fee = BuildFee(request, snapshot, FeeRules.DetermineStatus(calculation.Amount, 0m),
            calculation.Snapshot, result.DueDate, result.ResolvedAt);
        try { return _repository.Create(connection, transaction, fee); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new DuplicateFeeSourceException(); }
    }

    public Task<Fee?> CreateLateFeeAsync(int borrowId, int lateDays, string sourceType, string sourceId,
        CancellationToken cancellationToken = default) => CreateCalculatedFeeAsync(borrowId, FeeType.Late,
        $"Trả trễ {lateDays} ngày.", sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí trễ hạn chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateLateSnapshot(snapshot.BookPrice.Value, lateDays, policy),
                lateDays, AppliedCapRate: policy.MaxLateFeePercent);
        }, cancellationToken);

    public Task<Fee?> CreateDamageFeeAsync(int borrowId, bool major, string reason, string sourceType, string sourceId,
        CancellationToken cancellationToken = default) => CreateCalculatedFeeAsync(borrowId, FeeType.Damage, reason,
        sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí hư hỏng chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateDamageSnapshot(snapshot.BookPrice.Value, major, policy),
                DamageLevel: major ? "Major" : "Minor");
        }, cancellationToken);

    public Task<Fee?> CreateReplacementFeeAsync(int borrowId, string sourceType, string sourceId,
        CancellationToken cancellationToken = default) => CreateCalculatedFeeAsync(borrowId, FeeType.Replacement,
        "Phí thay thế theo FeePolicy.", sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí thay thế chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateReplacementSnapshot(snapshot.BookPrice.Value, policy));
        }, cancellationToken);

    // These overloads join a caller-owned SQL transaction. The caller ensures migration first
    // and owns commit/rollback, so Return commands can keep Fee atomic.
    public Fee? CreateLateFee(SqlConnection connection, SqlTransaction transaction, int borrowId, int lateDays,
        string sourceType, string sourceId) => CreateCalculatedFeeInTransaction(connection, transaction, borrowId,
        FeeType.Late, $"Trả trễ {lateDays} ngày.", sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí trễ hạn chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateLateSnapshot(snapshot.BookPrice.Value, lateDays, policy),
                lateDays, AppliedCapRate: policy.MaxLateFeePercent);
        });

    public Fee? CreateDamageFee(SqlConnection connection, SqlTransaction transaction, int borrowId, bool major,
        string reason, string sourceType, string sourceId) => CreateCalculatedFeeInTransaction(connection, transaction,
        borrowId, FeeType.Damage, reason, sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí hư hỏng chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateDamageSnapshot(snapshot.BookPrice.Value, major, policy),
                DamageLevel: major ? "Major" : "Minor");
        });

    public Fee? CreateReplacementFee(SqlConnection connection, SqlTransaction transaction, int borrowId,
        string sourceType, string sourceId) => CreateCalculatedFeeInTransaction(connection, transaction, borrowId,
        FeeType.Replacement, "Phí thay thế theo FeePolicy.", sourceType, sourceId, (snapshot, policy) =>
        {
            if (snapshot.BookPrice is null) throw new FeePolicyNotConfiguredException("Chưa cấu hình Replacement Value cho sách; phí thay thế chưa được phát sinh.");
            return new FeeCalculation(FeeCalculator.CalculateReplacementSnapshot(snapshot.BookPrice.Value, policy));
        });

    private Fee? CreateCalculatedFeeInTransaction(SqlConnection connection, SqlTransaction transaction, int borrowId,
        FeeType feeType, string reason, string sourceType, string sourceId,
        Func<FeeSourceSnapshot, FeePolicy, FeeCalculation> calculate)
    {
        ValidateSourceCommand(borrowId, reason, sourceType, sourceId);
        FeeSourceSnapshot snapshot = _repository.GetSourceSnapshot(connection, transaction, borrowId)
            ?? throw new BusinessRuleException("Phiếu mượn hoặc dữ liệu tham chiếu không tồn tại.");
        ValidateSnapshot(snapshot);
        FeeCalculation calculation = calculate(snapshot, _policyProvider.GetCurrent());
        if (calculation.Amount == 0m) return null;
        FeeStatus status = FeeRules.ValidateNewAmount(calculation.Amount);
        var request = new CreateFeeRequest(borrowId, feeType, calculation.Amount, reason,
            sourceType.Trim(), sourceId.Trim(), LateDays: calculation.LateDays,
            AppliedRate: calculation.Snapshot.AppliedRate, DamageLevel: calculation.DamageLevel,
            AppliedCapRate: calculation.AppliedCapRate);
        ValidateCreateRequest(request);
        Fee fee = BuildFee(request, snapshot, status, calculation.Snapshot);
        try { return _repository.Create(connection, transaction, fee); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new DuplicateFeeSourceException(); }
    }

    private static void ValidateSourceCommand(int borrowId, string reason, string sourceType, string sourceId)
    {
        if (borrowId <= 0 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(sourceType) || sourceType.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(sourceId) || sourceId.Trim().Length > 200)
            throw new BusinessRuleException("Thông tin nguồn phát sinh phí không hợp lệ.");
    }

    private async Task<Fee?> CreateCalculatedFeeAsync(int borrowId, FeeType feeType, string reason,
        string sourceType, string sourceId, Func<FeeSourceSnapshot, FeePolicy, FeeCalculation> calculate,
        CancellationToken cancellationToken)
    {
        ValidateSourceCommand(borrowId, reason, sourceType, sourceId);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            FeeSourceSnapshot snapshot = _repository.GetSourceSnapshot(connection, transaction, borrowId)
                ?? throw new BusinessRuleException("Phiếu mượn hoặc dữ liệu tham chiếu không tồn tại.");
            ValidateSnapshot(snapshot);
            FeeCalculation calculation = calculate(snapshot, _policyProvider.GetCurrent());
            if (calculation.Amount == 0m)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            FeeStatus status = FeeRules.ValidateNewAmount(calculation.Amount);
            var request = new CreateFeeRequest(borrowId, feeType, calculation.Amount, reason,
                sourceType.Trim(), sourceId.Trim(), LateDays: calculation.LateDays,
                AppliedRate: calculation.Snapshot.AppliedRate, DamageLevel: calculation.DamageLevel,
                AppliedCapRate: calculation.AppliedCapRate);
            ValidateCreateRequest(request);
            Fee fee = BuildFee(request, snapshot, status, calculation.Snapshot);
            Fee created;
            try { created = await _repository.CreateAsync(connection, transaction, fee, cancellationToken); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new DuplicateFeeSourceException(); }
            await transaction.CommitAsync(cancellationToken);
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeeCreated, created.FeeId.ToString()));
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Fee BuildFee(CreateFeeRequest request, FeeSourceSnapshot snapshot, FeeStatus status,
        FeeCalculationSnapshot? calculationSnapshot = null, DateTime? dueDateSnapshot = null,
        DateTime? resolvedAtSnapshot = null)
    {
        calculationSnapshot ??= new FeeCalculationSnapshot(request.Amount, request.AppliedRate,
            request.LateDays, null, request.Amount, request.Amount);
        ValidateCalculationSnapshot(calculationSnapshot, request.Amount);
        return new Fee(
            FeeId: 0, BorrowId: request.BorrowId, ReaderId: snapshot.ReaderId, BookCopyId: snapshot.BookCopyId,
            FeeType: request.FeeType, Amount: request.Amount, PaidAmount: 0m, Status: status,
            Reason: request.Reason.Trim(), Description: string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SourceType: request.SourceType.Trim(), SourceId: request.SourceId.Trim(),
            ReaderNameSnapshot: snapshot.ReaderName.Trim(), BookTitleSnapshot: snapshot.BookTitle.Trim(),
            BarcodeSnapshot: snapshot.Barcode, BookPriceSnapshot: snapshot.BookPrice,
            CreatedAt: DateTime.UtcNow, CreatedBy: CurrentActorId(), UpdatedAt: null, PaidAt: null,
            RentalPriceSnapshot: snapshot.RentalPrice, LateDays: request.LateDays,
            AppliedRate: calculationSnapshot.AppliedRate, DamageLevel: request.DamageLevel,
            AppliedCapRate: request.AppliedCapRate, BaseAmount: calculationSnapshot.BaseAmount,
            Units: calculationSnapshot.Units, CapAmount: calculationSnapshot.CapAmount,
            UncappedAmount: calculationSnapshot.UncappedAmount,
            DueDateSnapshot: dueDateSnapshot, ResolvedAtSnapshot: resolvedAtSnapshot);
    }

    private static void ValidateCalculationSnapshot(FeeCalculationSnapshot snapshot, decimal amount)
    {
        static bool IsMoney(decimal? value) => !value.HasValue ||
            (value.Value >= 0m && value.Value <= FeeRules.MaximumSqlAmount && decimal.Round(value.Value, 2) == value.Value);

        if (snapshot.FinalAmount != amount || !IsMoney(snapshot.BaseAmount) || !IsMoney(snapshot.CapAmount) ||
            !IsMoney(snapshot.UncappedAmount) || snapshot.Units is < 0m or > 99999999999999.9999m ||
            (snapshot.Units.HasValue && decimal.Round(snapshot.Units.Value, 4) != snapshot.Units.Value) ||
            (snapshot.AppliedRate.HasValue && (snapshot.AppliedRate < 0m || snapshot.AppliedRate > 1m ||
                decimal.Round(snapshot.AppliedRate.Value, 6) != snapshot.AppliedRate.Value)))
            throw new BusinessRuleException("Calculation snapshot không khớp hoặc vượt độ chính xác lưu trữ của Fee.");
    }

    private static void ValidateCreateRequest(CreateFeeRequest request)
    {
        if (request.BorrowId <= 0 || !Enum.IsDefined(request.FeeType) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(request.SourceType) || request.SourceType.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.SourceId) || request.SourceId.Trim().Length > 200 ||
            request.Description?.Length > 1000 || request.LateDays < 0 ||
            (request.AppliedRate.HasValue && (request.AppliedRate < 0m || request.AppliedRate > 1m)) ||
            (request.AppliedCapRate.HasValue && (request.AppliedCapRate < 0m || request.AppliedCapRate > 1m)) ||
            request.AppliedRate.HasValue && decimal.Round(request.AppliedRate.Value, 6) != request.AppliedRate.Value ||
            request.AppliedCapRate.HasValue && decimal.Round(request.AppliedCapRate.Value, 6) != request.AppliedCapRate.Value ||
            request.DamageLevel?.Length > 30)
            throw new ArgumentException("Thông tin khoản phí không hợp lệ.", nameof(request));
    }

    private static void ValidateSnapshot(FeeSourceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ReaderName) || string.IsNullOrWhiteSpace(snapshot.BookTitle))
            throw new BusinessRuleException("Không thể ghi khoản phí với snapshot độc giả hoặc sách trống.");
        if (snapshot.BookCopyId.HasValue && string.IsNullOrWhiteSpace(snapshot.Barcode))
            throw new BusinessRuleException("Bản sách không khớp với phiếu mượn; không thể ghi snapshot barcode.");
        if (snapshot.BookPrice < 0m || snapshot.RentalPrice < 0m ||
            snapshot.BookPrice > FeeRules.MaximumSqlAmount || snapshot.RentalPrice > FeeRules.MaximumSqlAmount)
            throw new BusinessRuleException("Giá trị snapshot của sách không hợp lệ.");
    }

    public async Task<FeePayment> RecordPaymentAsync(RecordFeePaymentRequest request, CancellationToken cancellationToken = default)
    {
        request = request with { Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() };
        ValidatePaymentRequest(request);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            Fee fee = _repository.GetForUpdate(connection, transaction, request.FeeId)
                ?? throw new BusinessRuleException("Không tìm thấy khoản phí.");
            FeePayment? prior = _repository.GetPaymentByIdempotencyKey(connection, transaction, request.IdempotencyKey);
            if (prior is not null)
            {
                EnsureSamePayment(prior, request);
                await transaction.CommitAsync(cancellationToken);
                return prior;
            }
            if (!FeeStateRules.CanRecordPayment(fee))
                throw new BusinessRuleException("Khoản phí đã kết thúc, không thể ghi nhận thêm thanh toán.");
            if (request.Amount > fee.Amount - fee.PaidAmount)
                throw new BusinessRuleException("Số tiền thanh toán vượt quá số dư còn lại.");

            decimal paid = fee.PaidAmount + request.Amount;
            FeeStatus status = FeeRules.DetermineStatus(fee.Amount, paid);
            DateTime now = DateTime.UtcNow;
            var payment = _repository.InsertPayment(connection, transaction, request, CurrentActorId(), now);
            DateTime? paidAt = status == FeeStatus.Paid ? now : null;
            if (!_repository.UpdatePaymentState(connection, transaction, fee.FeeId, paid, status, now, paidAt))
                throw new BusinessRuleException("Khoản phí vừa được thay đổi. Hãy tải lại và thử lại.");

            await transaction.CommitAsync(cancellationToken);
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeePaid, fee.FeeId.ToString()));
            return payment;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            FeePayment? prior = _repository.GetPaymentByIdempotencyKey(request.IdempotencyKey);
            if (prior is not null)
            {
                EnsureSamePayment(prior, request);
                return prior;
            }
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidatePaymentRequest(RecordFeePaymentRequest request)
    {
        FeeRules.DetermineStatus(request.Amount, 0m);
        if (request.FeeId <= 0 || request.Amount <= 0m || request.IdempotencyKey == Guid.Empty || request.Note?.Length > 500)
            throw new BusinessRuleException("Thông tin thanh toán không hợp lệ.");
    }

    private static void EnsureSamePayment(FeePayment prior, RecordFeePaymentRequest request)
    {
        if (prior.FeeId != request.FeeId || prior.Amount != request.Amount ||
            !string.Equals(prior.Note, request.Note, StringComparison.Ordinal))
            throw new FeePaymentIdempotencyConflictException();
    }

    public Task<Fee> WaiveAsync(int feeId, string reason, CancellationToken cancellationToken = default) =>
        ChangeTerminalStateAsync(feeId, reason, waive: true, cancellationToken);

    public Task<Fee> CancelAsync(int feeId, string reason, CancellationToken cancellationToken = default) =>
        ChangeTerminalStateAsync(feeId, reason, waive: false, cancellationToken);

    private async Task<Fee> ChangeTerminalStateAsync(int feeId, string reason, bool waive, CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (feeId <= 0 || reason.Length == 0 || reason.Length > 500)
            throw new BusinessRuleException("Cần nhập lý do hợp lệ (tối đa 500 ký tự).");
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            Fee fee = _repository.GetForUpdate(connection, transaction, feeId)
                ?? throw new BusinessRuleException("Không tìm thấy khoản phí.");
            bool allowed = waive ? FeeStateRules.CanWaive(fee) : FeeStateRules.CanCancel(fee);
            if (!allowed)
                throw new BusinessRuleException(waive
                    ? "Chỉ có thể miễn khoản phí đang Pending hoặc Partial."
                    : "Chỉ có thể hủy khoản phí Pending chưa thanh toán.");

            DateTime now = DateTime.UtcNow;
            bool changed = waive
                ? _repository.Waive(connection, transaction, feeId, reason, CurrentActorId(), now)
                : _repository.Cancel(connection, transaction, feeId, reason, CurrentActorId(), now);
            if (!changed) throw new BusinessRuleException("Khoản phí vừa được thay đổi. Hãy tải lại trước khi thao tác.");
            Fee updated = _repository.GetForUpdate(connection, transaction, feeId)!;
            await transaction.CommitAsync(cancellationToken);
            if (waive) NotificationEvents.PublishAfterSuccess(new(BusinessAction.FeeWaived, feeId.ToString()));
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private int? CurrentActorId() => _currentUser.CurrentUser is { Id: > 0 } user ? user.Id : null;

    public async Task<Fee?> GetFeeAsync(int id, CancellationToken cancellationToken = default)
    { if (id <= 0) throw new BusinessRuleException("Mã khoản phí không hợp lệ."); await EnsureSchemaAsync(cancellationToken); return await _repository.GetByIdAsync(id, cancellationToken); }
    public async Task<IReadOnlyList<Fee>> GetFeesByBorrowAsync(int borrowId, CancellationToken cancellationToken = default)
    { if (borrowId <= 0) throw new BusinessRuleException("Mã phiếu mượn không hợp lệ."); await EnsureSchemaAsync(cancellationToken); return await _repository.GetByBorrowIdAsync(borrowId, cancellationToken); }
    public async Task<FeePage> GetFeesAsync(FeeSearchQuery query, CancellationToken cancellationToken = default)
    {
        if ((query.Status.HasValue && !Enum.IsDefined(query.Status.Value)) ||
            (query.FeeType.HasValue && !Enum.IsDefined(query.FeeType.Value)) || query.SearchText?.Length > 200)
            throw new BusinessRuleException("Bộ lọc khoản phí không hợp lệ.");
        await EnsureSchemaAsync(cancellationToken);
        return await _repository.GetPageAsync(query with { Page = Math.Max(query.Page, 1), PageSize = Math.Clamp(query.PageSize, 1, 200) }, cancellationToken);
    }
    public async Task<IReadOnlyList<FeePayment>> GetPaymentHistoryAsync(int feeId, CancellationToken cancellationToken = default)
    {
        if (feeId <= 0) throw new BusinessRuleException("Mã khoản phí không hợp lệ.");
        await EnsureSchemaAsync(cancellationToken);
        return await _repository.GetPaymentHistoryAsync(feeId, cancellationToken);
    }
    public async Task<decimal> GetOutstandingBalanceAsync(int readerId, CancellationToken cancellationToken = default)
    {
        if (readerId <= 0) throw new BusinessRuleException("Mã độc giả không hợp lệ.");
        await EnsureSchemaAsync(cancellationToken);
        return await _repository.GetOutstandingBalanceAsync(readerId, cancellationToken);
    }
    public async Task<decimal> GetTotalOutstandingBalanceAsync(CancellationToken cancellationToken = default)
    { await EnsureSchemaAsync(cancellationToken); return await _repository.GetTotalOutstandingBalanceAsync(cancellationToken); }
}
