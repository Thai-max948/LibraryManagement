using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public class HistoryService
{
    public const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private static readonly HashSet<string> SupportedStatuses = new(StringComparer.Ordinal)
    {
        "All", "Borrowing", "Returned", "Lost", "Overdue"
    };

    private readonly HistoryRepository _historyRepository;
    private readonly CirculationAuditRepository _auditRepository;
    private readonly TimeProvider _timeProvider;

    public HistoryService() : this(new HistoryRepository(), new CirculationAuditRepository()) { }

    public HistoryService(HistoryRepository historyRepository, CirculationAuditRepository auditRepository,
        TimeProvider? timeProvider = null)
    {
        _historyRepository = historyRepository;
        _auditRepository = auditRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public virtual HistoryPage GetPage(HistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value.Date > query.ToDate.Value.Date)
            throw new ArgumentException("From date must be on or before To date.", nameof(query));
        if (!SupportedStatuses.Contains(query.Status))
            throw new ArgumentException("Unsupported History status filter.", nameof(query));

        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        int pageNumber = Math.Max(1, query.PageNumber);
        var normalizedQuery = new HistoryQuery
        {
            SearchText = query.SearchText?.Trim() ?? string.Empty,
            Status = query.Status,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var page = _historyRepository.GetPage(normalizedQuery, _timeProvider.GetLocalNow().DateTime.Date);
        if (page.Records.Count > 0)
        {
            var eventsByBorrow = _auditRepository.GetByBorrowIds(page.Records.Select(record => record.BorrowId))
                .GroupBy(item => item.BorrowId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CirculationAuditEvent>)group.ToList());
            foreach (var record in page.Records)
                record.Events = eventsByBorrow.GetValueOrDefault(record.BorrowId, Array.Empty<CirculationAuditEvent>());
        }
        return page;
    }
}
