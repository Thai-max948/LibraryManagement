using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public interface IDashboardService
{
    Task<DashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _repository;
    private readonly IFeeBalanceReader _feeBalances;
    private readonly TimeProvider _timeProvider;

    public DashboardService()
        : this(new DashboardRepository(), new FeeRepository(), TimeProvider.System)
    {
    }

    public DashboardService(
        IDashboardRepository repository,
        IFeeBalanceReader feeBalances,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _feeBalances = feeBalances ?? throw new ArgumentNullException(nameof(feeBalances));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        DateTime today = _timeProvider.GetLocalNow().Date;
        DateTime dueSoonDate = DueSoonDatePolicy.GetTargetDate(today);
        var dateRange = new DashboardDateRange(
            Today: today,
            DueSoonDate: dueSoonDate,
            CirculationStart: today.AddDays(-6),
            CirculationEndExclusive: today.AddDays(1));

        DashboardRepositoryData repositoryData = await _repository
            .GetDashboardDataAsync(dateRange, cancellationToken)
            .ConfigureAwait(false);
        // Fee schema is prepared by LibraryDatabaseStartupMigration before the main UI opens.
        // Query through the Fee repository's canonical aggregate without rerunning DDL on refresh.
        decimal outstandingFees = await _feeBalances
            .GetTotalOutstandingBalanceAsync(cancellationToken)
            .ConfigureAwait(false);

        var activityByDate = repositoryData.CirculationCounts
            .GroupBy(point => point.Date.Date)
            .ToDictionary(
                group => group.Key,
                group => new DashboardCirculationCount(
                    group.Key,
                    checked(group.Sum(point => point.BorrowCount)),
                    checked(group.Sum(point => point.ReturnCount))));

        var circulation = Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(offset - 6))
            .Select(date => activityByDate.TryGetValue(date, out var point)
                ? new DashboardCirculationPoint(date, point.BorrowCount, point.ReturnCount)
                : new DashboardCirculationPoint(date, 0, 0))
            .ToArray();

        return new DashboardData(
            repositoryData.Snapshot with { OutstandingFees = outstandingFees },
            repositoryData.RecentBorrowings,
            repositoryData.RecentReturns,
            circulation);
    }
}
