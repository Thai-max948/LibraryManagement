using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels;

public sealed record FeeStatusFilter(string Label, FeeStatus? Value);
public sealed record FeeTypeFilter(string Label, FeeType? Value);

public sealed class FeesViewModel : BaseViewModel
{
    private readonly IFeeService _service;
    private bool _isLoading;
    private string _searchText = string.Empty;
    private string _statusMessage = "Đang tải dữ liệu phí.";
    private string _errorMessage = string.Empty;
    private string _paymentAmount = string.Empty;
    private string _paymentNote = string.Empty;
    private string _waiveReason = string.Empty;
    private string _cancelReason = string.Empty;
    private Fee? _selectedFee;
    private FeeStatusFilter _selectedStatus;
    private FeeTypeFilter _selectedType;
    private decimal _totalOutstanding;
    private decimal _readerOutstanding;
    private int _page = 1;
    private int _totalCount;
    private int _pageSize = 50;
    private readonly FeePaymentAttemptKey _paymentAttemptKey = new();

    public ObservableCollection<Fee> Fees { get; } = new();
    public ObservableCollection<FeePayment> PaymentHistory { get; } = new();
    public IReadOnlyList<FeeStatusFilter> StatusFilters { get; } = new FeeStatusFilter[]
    {
        new("Tất cả trạng thái", null), new("Chưa thanh toán", FeeStatus.Pending),
        new("Thanh toán một phần", FeeStatus.Partial), new("Đã thanh toán", FeeStatus.Paid),
        new("Đã miễn", FeeStatus.Waived), new("Đã hủy", FeeStatus.Cancelled)
    };
    public IReadOnlyList<FeeTypeFilter> TypeFilters { get; } = new FeeTypeFilter[]
    {
        new("Tất cả loại phí", null), new("Mượn sách", FeeType.Borrow), new("Trễ hạn", FeeType.Late),
        new("Hư hỏng", FeeType.Damage), new("Đền bù sách mất", FeeType.Replacement), new("Khác", FeeType.Other)
    };

    public RelayCommand SearchCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand RecordPaymentCommand { get; }
    public RelayCommand WaiveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public FeesViewModel() : this(new FeeService()) { }

    public FeesViewModel(IFeeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _selectedStatus = StatusFilters[0];
        _selectedType = TypeFilters[0];
        SearchCommand = new RelayCommand(async _ => await SearchAsync());
        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        PreviousPageCommand = new RelayCommand(async _ => await ChangePageAsync(-1), _ => Page > 1 && !IsLoading);
        NextPageCommand = new RelayCommand(async _ => await ChangePageAsync(1), _ => Page < PageCount && !IsLoading);
        RecordPaymentCommand = new RelayCommand(async _ => await RecordPaymentAsync(), _ => CanRecordPayment);
        WaiveCommand = new RelayCommand(async _ => await WaiveAsync(), _ => CanWaive);
        CancelCommand = new RelayCommand(async _ => await CancelAsync(), _ => CanCancel);
    }

    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyCommands(); } }
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string PaymentAmount { get => _paymentAmount; set { if (SetProperty(ref _paymentAmount, value)) NotifyCommands(); } }
    public string PaymentNote { get => _paymentNote; set => SetProperty(ref _paymentNote, value); }
    public string WaiveReason { get => _waiveReason; set { if (SetProperty(ref _waiveReason, value)) NotifyCommands(); } }
    public string CancelReason { get => _cancelReason; set { if (SetProperty(ref _cancelReason, value)) NotifyCommands(); } }
    public Fee? SelectedFee
    {
        get => _selectedFee;
        set
        {
            if (!SetProperty(ref _selectedFee, value)) return;
            OnPropertyChanged(nameof(CanRecordPayment));
            OnPropertyChanged(nameof(CanWaive));
            OnPropertyChanged(nameof(CanCancel));
            NotifyCommands();
            _ = LoadSelectedFeeDetailsAsync(value);
        }
    }
    public FeeStatusFilter SelectedStatus { get => _selectedStatus; set { if (SetProperty(ref _selectedStatus, value)) _ = SearchAsync(); } }
    public FeeTypeFilter SelectedType { get => _selectedType; set { if (SetProperty(ref _selectedType, value)) _ = SearchAsync(); } }
    public decimal TotalOutstanding { get => _totalOutstanding; private set => SetProperty(ref _totalOutstanding, value); }
    public decimal ReaderOutstanding { get => _readerOutstanding; private set => SetProperty(ref _readerOutstanding, value); }
    public int Page { get => _page; private set { if (SetProperty(ref _page, value)) { OnPropertyChanged(nameof(PageText)); NotifyCommands(); } } }
    public int TotalCount { get => _totalCount; private set { if (SetProperty(ref _totalCount, value)) { OnPropertyChanged(nameof(PageCount)); OnPropertyChanged(nameof(PageText)); NotifyCommands(); } } }
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
    public string PageText => TotalCount == 0 ? "Không có khoản phí" : $"Trang {Page}/{PageCount} · {TotalCount} khoản phí";
    public int PageSize { get => _pageSize; }
    public bool HasNoFees => !IsLoading && Fees.Count == 0 && string.IsNullOrEmpty(ErrorMessage);
    public bool CanRecordPayment => !IsLoading && SelectedFee is { Remaining: > 0m } fee && FeeStateRules.CanRecordPayment(fee);
    public bool CanWaive => !IsLoading && SelectedFee is { } fee && FeeStateRules.CanWaive(fee) && !string.IsNullOrWhiteSpace(WaiveReason);
    public bool CanCancel => !IsLoading && SelectedFee is { } fee && FeeStateRules.CanCancel(fee) && !string.IsNullOrWhiteSpace(CancelReason);

    public Task LoadAsync() => LoadAsync(allowBusy: false);

    private async Task LoadAsync(bool allowBusy)
    {
        if (IsLoading && !allowBusy) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            FeePage page = await _service.GetFeesAsync(new FeeSearchQuery(SearchText, SelectedStatus.Value,
                SelectedType.Value, Page, PageSize));
            int? selectedId = SelectedFee?.FeeId;
            Fees.Clear();
            foreach (Fee fee in page.Items) Fees.Add(fee);
            TotalCount = page.TotalCount;
            Page = page.Page;
            TotalOutstanding = await _service.GetTotalOutstandingBalanceAsync();
            SelectedFee = selectedId.HasValue ? Fees.FirstOrDefault(f => f.FeeId == selectedId) : Fees.FirstOrDefault();
            StatusMessage = Fees.Count == 0 ? "Không tìm thấy khoản phí phù hợp." : $"Đang hiển thị {Fees.Count} khoản phí.";
            OnPropertyChanged(nameof(HasNoFees));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Không thể tải danh sách phí: " + ex.Message;
            StatusMessage = string.Empty;
        }
        finally { IsLoading = false; }
    }

    private async Task SearchAsync()
    {
        Page = 1;
        await LoadAsync();
    }

    private async Task ChangePageAsync(int delta)
    {
        int next = Page + delta;
        if (next < 1 || next > PageCount) return;
        Page = next;
        await LoadAsync();
    }

    private async Task LoadSelectedFeeDetailsAsync(Fee? fee)
    {
        PaymentHistory.Clear();
        ReaderOutstanding = 0m;
        if (fee is null) { OnPropertyChanged(nameof(HasSelectedFee)); return; }
        int id = fee.FeeId;
        try
        {
            var history = await _service.GetPaymentHistoryAsync(id);
            decimal outstanding = await _service.GetOutstandingBalanceAsync(fee.ReaderId);
            if (SelectedFee?.FeeId != id) return;
            foreach (FeePayment payment in history) PaymentHistory.Add(payment);
            ReaderOutstanding = outstanding;
        }
        catch (Exception ex)
        {
            if (SelectedFee?.FeeId == id) ErrorMessage = "Không tải được chi tiết thanh toán: " + ex.Message;
        }
        OnPropertyChanged(nameof(HasSelectedFee));
    }

    public bool HasSelectedFee => SelectedFee is not null;

    private async Task RecordPaymentAsync()
    {
        if (SelectedFee is null || !CanRecordPayment) return;
        if (!decimal.TryParse(PaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount) &&
            !decimal.TryParse(PaymentAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        { ErrorMessage = "Số tiền thanh toán không hợp lệ."; return; }

        string note = PaymentNote.Trim();
        Guid operationId = _paymentAttemptKey.GetOrCreate(SelectedFee.FeeId, amount, note);

        await RunCommandAsync(async () =>
        {
            await _service.RecordPaymentAsync(new RecordFeePaymentRequest(SelectedFee.FeeId, amount, operationId,
                string.IsNullOrEmpty(note) ? null : note));
            _paymentAttemptKey.Complete();
            PaymentAmount = string.Empty;
            PaymentNote = string.Empty;
            StatusMessage = "Đã ghi nhận thanh toán.";
            await LoadAsync(allowBusy: true);
        });
    }

    private async Task WaiveAsync()
    {
        if (SelectedFee is null || !CanWaive) return;
        if (MessageBox.Show($"Miễn khoản phí #{SelectedFee.FeeId} với lý do đã nhập?", "Xác nhận miễn phí",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int id = SelectedFee.FeeId;
        await RunCommandAsync(async () =>
        {
            await _service.WaiveAsync(id, WaiveReason);
            WaiveReason = string.Empty;
            StatusMessage = "Đã ghi nhận miễn phí.";
            await LoadAsync(allowBusy: true);
        });
    }

    private async Task CancelAsync()
    {
        if (SelectedFee is null || !CanCancel) return;
        if (MessageBox.Show($"Hủy khoản phí #{SelectedFee.FeeId}? Thao tác sẽ được lưu vào audit.", "Xác nhận hủy phí",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int id = SelectedFee.FeeId;
        await RunCommandAsync(async () =>
        {
            await _service.CancelAsync(id, CancelReason);
            CancelReason = string.Empty;
            StatusMessage = "Đã ghi nhận hủy phí.";
            await LoadAsync(allowBusy: true);
        });
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try { await action(); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanRecordPayment));
        OnPropertyChanged(nameof(CanWaive));
        OnPropertyChanged(nameof(CanCancel));
        CommandManager.InvalidateRequerySuggested();
    }
}
