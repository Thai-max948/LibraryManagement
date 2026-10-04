using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using System.Diagnostics;
using System.Windows;

namespace LibraryManagement.ViewModels;

public class NotificationViewModel : BaseViewModel, IDisposable
{
    private bool _isCenterOpen;
    private bool _showUnreadOnly;
    private NotificationItemViewModel? _currentToast;
    private readonly INotificationService? _service;
    private string? _loadError;
    public string? LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }

    public ObservableCollection<NotificationItemViewModel> Notifications { get; }
    public ICollectionView FilteredNotifications { get; }
    public int UnreadCount => Notifications.Count(item => !item.IsRead);
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
            FilteredNotifications.Refresh();
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsUnreadSelected));
            OnPropertyChanged(nameof(HasVisibleNotifications));
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
        Notifications = new ObservableCollection<NotificationItemViewModel>(items);
        FilteredNotifications = CollectionViewSource.GetDefaultView(Notifications);
        FilteredNotifications.Filter = item => item is NotificationItemViewModel notification && (!ShowUnreadOnly || !notification.IsRead);
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
        try
        {
            var items = await _service.GetAllAsync();
            Notifications.Clear();
            foreach (var item in items) Notifications.Add(NotificationItemViewModel.FromNotification(item));
            LoadError = null;
        }
        catch (Exception exception)
        {
            LoadError = "Không thể tải thông báo.";
            Trace.TraceError($"Notification load failed: {exception}");
        }
    }

    private async Task MarkAsReadAsync(NotificationItemViewModel item)
    {
        if (item.IsRead) return;
        try
        {
            if (_service != null) await _service.MarkAsReadAsync(item.Id);
            item.IsRead = true;
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
            CurrentToast = item;
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Add();
        else dispatcher.BeginInvoke((Action)Add);
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
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(HasVisibleNotifications));
    }

}
