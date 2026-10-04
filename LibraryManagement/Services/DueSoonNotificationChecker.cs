using System.Diagnostics;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public interface IDueSoonNotificationChecker
{
    Task<int> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class DueSoonNotificationChecker : IDueSoonNotificationChecker
{
    private readonly IDueSoonLoanRepository _loans;
    private readonly IApplicationEventDispatcher _events;
    private readonly TimeProvider _timeProvider;

    public DueSoonNotificationChecker(IDueSoonLoanRepository loans, IApplicationEventDispatcher events, TimeProvider? timeProvider = null)
    {
        _loans = loans ?? throw new ArgumentNullException(nameof(loans));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> CheckAsync(CancellationToken cancellationToken = default)
    {
        DateTime targetDate = _timeProvider.GetLocalNow().Date.AddDays(2);
        IReadOnlyList<DueSoonLoan> loans = await _loans.GetActiveLoansDueOnAsync(targetDate, cancellationToken).ConfigureAwait(false);
        int published = 0;
        foreach (var loan in loans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _events.PublishAsync(new LoanDueSoonEvent(loan.BorrowId, loan.ReaderId, loan.BookTitle, loan.DueDate), cancellationToken)
                    .ConfigureAwait(false);
                published++;
            }
            catch (Exception exception)
            {
                Trace.TraceError($"DueSoon notification failed for borrow {loan.BorrowId}: {exception}");
            }
        }
        return published;
    }
}
