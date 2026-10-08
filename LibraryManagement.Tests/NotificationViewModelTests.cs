using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public class NotificationViewModelTests
{
    [Fact]
    public void LoadUsesFirstPageOfFiftyAndNeverLoadsFullHistory()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService(Enumerable.Range(1, 2).Select(id => CreateNotification(id)), unreadCount: 2);
            using var viewModel = new NotificationViewModel(service.Mock.Object);

            viewModel.LoadAsync().GetAwaiter().GetResult();

            var query = Assert.Single(service.PageQueries);
            Assert.False(query.UnreadOnly);
            Assert.Equal(1, query.PageNumber);
            Assert.Equal(50, query.PageSize);
            Assert.Equal(2, viewModel.Notifications.Count);
            service.Mock.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
            service.Mock.Verify(x => x.GetUnreadAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Fact]
    public void SwitchingModesQueriesBackendForUnreadAndAllPages()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService(
                allItems: [CreateNotification(1), CreateNotification(2, isRead: true)],
                unreadCount: 1);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.ShowUnreadCommand.Execute(null);
            viewModel.ShowAllCommand.Execute(null);

            Assert.Equal(new[] { false, true, false }, service.PageQueries.Select(query => query.UnreadOnly));
            Assert.Equal(new[] { 1, 2 }, viewModel.Notifications.Select(item => item.Id));
        });
    }

    [Fact]
    public void UnreadBadgeUsesGlobalServiceCountInsteadOfLoadedPage()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService(Enumerable.Range(1, 20).Select(id => CreateNotification(id)), unreadCount: 250);
            using var viewModel = new NotificationViewModel(service.Mock.Object);

            viewModel.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal(20, viewModel.Notifications.Count);
            Assert.Equal(250, viewModel.UnreadCount);
            Assert.True(viewModel.HasUnread);
            service.Mock.Verify(x => x.GetUnreadCountAsync(It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void CreatedNotificationIncrementsGlobalCountShowsToastAndInsertsFirst()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService([CreateNotification(1), CreateNotification(2)], unreadCount: 8);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            var created = CreateNotification(99);

            service.Mock.Raise(x => x.Created += null, created);

            Assert.Equal(9, viewModel.UnreadCount);
            Assert.Equal(99, viewModel.Notifications[0].Id);
            Assert.Equal(99, viewModel.CurrentToast?.Id);
            Assert.True(viewModel.IsToastVisible);
        });
    }

    [Fact]
    public void CreatedNotificationKeepsLoadedCollectionAtFifty()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService(Enumerable.Range(1, 50).Select(id => CreateNotification(id)), unreadCount: 50);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.LoadAsync().GetAwaiter().GetResult();

            service.Mock.Raise(x => x.Created += null, CreateNotification(100));

            Assert.Equal(50, viewModel.Notifications.Count);
            Assert.Equal(100, viewModel.Notifications[0].Id);
            Assert.DoesNotContain(viewModel.Notifications, item => item.Id == 50);
        });
    }

    [Fact]
    public void MarkSingleReadInAllModeDecrementsBadgeAndKeepsReadItem()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService([CreateNotification(1), CreateNotification(2, isRead: true)], unreadCount: 7);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            var item = viewModel.Notifications[0];

            viewModel.OpenNotificationCommand.Execute(item);

            Assert.True(item.IsRead);
            Assert.Contains(item, viewModel.Notifications);
            Assert.Equal(6, viewModel.UnreadCount);
            service.Mock.Verify(x => x.MarkAsReadAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void MarkSingleReadInUnreadModeDecrementsBadgeAndRemovesItem()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService([CreateNotification(1), CreateNotification(2)], unreadCount: 2);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.ShowUnreadCommand.Execute(null);
            var item = viewModel.Notifications[0];

            viewModel.OpenNotificationCommand.Execute(item);

            Assert.Equal(1, viewModel.UnreadCount);
            Assert.DoesNotContain(item, viewModel.Notifications);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkAllReadUpdatesGlobalCountAndCurrentModeCollection(bool unreadOnly)
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService(
                [CreateNotification(1), CreateNotification(2), CreateNotification(3, isRead: true)],
                unreadCount: 2);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            if (unreadOnly) viewModel.ShowUnreadCommand.Execute(null);
            else viewModel.LoadAsync().GetAwaiter().GetResult();

            viewModel.MarkAllAsReadCommand.Execute(null);

            Assert.Equal(0, viewModel.UnreadCount);
            if (unreadOnly)
                Assert.Empty(viewModel.Notifications);
            else
                Assert.All(viewModel.Notifications, item => Assert.True(item.IsRead));
            service.Mock.Verify(x => x.MarkAllAsReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void FailedCountRefreshPreservesPreviouslyLoadedBadge()
    {
        StaHelper.RunInSta(() =>
        {
            var service = CreateService([CreateNotification(1)], unreadCount: 14);
            using var viewModel = new NotificationViewModel(service.Mock.Object);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            service.Mock.Setup(x => x.GetUnreadCountAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("count unavailable"));

            viewModel.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal(14, viewModel.UnreadCount);
            Assert.Single(viewModel.Notifications);
            Assert.Equal("Không thể tải thông báo.", viewModel.LoadError);
        });
    }

    private static (Mock<INotificationService> Mock, List<NotificationPageQuery> PageQueries) CreateService(IEnumerable<Notification> allItems, int unreadCount)
    {
        var items = allItems.ToArray();
        var unreadItems = items.Where(item => !item.IsRead).ToArray();
        var service = new Mock<INotificationService>();
        var pageQueries = new List<NotificationPageQuery>();
        service.Setup(x => x.GetPageAsync(It.IsAny<NotificationPageQuery>(), It.IsAny<CancellationToken>()))
            .Returns((NotificationPageQuery query, CancellationToken _) =>
            {
                pageQueries.Add(query);
                var selectedItems = query.UnreadOnly ? unreadItems : items;
                return Task.FromResult(new NotificationPage
                {
                    Items = selectedItems.Take(query.PageSize).ToArray(),
                    TotalCount = selectedItems.Length,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize
                });
            });
        service.Setup(x => x.GetUnreadCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(unreadCount);
        service.Setup(x => x.MarkAsReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.MarkAllAsReadAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Full-history query must not be used."));
        service.Setup(x => x.GetUnreadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Full unread-history query must not be used."));
        return (service, pageQueries);
    }

    private static Notification CreateNotification(int id, bool isRead = false) => new()
    {
        Id = id,
        Title = $"Notification {id}",
        Message = $"Message {id}",
        Type = NotificationType.Info,
        SourceModule = "Test",
        CreatedAt = DateTime.UtcNow.AddMinutes(-id),
        IsRead = isRead
    };
}
