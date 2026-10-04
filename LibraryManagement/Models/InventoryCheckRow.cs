namespace LibraryManagement.Models;

public sealed class InventoryCheckRow
{
    public int BookId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int TotalCurrent { get; init; }
    public int AvailableCurrent { get; init; }
    public int Borrowed { get; init; }
    public int DamagedUnderRepair { get; init; }
    public int Lost { get; init; }
    public int Retired { get; init; }

    // Stored columns are retained only as legacy snapshots, never circulation authority.
    public int StoredQuantity { get; init; }
    public int StoredAvailable { get; init; }
    public int DerivedQuantity { get; init; }
    public int DerivedAvailable { get; init; }
    public int UnresolvedLegacyLoans { get; init; }
    public int InconsistentCopies { get; init; }
    public bool CounterDrift => StoredQuantity != DerivedQuantity || StoredAvailable != DerivedAvailable;
    public bool NeedsReview => CounterDrift || UnresolvedLegacyLoans > 0 || InconsistentCopies > 0;
}
