namespace LibraryManagement.Repositories;

/// <summary>Read-only access to the Fee module's canonical outstanding balance aggregate.</summary>
public interface IFeeBalanceReader
{
    Task<decimal> GetTotalOutstandingBalanceAsync(CancellationToken cancellationToken = default);
}
