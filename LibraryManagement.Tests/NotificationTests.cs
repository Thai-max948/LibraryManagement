using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;

namespace LibraryManagement.Tests;

public class NotificationTests
{
    [Fact]
    public async Task NotifyCreatesUnreadNotificationWithSourceAndPublishesCreated()
    {
        var repository = new MemoryRepository();
        var service = new NotificationService(repository);
        Notification? published = null;
        service.Created += notification => published = notification;

        var created = await service.NotifyAsync("Title", "Message", NotificationType.Warning, "Return", "BorrowRecord", "42");

        Assert.NotNull(created);
        Assert.Equal(1, created.Id);
        Assert.False(created.IsRead);
        Assert.Equal(NotificationType.Warning, created.Type);
        Assert.Equal("Return", created.SourceModule);
        Assert.Equal("42", created.SourceEntityId);
        Assert.Same(created, published);
        Assert.Equal(1, await service.GetUnreadCountAsync());
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("Title", " ")]
    public async Task NotifyRejectsEmptyTitleOrMessage(string title, string message)
    {
        var repository = new MemoryRepository();
        var service = new NotificationService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.NotifyAsync(title, message, NotificationType.Info, "Book"));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task ReadOperationsUpdateUnreadCount()
    {
        var service = new NotificationService(new MemoryRepository());
        var first = await service.NotifyAsync("One", "Message", NotificationType.Info, "Book");
        Assert.NotNull(first);
        await service.NotifyAsync("Two", "Message", NotificationType.Success, "Reader");
        await service.MarkAsReadAsync(first.Id);
        Assert.Single(await service.GetUnreadAsync());
        Assert.Equal(1, await service.GetUnreadCountAsync());
        await service.MarkAllAsReadAsync();
        Assert.Equal(0, await service.GetUnreadCountAsync());
    }

    [Theory]
    [InlineData(BusinessAction.BorrowCreated, "Borrow", NotificationType.Success)]
    [InlineData(BusinessAction.FeePaid, "Fee", NotificationType.Success)]
    public async Task HandlerMapsBusinessActionToNotification(BusinessAction action, string module, NotificationType type)
    {
        var service = new NotificationService(new MemoryRepository());
        var handler = new NotificationEventHandler(service);
        await handler.HandleAsync(new BusinessActionCompleted(action, "25"));
        var notification = Assert.Single(await service.GetAllAsync());
        Assert.Equal(module, notification.SourceModule);
        Assert.Equal(type, notification.Type);
        Assert.Equal("25", notification.SourceEntityId);
    }

    [Fact]
    public async Task DueSoonHandlerPersistsSemanticKeyAndIgnoresDuplicateEvents()
    {
        var repository = new MemoryRepository();
        var service = new NotificationService(repository);
        int createdEvents = 0;
        service.Created += _ => createdEvents++;
        var handler = new LoanDueSoonNotificationHandler(service);
        var reminder = new LoanDueSoonEvent(7, 1, "Clean Code", new DateTime(2026, 10, 6, 23, 30, 0));

        await handler.HandleAsync(reminder);
        await handler.HandleAsync(reminder);

        var notification = Assert.Single(await service.GetAllAsync());
        Assert.Equal("DueSoon:7:2026-10-06", notification.IdempotencyKey);
        Assert.Equal(NotificationType.DueSoon, notification.Type);
        Assert.Equal("Borrow", notification.SourceModule);
        Assert.Equal("Borrow", notification.SourceEntityType);
        Assert.Equal("7", notification.SourceEntityId);
        Assert.Contains("06/10/2026", notification.Message);
        Assert.Equal(1, createdEvents);
    }

    internal sealed class MemoryRepository : INotificationRepository
    {
        private readonly List<Notification> _items = new();

        public Task<Notification?> AddAsync(Notification notification, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            if (idempotencyKey != null && _items.Any(item => item.IdempotencyKey == idempotencyKey))
                return Task.FromResult<Notification?>(null);
            var saved = new Notification
            {
                Id = _items.Count + 1, Title = notification.Title, Message = notification.Message,
                Type = notification.Type, SourceModule = notification.SourceModule,
                SourceEntityType = notification.SourceEntityType, SourceEntityId = notification.SourceEntityId, IdempotencyKey = idempotencyKey,
                CreatedAt = notification.CreatedAt
            };
            _items.Add(saved);
            return Task.FromResult<Notification?>(saved);
        }

        public Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(_items.ToList());

        public Task<IReadOnlyList<Notification>> GetUnreadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(_items.Where(item => !item.IsRead).ToList());

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.Count(item => !item.IsRead));

        public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
        {
            int index = _items.FindIndex(item => item.Id == id);
            if (index >= 0) _items[index] = CopyRead(_items[index]);
            return Task.CompletedTask;
        }

        public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < _items.Count; i++) _items[i] = CopyRead(_items[i]);
            return Task.CompletedTask;
        }

        private static Notification CopyRead(Notification item) => new()
        {
            Id = item.Id, Title = item.Title, Message = item.Message, Type = item.Type,
            SourceModule = item.SourceModule, SourceEntityType = item.SourceEntityType,
            SourceEntityId = item.SourceEntityId, CreatedAt = item.CreatedAt, IsRead = true
        };
    }
}
