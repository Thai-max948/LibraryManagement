using System;
using System.Collections.Generic;

namespace LibraryManagement.Models;

public enum DamageSeverity { Minor = 1, Major = 2 }

// A circulation event carries the authoritative dates. Fee owns overdue-day calculation.
public sealed record ReturnResult(int BorrowId, int ReaderId, int BookId, int BookCopyId,
    DateTime DueDate, DateTime ResolvedAt, ReturnCondition Disposition)
{
    public long? ReturnEventId { get; init; }
    public int? LateDays { get; init; }
    public DamageSeverity? DamageLevel => Disposition switch
    {
        ReturnCondition.Damaged => DamageSeverity.Minor,
        ReturnCondition.NeedsRepair => DamageSeverity.Major,
        _ => null
    };
    public IReadOnlyList<string> FeeWarnings { get; init; } = Array.Empty<string>();
    public int FeesCreated { get; init; }
    public bool IsOverdue => LateDays is > 0;
    public bool IsLost => Disposition == ReturnCondition.Lost;
    public DateTime? ReturnedAt => IsLost ? null : ResolvedAt;
    public bool RequiresFeeProcessing => IsOverdue || Disposition != ReturnCondition.Normal;

    public static ReturnResult FromLoan(BorrowRecord loan, DateTime resolvedAt, ReturnCondition disposition) =>
        new(loan.BorrowId, loan.ReaderId, loan.BookId,
            loan.BookCopyId ?? throw new InvalidOperationException("Loan must reference a physical copy."),
            loan.DueDate, resolvedAt, disposition);
}
