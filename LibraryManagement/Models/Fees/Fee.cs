namespace LibraryManagement.Models;

public enum FeeType { Borrow = 1, Late = 2, Damage = 4, Replacement = 6, Other = 7 }
public enum FeeStatus { Pending = 1, Partial = 2, Paid = 3, Waived = 4, Cancelled = 5 }

public sealed record Fee(
    int FeeId, int BorrowId, int ReaderId, int? BookCopyId, FeeType FeeType,
    decimal Amount, decimal PaidAmount, FeeStatus Status, string Reason, string? Description,
    string SourceType, string SourceId, string ReaderNameSnapshot, string BookTitleSnapshot,
    string? BarcodeSnapshot, decimal? BookPriceSnapshot, DateTime CreatedAt, int? CreatedBy,
    DateTime? UpdatedAt, DateTime? PaidAt, decimal? RentalPriceSnapshot = null,
    int? LateDays = null, decimal? AppliedRate = null, string? DamageLevel = null,
    DateTime? WaivedAt = null, int? WaivedBy = null, string? WaiveReason = null,
    DateTime? CancelledAt = null, int? CancelledBy = null, string? CancelReason = null,
    decimal? AppliedCapRate = null, decimal? BaseAmount = null, decimal? Units = null,
    decimal? CapAmount = null, decimal? UncappedAmount = null, decimal WaivedAmount = 0m,
    DateTime? DueDateSnapshot = null, DateTime? ResolvedAtSnapshot = null)
{
    public decimal Remaining => Status is FeeStatus.Waived or FeeStatus.Cancelled ? 0m : Amount - PaidAmount;
    // Amount is the persisted final amount. Null inputs remain null for legacy rows that cannot
    // be explained reliably; the projection never recalculates from today's FeePolicy.
    public FeeCalculationSnapshot? CalculationSnapshot =>
        BaseAmount.HasValue || AppliedRate.HasValue || Units.HasValue || CapAmount.HasValue || UncappedAmount.HasValue
            ? new FeeCalculationSnapshot(BaseAmount, AppliedRate, Units, CapAmount, UncappedAmount, Amount)
            : null;
    public string FeeTypeLabel => FeeType switch
    {
        FeeType.Borrow => "Mượn sách",
        FeeType.Late => "Trễ hạn",
        FeeType.Damage => "Hư hỏng",
        FeeType.Replacement => "Đền bù sách mất",
        _ => "Khác"
    };
    public string StatusLabel => Status switch
    {
        FeeStatus.Pending => "Chưa thanh toán",
        FeeStatus.Partial => "Thanh toán một phần",
        FeeStatus.Paid => "Đã thanh toán",
        FeeStatus.Waived => "Đã miễn",
        _ => "Đã hủy"
    };
    public string SourceDisplay => $"{SourceType} · {SourceId}";
}

public sealed record FeeCalculationSnapshot(decimal? BaseAmount, decimal? AppliedRate,
    decimal? Units, decimal? CapAmount, decimal? UncappedAmount, decimal FinalAmount);

public sealed record CreateFeeRequest(int BorrowId, FeeType FeeType, decimal Amount,
    string Reason, string SourceType, string SourceId, string? Description = null,
    int? LateDays = null, decimal? AppliedRate = null, string? DamageLevel = null,
    decimal? AppliedCapRate = null);

public sealed record FeePayment(int PaymentId, int FeeId, decimal Amount, DateTime PaidAt,
    int? RecordedBy, string? Note, Guid IdempotencyKey);

public sealed record FeeSourceSnapshot(int ReaderId, int? BookCopyId, string ReaderName,
    string BookTitle, string? Barcode, decimal? BookPrice, decimal? RentalPrice);

public sealed record RecordFeePaymentRequest(int FeeId, decimal Amount, Guid IdempotencyKey, string? Note = null);
public sealed record FeeSearchQuery(string? SearchText, FeeStatus? Status, FeeType? FeeType, int Page = 1, int PageSize = 50);
public sealed record FeePage(IReadOnlyList<Fee> Items, int TotalCount, int Page, int PageSize);
public sealed record FeeAssessmentResult(IReadOnlyList<Fee> Created, IReadOnlyList<string> Warnings)
{
    public int LateDays { get; init; }
}

public static class FeeRules
{
    public const decimal MaximumSqlAmount = 9999999999999999.99m;

    public static FeeStatus ValidateNewAmount(decimal amount)
    {
        FeeStatus status = DetermineStatus(amount, 0m);
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "A new fee must be greater than zero.");
        return status;
    }

    public static FeeStatus DetermineStatus(decimal amount, decimal paidAmount)
    {
        if (amount < 0 || paidAmount < 0 || paidAmount > amount ||
            amount > MaximumSqlAmount || paidAmount > MaximumSqlAmount ||
            decimal.Round(amount, 2) != amount || decimal.Round(paidAmount, 2) != paidAmount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Fee amounts must be nonnegative cents and paid cannot exceed amount.");
        if (amount == 0 || paidAmount == amount) return FeeStatus.Paid;
        return paidAmount == 0 ? FeeStatus.Pending : FeeStatus.Partial;
    }
}

public static class FeeStateRules
{
    public static bool CanRecordPayment(Fee fee) => fee.Status is FeeStatus.Pending or FeeStatus.Partial;
    public static bool CanWaive(Fee fee) => fee.Status is FeeStatus.Pending or FeeStatus.Partial;
    public static bool CanCancel(Fee fee) => fee.Status == FeeStatus.Pending && fee.PaidAmount == 0m;
}
