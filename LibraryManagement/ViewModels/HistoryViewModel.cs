using System.Collections.ObjectModel;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels;

public class HistoryViewModel : BaseViewModel
{
    private readonly HistoryService _historyService;
    private string _searchText = string.Empty;
    private string _selectedStatus = "All";
    private string _selectedDateFilter = "Borrow date";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _pageNumber = 1;
    private int _totalPages = 1;
    private int _totalCount;
    private int _overdueCount;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private HistoryRow? _selectedRecord;

    public ObservableCollection<HistoryRow> Records { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "All", "Borrowing", "Returned", "Lost", "Overdue" };
    public ObservableCollection<string> DateFilterOptions { get; } = new() { "Borrow date", "Return date" };

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ResetAndLoad(); }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetProperty(ref _selectedStatus, value)) ResetAndLoad(); }
    }

    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set { if (SetProperty(ref _selectedDateFilter, value)) ResetAndLoad(); }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set { if (SetProperty(ref _fromDate, value)) ResetAndLoad(); }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set { if (SetProperty(ref _toDate, value)) ResetAndLoad(); }
    }

    public int PageNumber
    {
        get => _pageNumber;
        private set { if (SetProperty(ref _pageNumber, value)) OnPropertyChanged(nameof(PageSummary)); }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set { if (SetProperty(ref _totalPages, value)) OnPropertyChanged(nameof(PageSummary)); }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set { if (SetProperty(ref _totalCount, value)) OnPropertyChanged(nameof(PageSummary)); }
    }

    public int OverdueCount
    {
        get => _overdueCount;
        private set { if (SetProperty(ref _overdueCount, value)) OnPropertyChanged(nameof(HasOverdue)); }
    }

    public bool HasOverdue => OverdueCount > 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasRecords => Records.Count > 0;
    public string PageSummary => $"Page {PageNumber} of {TotalPages} · {TotalCount} records";

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public HistoryRow? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand FilterOverdueCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public HistoryViewModel() : this(new HistoryService()) { }

    public HistoryViewModel(HistoryService historyService)
    {
        _historyService = historyService;
        RefreshCommand = new RelayCommand(Load);
        FilterOverdueCommand = new RelayCommand(() => SelectedStatus = "Overdue");
        PreviousPageCommand = new RelayCommand(() => ChangePage(PageNumber - 1), () => HasPreviousPage);
        NextPageCommand = new RelayCommand(() => ChangePage(PageNumber + 1), () => HasNextPage);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        Load();
    }

    private void ResetAndLoad()
    {
        PageNumber = 1;
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        Load();
    }

    private void ChangePage(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > TotalPages) return;
        PageNumber = pageNumber;
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        Load();
    }

    private void ClearFilters()
    {
        _searchText = string.Empty;
        _selectedStatus = "All";
        _selectedDateFilter = "Borrow date";
        _fromDate = null;
        _toDate = null;
        PageNumber = 1;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(SelectedDateFilter));
        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        Load();
    }

    private void Load()
    {
        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
        {
            ErrorMessage = "From date must be on or before To date.";
            Records.Clear();
            TotalCount = 0;
            TotalPages = 1;
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
            SelectedRecord = null;
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var page = _historyService.GetPage(new HistoryQuery
            {
                SearchText = SearchText,
                Status = SelectedStatus,
                FromDate = FromDate,
                ToDate = ToDate,
                DateFilter = SelectedDateFilter == "Return date" ? HistoryDateFilter.ReturnDate : HistoryDateFilter.BorrowDate,
                PageNumber = PageNumber,
                PageSize = HistoryService.DefaultPageSize
            });

            Records.Clear();
            foreach (var item in page.Records)
            {
                Records.Add(new HistoryRow
                {
                    BorrowId = item.BorrowId,
                    ReaderName = item.ReaderName + (item.IsReaderDeleted ? " (deleted)" : string.Empty),
                    BookTitle = item.BookTitle + (item.IsLegacyRecord ? " (legacy record)" : string.Empty),
                    CopyLabel = item.BookCopyId.HasValue
                        ? item.Barcode ?? $"Copy #{item.BookCopyId} · barcode unavailable"
                        : "Legacy / unknown copy",
                    BorrowDate = item.BorrowDate.ToString("dd/MM/yyyy"),
                    DueDate = item.DueDate.ToString("dd/MM/yyyy"),
                    ReturnDate = item.ReturnDate?.ToString("dd/MM/yyyy") ?? "—",
                    Status = item.Status,
                    StatusDisplay = item.IsOverdue ? $"Overdue ({item.OverdueDays}d)" : item.Status,
                    OverdueText = item.IsOverdue
                        ? $"Overdue {item.OverdueDays} days (due {item.DueDate:dd/MM/yyyy})"
                        : item.Status == "Borrowing" ? $"Borrowed (due {item.DueDate:dd/MM/yyyy})" : item.Status,
                    IsOverdue = item.IsOverdue,
                    OverdueDays = item.OverdueDays,
                    ActionDate = item.ActionDate,
                    AuditTimeline = FormatTimeline(item.Events)
                });
            }

            TotalCount = page.TotalCount;
            TotalPages = page.TotalPages;
            OverdueCount = page.OverdueCount;
            if (PageNumber > TotalPages)
            {
                PageNumber = TotalPages;
                Load();
                return;
            }
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
            SelectedRecord = Records.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not load History: " + ex.Message;
            Records.Clear();
            TotalCount = 0;
            TotalPages = 1;
            SelectedRecord = null;
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatTimeline(IReadOnlyList<CirculationAuditEvent> events)
    {
        if (events.Count == 0) return "No event details recorded for this legacy circulation.";
        return string.Join(Environment.NewLine, events.Select(item =>
            $"{item.OccurredAt:dd/MM/yyyy HH:mm} · {EventLabel(item.EventType)} · {item.ActorNameSnapshot}" +
            (string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" · {item.Note}")));
    }

    private static string EventLabel(CirculationAuditEventType eventType) => eventType switch
    {
        CirculationAuditEventType.BorrowCreated => "Borrowed",
        CirculationAuditEventType.LegacyCopyMapped => "Copy verified",
        CirculationAuditEventType.Returned => "Returned (condition unknown)",
        CirculationAuditEventType.ReturnedNormal => "Returned",
        CirculationAuditEventType.ReturnedDamaged => "Returned damaged",
        CirculationAuditEventType.ReturnedNeedsRepair => "Returned for repair",
        CirculationAuditEventType.MarkedLost => "Marked lost",
        _ => eventType.ToString()
    };
}

public sealed class HistoryRow
{
    public int BorrowId { get; init; }
    public string ReaderName { get; init; } = string.Empty;
    public string BookTitle { get; init; } = string.Empty;
    public string CopyLabel { get; init; } = string.Empty;
    public string BorrowDate { get; init; } = string.Empty;
    public string DueDate { get; init; } = string.Empty;
    public string ReturnDate { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StatusDisplay { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }
    public int OverdueDays { get; init; }
    public string OverdueText { get; init; } = string.Empty;
    public DateTime ActionDate { get; init; }
    public string AuditTimeline { get; init; } = string.Empty;
}
