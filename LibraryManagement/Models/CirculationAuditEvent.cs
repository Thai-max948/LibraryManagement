using System;

namespace LibraryManagement.Models;

public enum CirculationAuditEventType
{
    BorrowCreated,
    LegacyCopyMapped,
    Returned,
    ReturnedNormal,
    ReturnedDamaged,
    ReturnedNeedsRepair,
    MarkedLost
}

public sealed class CirculationAuditEvent
{
    public long AuditEventId { get; set; }
    public int BorrowId { get; set; }
    public CirculationAuditEventType EventType { get; set; }
    public int? ActorUserId { get; set; }
    public string ActorNameSnapshot { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public int? BookCopyId { get; set; }
    public string? Note { get; set; }
}
