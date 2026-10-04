using LibraryManagement.Models;

namespace LibraryManagement.Repositories;

public interface IDueSoonLoanRepository
{
    Task<IReadOnlyList<DueSoonLoan>> GetActiveLoansDueOnAsync(DateTime targetDate, CancellationToken cancellationToken = default);
}
