using System.Diagnostics;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels;

public sealed class DashboardViewModel : BaseViewModel
{
    private readonly IDashboardService _dashboardService;
    private readonly IUserDialogService _dialogService;
    private readonly TimeProvider _timeProvider;
    private DashboardData? _data;
    private bool _isLoading;
    private bool _hasLoaded;
    private DateTimeOffset? _lastUpdated;
    private int _refreshGate;

    public DashboardViewModel(IUserDialogService dialogService)
        : this(new DashboardService(), dialogService, TimeProvider.System)
    {
    }

    public DashboardViewModel(
        IDashboardService dashboardService,
        IUserDialogService dialogService,
        TimeProvider? timeProvider = null)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        InitialLoadTask = RefreshAsync();
    }

    public Task InitialLoadTask { get; }

    public ICommand RefreshCommand { get; }

    public DashboardData? Data => _data;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                OnPropertyChanged(nameof(DashboardLoadMessage));
                OnPropertyChanged(nameof(RecentBorrowingsEmptyMessage));
                OnPropertyChanged(nameof(RecentReturningsEmptyMessage));
            }
        }
    }

    public bool HasLoaded
    {
        get => _hasLoaded;
        private set
        {
            if (SetProperty(ref _hasLoaded, value))
            {
                OnPropertyChanged(nameof(DashboardLoadMessage));
                OnPropertyChanged(nameof(RecentBorrowingsEmptyMessage));
                OnPropertyChanged(nameof(RecentReturningsEmptyMessage));
            }
        }
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        private set
        {
            if (SetProperty(ref _lastUpdated, value))
                OnPropertyChanged(nameof(LastUpdatedText));
        }
    }

    public string LastUpdatedText => LastUpdated?.ToString("HH:mm") ?? "—";
    public string RefreshButtonText => IsLoading ? "Refreshing…" : "Refresh";
    public string DashboardLoadMessage => HasLoaded
        ? string.Empty
        : IsLoading ? "Loading dashboard data…" : "Dashboard data is unavailable.";

    public int? ActiveReaders => _data?.Snapshot.ActiveReaders;
    public int? TotalCopies => _data?.Snapshot.TotalCopies;
    public int? AvailableCopies => _data?.Snapshot.AvailableCopies;
    public int? BorrowedCopies => _data?.Snapshot.BorrowedCopies;
    public int? DamagedCopies => _data?.Snapshot.DamagedCopies;
    public int? UnderRepairCopies => _data?.Snapshot.UnderRepairCopies;
    public int? DamagedOrRepairCopies => _data?.Snapshot.DamagedOrRepairCopies;
    public int? LostCopies => _data?.Snapshot.LostCopies;
    public int? RetiredCopies => _data?.Snapshot.RetiredCopies;
    public int? ActiveLoans => _data?.Snapshot.ActiveLoans;
    public int? DueSoonLoans => _data?.Snapshot.DueSoonLoans;
    public int? OverdueLoans => _data?.Snapshot.OverdueLoans;
    public decimal? OutstandingFees => _data?.Snapshot.OutstandingFees;
    public string OutstandingFeesText => OutstandingFees is decimal value ? $"{value:N0} ₫" : "—";

    public IReadOnlyList<DashboardRecentBorrow> RecentBorrowings =>
        _data?.RecentBorrowings ?? Array.Empty<DashboardRecentBorrow>();

    public IReadOnlyList<DashboardRecentReturn> RecentReturnings =>
        _data?.RecentReturns ?? Array.Empty<DashboardRecentReturn>();

    public string RecentBorrowingsEmptyMessage => !HasLoaded
        ? DashboardLoadMessage
        : "No recent borrow records to show.";

    public string RecentReturningsEmptyMessage => !HasLoaded
        ? DashboardLoadMessage
        : "No recent return records to show.";

    public IReadOnlyList<DashboardCirculationPoint> Circulation =>
        _data?.Circulation ?? Array.Empty<DashboardCirculationPoint>();

    public double CirculationMaximum => _data is null
        ? 1
        : Math.Max(1, _data.Circulation
            .Select(point => Math.Max(point.BorrowCount, point.ReturnCount))
            .DefaultIfEmpty()
            .Max());

    // Backward-compatible aliases for any existing bindings or callers.
    public int? TotalBooks => TotalCopies;
    public int? TotalReaders => ActiveReaders;
    public int? AvailableBooks => AvailableCopies;
    public int? CurrentlyBorrowed => ActiveLoans;
    public int? OverdueBooks => OverdueLoans;

    public async Task RefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _refreshGate, 1, 0) != 0)
            return;

        IsLoading = true;
        try
        {
            DashboardData refreshed = await _dashboardService.GetDashboardDataAsync();
            _data = refreshed;
            OnPropertyChanged(nameof(Data));
            OnPropertyChanged(nameof(ActiveReaders));
            OnPropertyChanged(nameof(TotalCopies));
            OnPropertyChanged(nameof(AvailableCopies));
            OnPropertyChanged(nameof(BorrowedCopies));
            OnPropertyChanged(nameof(DamagedCopies));
            OnPropertyChanged(nameof(UnderRepairCopies));
            OnPropertyChanged(nameof(DamagedOrRepairCopies));
            OnPropertyChanged(nameof(LostCopies));
            OnPropertyChanged(nameof(RetiredCopies));
            OnPropertyChanged(nameof(ActiveLoans));
            OnPropertyChanged(nameof(DueSoonLoans));
            OnPropertyChanged(nameof(OverdueLoans));
            OnPropertyChanged(nameof(OutstandingFees));
            OnPropertyChanged(nameof(OutstandingFeesText));
            OnPropertyChanged(nameof(RecentBorrowings));
            OnPropertyChanged(nameof(RecentReturnings));
            OnPropertyChanged(nameof(RecentBorrowingsEmptyMessage));
            OnPropertyChanged(nameof(RecentReturningsEmptyMessage));
            OnPropertyChanged(nameof(Circulation));
            OnPropertyChanged(nameof(CirculationMaximum));
            OnPropertyChanged(nameof(TotalBooks));
            OnPropertyChanged(nameof(TotalReaders));
            OnPropertyChanged(nameof(AvailableBooks));
            OnPropertyChanged(nameof(CurrentlyBorrowed));
            OnPropertyChanged(nameof(OverdueBooks));

            LastUpdated = _timeProvider.GetLocalNow();
            HasLoaded = true;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Dashboard refresh failed: {exception}");
            _dialogService.ShowError("Không thể tải dữ liệu Dashboard: " + exception.Message, "Lỗi");
        }
        finally
        {
            IsLoading = false;
            Volatile.Write(ref _refreshGate, 0);
        }
    }
}
