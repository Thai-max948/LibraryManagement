using LibraryManagement.Models;

namespace LibraryManagement.Repositories;

public interface IDashboardRepository
{
    Task<DashboardRepositoryData> GetDashboardDataAsync(
        DashboardDateRange dateRange,
        CancellationToken cancellationToken = default);
}
