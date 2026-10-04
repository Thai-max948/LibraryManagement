namespace LibraryManagement.Models;

// Rates are fractions (0.10 means 10%). Book values and rental prices remain Book-owned.
public sealed record FeePolicy(decimal? LateFeeRatePerDay = null,
    decimal? MaxLateFeePercent = null, decimal? MinorDamageRate = null,
    decimal? MajorDamageRate = null, decimal? LostRate = null,
    decimal? ReplacementRate = null, decimal? RenewalFeeRate = null,
    int? RenewalDays = null, int? MaxRenewals = null);

public interface IFeePolicyProvider
{
    FeePolicy GetCurrent();
}
