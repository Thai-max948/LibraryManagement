using System.Diagnostics;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services;

public enum BusinessAction
{
    BookCreated, BookUpdated, BookArchived,
    ReaderCreated, ReaderUpdated, ReaderSuspended, ReaderActivated,
    BorrowCreated, ReturnCompleted, ReturnDamaged,
    FeeCreated, FeePaid, FeeWaived
}

public sealed record BusinessActionCompleted(BusinessAction Action, string EntityId, string? DisplayName = null);
public sealed record LoanDueSoonEvent(int BorrowId, int ReaderId, string BookTitle, DateTime DueDate);

public interface IApplicationEventDispatcher
{
    Task PublishAsync(LoanDueSoonEvent applicationEvent, CancellationToken cancellationToken = default);
}

public interface IApplicationEventHandler<in TEvent>
{
    Task HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken = default);
}

public sealed class NotificationEventHandler : IApplicationEventHandler<BusinessActionCompleted>
{
    private readonly INotificationService _service;
    public NotificationEventHandler(INotificationService service) => _service = service;

    public async Task HandleAsync(BusinessActionCompleted applicationEvent, CancellationToken cancellationToken = default)
    {
        var e = applicationEvent;
        string label = string.IsNullOrWhiteSpace(e.DisplayName) ? $"#{e.EntityId}" : e.DisplayName;
        var (title, message, type, module, entityType) = e.Action switch
        {
            BusinessAction.BookCreated => ("Đã thêm sách", $"Sách {label} đã được thêm.", NotificationType.Success, "Book", "Book"),
            BusinessAction.BookUpdated => ("Đã cập nhật sách", $"Sách {label} đã được cập nhật.", NotificationType.Info, "Book", "Book"),
            BusinessAction.BookArchived => ("Đã lưu trữ sách", $"Sách {label} đã được lưu trữ.", NotificationType.Info, "Book", "Book"),
            BusinessAction.ReaderCreated => ("Đã thêm độc giả", $"Độc giả {label} đã được tạo.", NotificationType.Success, "Reader", "Reader"),
            BusinessAction.ReaderUpdated => ("Đã cập nhật độc giả", $"Độc giả {label} đã được cập nhật.", NotificationType.Info, "Reader", "Reader"),
            BusinessAction.ReaderSuspended => ("Đã tạm khóa độc giả", $"Độc giả {label} đã bị tạm khóa.", NotificationType.Warning, "Reader", "Reader"),
            BusinessAction.ReaderActivated => ("Đã kích hoạt độc giả", $"Độc giả {label} đã được kích hoạt.", NotificationType.Success, "Reader", "Reader"),
            BusinessAction.BorrowCreated => ("Mượn sách thành công", $"Phiếu mượn {label} đã được tạo.", NotificationType.Success, "Borrow", "BorrowRecord"),
            BusinessAction.ReturnCompleted => ("Đã xử lý trả sách", $"Phiếu mượn {label} đã được xử lý.", NotificationType.Success, "Return", "BorrowRecord"),
            BusinessAction.ReturnDamaged => ("Sách trả bị hư hỏng", $"Phiếu mượn {label} được trả với tình trạng hư hỏng.", NotificationType.Warning, "Return", "BorrowRecord"),
            BusinessAction.FeeCreated => ("Đã tạo khoản phí", $"Khoản phí {label} đã được tạo.", NotificationType.Info, "Fee", "Fee"),
            BusinessAction.FeePaid => ("Đã ghi nhận thanh toán", $"Khoản phí {label} đã được ghi nhận thanh toán.", NotificationType.Success, "Fee", "Fee"),
            BusinessAction.FeeWaived => ("Đã miễn phí", $"Khoản phí {label} đã được miễn.", NotificationType.Info, "Fee", "Fee"),
            _ => throw new ArgumentOutOfRangeException(nameof(applicationEvent))
        };
        await _service.NotifyAsync(title, message, type, module, entityType, e.EntityId, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class LoanDueSoonNotificationHandler : IApplicationEventHandler<LoanDueSoonEvent>
{
    private readonly INotificationService _service;
    public LoanDueSoonNotificationHandler(INotificationService service) => _service = service;

    public async Task HandleAsync(LoanDueSoonEvent applicationEvent, CancellationToken cancellationToken = default)
    {
        string readerCode = $"R{applicationEvent.ReaderId:D6}";
        string dueDate = applicationEvent.DueDate.ToString("dd/MM/yyyy");
        string key = $"DueSoon:{applicationEvent.BorrowId}:{applicationEvent.DueDate:yyyy-MM-dd}";
        await _service.NotifyAsync("Sắp đến hạn trả sách",
            $"\"{applicationEvent.BookTitle}\" của độc giả {readerCode} sẽ đến hạn trả vào {dueDate}. Còn 2 ngày.",
            NotificationType.DueSoon, "Borrow", "Borrow", applicationEvent.BorrowId.ToString(),
            cancellationToken, key).ConfigureAwait(false);
    }
}

public sealed class NotificationApplicationEventDispatcher : IApplicationEventDispatcher
{
    private readonly IApplicationEventHandler<LoanDueSoonEvent> _dueSoonHandler;
    public NotificationApplicationEventDispatcher(IApplicationEventHandler<LoanDueSoonEvent> dueSoonHandler) => _dueSoonHandler = dueSoonHandler;

    public Task PublishAsync(LoanDueSoonEvent applicationEvent, CancellationToken cancellationToken = default) =>
        _dueSoonHandler.HandleAsync(applicationEvent, cancellationToken);
}

// The desktop app has no DI container. This one process-wide dispatcher is configured at startup.
public static class NotificationEvents
{
    private static IApplicationEventHandler<BusinessActionCompleted>? _handler;

    public static void Configure(IApplicationEventHandler<BusinessActionCompleted> handler) => _handler = handler;

    public static void PublishAfterSuccess(BusinessActionCompleted applicationEvent)
    {
        try
        {
            var handler = _handler;
            if (handler != null)
                Task.Run(() => handler.HandleAsync(applicationEvent)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // The business transaction has already committed; notification failure cannot undo it.
            Trace.TraceError($"Notification failed for {applicationEvent.Action}: {exception}");
        }
    }
}

public static class NotificationRuntime
{
    public static INotificationService Service { get; } = new NotificationService(new NotificationRepository());
    public static IDueSoonNotificationChecker DueSoonChecker { get; } = new DueSoonNotificationChecker(
        new BorrowRepository(), new NotificationApplicationEventDispatcher(new LoanDueSoonNotificationHandler(Service)), TimeProvider.System);

    public static void Initialize() => NotificationEvents.Configure(new NotificationEventHandler(Service));

    public static async Task CheckDueSoonSafelyAsync()
    {
        try { await DueSoonChecker.CheckAsync().ConfigureAwait(false); }
        catch (Exception exception) { Trace.TraceError($"DueSoon reminder check failed: {exception}"); }
    }
}
