using LibraryManagement.Models;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class FeeFinancialStandingRulesTests
{
    [Theory]
    [InlineData(FeeType.Borrow, FeeStatus.Pending, 10.00, 0.00, false)]
    [InlineData(FeeType.Borrow, FeeStatus.Partial, 10.00, 2.00, false)]
    [InlineData(FeeType.Late, FeeStatus.Pending, 10.00, 0.00, true)]
    [InlineData(FeeType.Late, FeeStatus.Partial, 10.00, 2.00, true)]
    [InlineData(FeeType.Damage, FeeStatus.Pending, 10.00, 0.00, true)]
    [InlineData(FeeType.Damage, FeeStatus.Partial, 10.00, 2.00, true)]
    [InlineData(FeeType.Replacement, FeeStatus.Pending, 10.00, 0.00, true)]
    [InlineData(FeeType.Replacement, FeeStatus.Partial, 10.00, 2.00, true)]
    [InlineData(FeeType.Late, FeeStatus.Paid, 10.00, 10.00, false)]
    [InlineData(FeeType.Damage, FeeStatus.Paid, 10.00, 10.00, false)]
    [InlineData(FeeType.Replacement, FeeStatus.Paid, 10.00, 10.00, false)]
    [InlineData(FeeType.Late, FeeStatus.Waived, 10.00, 0.00, false)]
    [InlineData(FeeType.Damage, FeeStatus.Waived, 10.00, 0.00, false)]
    [InlineData(FeeType.Replacement, FeeStatus.Waived, 10.00, 0.00, false)]
    [InlineData(FeeType.Late, FeeStatus.Cancelled, 10.00, 0.00, false)]
    [InlineData(FeeType.Damage, FeeStatus.Cancelled, 10.00, 0.00, false)]
    [InlineData(FeeType.Replacement, FeeStatus.Cancelled, 10.00, 0.00, false)]
    [InlineData(FeeType.Late, FeeStatus.Partial, 10.00, 10.00, false)]
    [InlineData(FeeType.Late, FeeStatus.Pending, 10.00, 11.00, false)]
    [InlineData(FeeType.Other, FeeStatus.Pending, 10.00, 0.00, true)]
    [InlineData((FeeType)3, FeeStatus.Pending, 10.00, 0.00, true)]
    [InlineData(FeeType.Late, (FeeStatus)99, 10.00, 0.00, false)]
    public void IsBlockingOutstandingFee_AppliesTypeStatusAndRemainingBalance(
        FeeType feeType, FeeStatus status, decimal amount, decimal paidAmount, bool expected)
    {
        Assert.Equal(expected, FeeFinancialStandingRules.IsBlockingOutstandingFee(
            feeType, status, amount, paidAmount));
    }
}
