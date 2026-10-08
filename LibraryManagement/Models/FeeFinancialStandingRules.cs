namespace LibraryManagement.Models;

/// <summary>
/// Fee classification used by the borrowing financial-standing aggregate.
/// Unknown fee types remain blocking to preserve the previous behavior.
/// </summary>
public static class FeeFinancialStandingRules
{
    private static readonly FeeStatus[] OutstandingStatusValues =
    [
        FeeStatus.Pending,
        FeeStatus.Partial
    ];

    public static FeeType NonBlockingBorrowFeeType => FeeType.Borrow;

    public static IReadOnlyList<FeeStatus> OutstandingStatuses { get; } =
        Array.AsReadOnly(OutstandingStatusValues);

    public static bool IsBlockingOutstandingFee(
        FeeType feeType, FeeStatus status, decimal amount, decimal paidAmount) =>
        IsOutstandingStatus(status) && amount > paidAmount && feeType != NonBlockingBorrowFeeType;

    public static bool IsOutstandingStatus(FeeStatus status) =>
        status is FeeStatus.Pending or FeeStatus.Partial;
}
