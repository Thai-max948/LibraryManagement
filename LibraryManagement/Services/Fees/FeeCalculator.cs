using System;
using LibraryManagement.Models;

namespace LibraryManagement.Services;

public static class FeeCalculator
{
    // DueDate and circulation event times are stored as local library wall-clock DateTime values.
    // Late charging follows calendar dates and deliberately ignores time-of-day.
    public static int CalculateLateDays(DateTime dueDate, DateTime resolvedAt) =>
        Math.Max(0, (resolvedAt.Date - dueDate.Date).Days);

    public static decimal CalculateLate(decimal bookValue, int lateDays, FeePolicy policy) =>
        CalculateLateSnapshot(bookValue, lateDays, policy).FinalAmount;

    public static FeeCalculationSnapshot CalculateLateSnapshot(decimal bookValue, int lateDays, FeePolicy policy)
    {
        if (lateDays < 0) throw new BusinessRuleException("Số ngày trễ hạn không hợp lệ.");
        ValidateBookValue(bookValue);
        if (lateDays == 0) return new FeeCalculationSnapshot(bookValue, null, 0m, null, 0m, 0m);
        decimal daily = RequireRate(policy.LateFeeRatePerDay, nameof(policy.LateFeeRatePerDay));
        decimal capRate = RequireRate(policy.MaxLateFeePercent, nameof(policy.MaxLateFeePercent));
        decimal uncappedRaw = bookValue * daily * lateDays;
        decimal capRaw = bookValue * capRate;
        return new FeeCalculationSnapshot(bookValue, daily, lateDays, Round(capRaw), Round(uncappedRaw),
            Round(Math.Min(uncappedRaw, capRaw)));
    }

    public static decimal CalculateDamage(decimal bookValue, bool major, FeePolicy policy) =>
        CalculateDamageSnapshot(bookValue, major, policy).FinalAmount;

    public static FeeCalculationSnapshot CalculateDamageSnapshot(decimal bookValue, bool major, FeePolicy policy)
    {
        decimal rate = RequireRate(major ? policy.MajorDamageRate : policy.MinorDamageRate,
            major ? nameof(policy.MajorDamageRate) : nameof(policy.MinorDamageRate));
        ValidateBookValue(bookValue);
        decimal amount = Round(bookValue * rate);
        return new FeeCalculationSnapshot(bookValue, rate, null, null, amount, amount);
    }

    public static decimal CalculateReplacement(decimal bookValue, FeePolicy policy) =>
        CalculateReplacementSnapshot(bookValue, policy).FinalAmount;

    public static FeeCalculationSnapshot CalculateReplacementSnapshot(decimal bookValue, FeePolicy policy)
    {
        decimal rate = RequireRate(policy.ReplacementRate, nameof(policy.ReplacementRate));
        ValidateBookValue(bookValue);
        decimal amount = Round(bookValue * rate);
        return new FeeCalculationSnapshot(bookValue, rate, 1m, null, amount, amount);
    }

    private static decimal RequireRate(decimal? rate, string name)
    {
        if (rate is null) throw new FeePolicyNotConfiguredException($"Chưa cấu hình FeePolicy.{name}; khoản phí chưa được phát sinh.");
        if (rate < 0m || rate > 1m || decimal.Round(rate.Value, 6) != rate.Value)
            throw new BusinessRuleException($"FeePolicy.{name} phải nằm trong khoảng 0..1 với tối đa 6 chữ số thập phân.");
        return rate.Value;
    }

    private static void ValidateBookValue(decimal value)
    {
        if (value < 0m || value > FeeRules.MaximumSqlAmount || decimal.Round(value, 2) != value)
            throw new BusinessRuleException("Replacement Value của sách không hợp lệ.");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
