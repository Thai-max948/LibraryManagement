using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using System.Diagnostics;

namespace LibraryManagement.ViewModels;

public class NotificationViewModel : BaseViewModel, IDisposable
{
    private bool _isCenterOpen;
    private bool _showUnreadOnly;
    private NotificationItemViewModel? _currentToast;
    private readonly INotificationService? _service;
    private readonly Dispatcher _dispatcher;
    private string? _loadError;
    private int _unreadCount;
    private int _loadVersion;
    private int _countMutationVersion;
    private int _notificationMutationVersion;
    public string? LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }

    public ObservableCollection<NotificationItemViewModel> Notifications { get; }
    public ICollectionView FilteredNotifications { get; }
    public int UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (SetProperty(ref _unreadCount, Math.Max(0, value)))
                OnPropertyChanged(nameof(HasUnread));
        }
    }
    public bool HasUnread => UnreadCount > 0;
    public bool HasVisibleNotifications => !FilteredNotifications.IsEmpty;
    public bool IsAllSelected => !ShowUnreadOnly;
    public bool IsUnreadSelected => ShowUnreadOnly;
    public bool IsCenterOpen { get => _isCenterOpen; set => SetProperty(ref _isCenterOpen, value); }
    public bool ShowUnreadOnly
    {
        get => _showUnreadOnly;
        private set
        {
            if (!SetProperty(ref _showUnreadOnly, value)) return;
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsUnreadSelected));
            UpdateCounts();
            if (_service != null) _ = LoadAsync();
        }
    }
    public NotificationItemViewModel? CurrentToast
    {
        get => _currentToast;
        private set
        {
            if (SetProperty(ref _currentToast, value)) OnPropertyChanged(nameof(IsToastVisible));
        }
    }
    public bool IsToastVisible => CurrentToast != null;

    public ICommand ToggleCenterCommand { get; }
    public ICommand CloseCenterCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ShowUnreadCommand { get; }
    public ICommand MarkAllAsReadCommand { get; }
    public ICommand OpenNotificationCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CloseToastCommand { get; }

    public NotificationViewModel() : this(NotificationRuntime.Service) { }

    public NotificationViewModel(INotificationService service) : this(Array.Empty<NotificationItemViewModel>())
    {
        _service = service;
        _service.Created += OnCreated;
    }

    public NotificationViewModel(IEnumerable<NotificationItemViewModel> items)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Notifications = new ObservableCollection<NotificationItemViewModel>(items);
        _unreadCount = Notifications.Count(item => !item.IsRead);
        FilteredNotifications = CollectionViewSource.GetDefaultView(Notifications);
        FilteredNotifications.Filter = item => item is NotificationItemViewModel;
        foreach (var item in Notifications) item.PropertyChanged += OnItemChanged;
        Notifications.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (NotificationItemViewModel item in args.OldItems) item.PropertyChanged -= OnItemChanged;
            if (args.NewItems != null)
                foreach (NotificationItemViewModel item in args.NewItems) item.PropertyChanged += OnItemChanged;
            UpdateCounts();
        };

        ToggleCenterCommand = new RelayCommand(() => IsCenterOpen = !IsCenterOpen);
        CloseCenterCommand = new RelayCommand(() => IsCenterOpen = false);
        ShowAllCommand = new RelayCommand(() => ShowUnreadOnly = false);
        ShowUnreadCommand = new RelayCommand(() => ShowUnreadOnly = true);
        MarkAllAsReadCommand = new RelayCommand(async () => await MarkAllAsReadAsync());
        OpenNotificationCommand = new RelayCommand(async value =>
        {
            if (value is NotificationItemViewModel item) await MarkAsReadAsync(item);
        });
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        CloseToastCommand = new RelayCommand(() => CurrentToast = null);
    }

    public async Task LoadAsync()
    {
        if (_service == null) return;

        int requestVersion = ++_loadVersion;
        bool unreadOnly = ShowUnreadOnly;
        int countMutationVersion = _countMutationVersion;
        int notificationMutationVersion = _notificationMutationVersion;
        Task<NotificationPage>? pageTask = null;
        Task<int>? unreadCountTask = null;
        Exception? pageFailure = null;
        Exception? countFailure = null;

        try
        {
            pageTask = _service.GetPageAsync(new NotificationPageQuery(
                unreadOnly: unreadOnly,
                pageNumber: 1,
                pageSize: NotificationPageQuery.DefaultPageSize));
        }
        catch (Exception exception)
        {
            pageFailure = exception;
        }

        try
        {
            unreadCountTask = _service.GetUnreadCountAsync();
        }
        catch (Exception exception)
        {
            countFailure = exception;
        }

        NotificationPage? page = null;
        int? unreadCount = null;
        if (pageTask != null)
        {
            try { page = await pageTask; }
            catch (Exception exception) { pageFailure = exception; }
        }
        if (unreadCountTask != null)
        {
            try { unreadCount = await unreadCountTask; }
            catch (Exception exception) { countFailure = exception; }
        }

        if (requestVersion != _loadVersion) return;

        if (page != null && notificationMutationVersion == _notificationMutationVersion && unreadOnly == ShowUnreadOnly)
        {
            Notifications.Clear();
            foreach (var item in page.Items.Take(NotificationPageQuery.DefaultPageSize))
                Notifications.Add(NotificationItemViewModel.FromNotification(item));
        }

        if (unreadCount.HasValue && countMutationVersion == _countMutationVersion)
            UnreadCount = unreadCount.Value;

        if (pageFailure == null && countFailure == null)
            LoadError = null;
        else
        {
            LoadError = "Không thể tải thông báo.";
            if (pageFailure != null) Trace.TraceError($"Notification page load failed: {pageFailure}");
            if (countFailure != null) Trace.TraceError($"Notification unread count load failed: {countFailure}");
        }
    }

    private async Task MarkAsReadAsync(NotificationItemViewModel item)
    {
        if (item.IsRead) return;
        bool wasUnread = !item.IsRead;
        try
        {
            if (_service != null) await _service.MarkAsReadAsync(item.Id);
            item.IsRead = true;
            _notificationMutationVersion++;
            if (wasUnread)
            {
                _countMutationVersion++;
                UnreadCount--;
            }
            if (ShowUnreadOnly) Notifications.Remove(item);
            LoadError = null;
        }
        catch (Exception exception)
        {
            LoadError = "Không thể đánh dấu thông báo đã đọc.";
            Trace.TraceError($"Notification update failed: {exception}");
        }
    }

    private async Task MarkAllAsReadAsync()
    {
        try
        {
            if (_service != null) await _service.MarkAllAsReadAsync();
            _notificationMutationVersion++;
            _countMutationVersion++;
            UnreadCount = 0;
            if (ShowUnreadOnly)
                Notifications.Clear();
            else
                foreach (var item in Notifications) item.IsRead = true;
            LoadError = null;
        }
        catch (Exception exception)
        {
            LoadError = "Không thể đánh dấu tất cả đã đọc.";
            Trace.TraceError($"Notification update failed: {exception}");
        }
    }

    private void OnCreated(Notification notification)
    {
        void Add()
        {
            var item = NotificationItemViewModel.FromNotification(notification);
            Notifications.Insert(0, item);
            _notificationMutationVersion++;
            while (Notifications.Count > NotificationPageQuery.DefaultPageSize)
                Notifications.RemoveAt(Notifications.Count - 1);
            if (!notification.IsRead)
            {
                _countMutationVersion++;
                UnreadCount++;
            }
            CurrentToast = item;
        }
        if (_dispatcher.CheckAccess()) Add();
        else _dispatcher.BeginInvoke((Action)Add);
    }

    public void Dispose()
    {
        if (_service != null) _service.Created -= OnCreated;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NotificationItemViewModel.IsRead)) UpdateCounts();
    }

    private void UpdateCounts()
    {
        FilteredNotifications.Refresh();
        OnPropertyChanged(nameof(HasVisibleNotifications));
    }

}
