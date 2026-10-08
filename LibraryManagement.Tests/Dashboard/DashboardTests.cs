using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public sealed class DashboardTests
{
    private static readonly DateTime Today = new(2026, 10, 5);

    [Fact]
    public async Task DashboardService_UsesSharedDueSoonDate_FeeAggregate_AndSevenDayWindow()
    {
        var repository = new Mock<IDashboardRepository>();
        var fees = new Mock<IFeeBalanceReader>();
        DashboardDateRange? requestedRange = null;
        var snapshot = new DashboardSnapshot(3, 12, 4, 5, 1, 1, 1, 0, 6, 2, 1, 0m);
        var repositoryData = new DashboardRepositoryData(
            snapshot,
            new[] { new DashboardRecentBorrow(1, "Reader", "Book", "BK-1", Today) },
            Array.Empty<DashboardRecentReturn>(),
            new[] { new DashboardCirculationCount(Today, 2, 1) });

        repository
            .Setup(repo => repo.GetDashboardDataAsync(It.IsAny<DashboardDateRange>(), It.IsAny<CancellationToken>()))
            .Callback<DashboardDateRange, CancellationToken>((range, _) => requestedRange = range)
            .ReturnsAsync(repositoryData);
        fees.Setup(service => service.GetTotalOutstandingBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1250m);

        var service = new DashboardService(repository.Object, fees.Object, new FixedTimeProvider(Today.AddHours(18)));
        DashboardData result = await service.GetDashboardDataAsync();

        Assert.NotNull(requestedRange);
        Assert.Equal(Today, requestedRange!.Today);
        Assert.Equal(DueSoonDatePolicy.GetTargetDate(Today), requestedRange.DueSoonDate);
        Assert.Equal(Today.AddDays(-6), requestedRange.CirculationStart);
        Assert.Equal(Today.AddDays(1), requestedRange.CirculationEndExclusive);
        Assert.Equal(1250m, result.Snapshot.OutstandingFees);
        Assert.Equal(7, result.Circulation.Count);
        Assert.Equal(Today.AddDays(-6), result.Circulation[0].Date);
        Assert.Equal(Today, result.Circulation[^1].Date);
        Assert.All(result.Circulation.Where(point => point.Date != Today), point =>
        {
            Assert.Equal(0, point.BorrowCount);
            Assert.Equal(0, point.ReturnCount);
        });
        Assert.Equal((2, 1), (result.Circulation[^1].BorrowCount, result.Circulation[^1].ReturnCount));
        Assert.Equal("MON", new DashboardCirculationPoint(new DateTime(2026, 10, 5), 0, 0).DayLabel);
        fees.Verify(service => service.GetTotalOutstandingBalanceAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DashboardService_EmptyRepositoryResult_ReturnsZeroSnapshotAndSevenZeroPoints()
    {
        var repository = new Mock<IDashboardRepository>();
        var fees = new Mock<IFeeBalanceReader>();
        repository.Setup(repo => repo.GetDashboardDataAsync(It.IsAny<DashboardDateRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardRepositoryData(
                DashboardSnapshot.Empty,
                Array.Empty<DashboardRecentBorrow>(),
                Array.Empty<DashboardRecentReturn>(),
                Array.Empty<DashboardCirculationCount>()));
        fees.Setup(service => service.GetTotalOutstandingBalanceAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        var service = new DashboardService(repository.Object, fees.Object, new FixedTimeProvider(Today));
        DashboardData result = await service.GetDashboardDataAsync();

        Assert.Equal(DashboardSnapshot.Empty, result.Snapshot);
        Assert.Empty(result.RecentBorrowings);
        Assert.Empty(result.RecentReturns);
        Assert.Equal(7, result.Circulation.Count);
        Assert.All(result.Circulation, point =>
        {
            Assert.Equal(0, point.BorrowCount);
            Assert.Equal(0, point.ReturnCount);
        });
    }

    [Fact]
    public void DueSoonDatePolicy_MatchesNotificationTargetAndUsesHalfOpenDayWindow()
    {
        DateTime target = DueSoonDatePolicy.GetTargetDate(Today.AddHours(23));
        DueSoonDayWindow window = DueSoonDatePolicy.GetDayWindow(target);

        Assert.Equal(Today.AddDays(2), target);
        Assert.Equal(target, window.StartInclusive);
        Assert.Equal(target.AddDays(1), window.EndExclusive);
    }

    [Fact]
    public async Task ViewModel_LoadsOnConstruction_AndManualRefreshReplacesSnapshotAndTimestamp()
    {
        var first = DataWithSnapshot(new DashboardSnapshot(1, 5, 2, 2, 0, 1, 0, 0, 2, 1, 0, 100m));
        var second = DataWithSnapshot(new DashboardSnapshot(2, 7, 3, 3, 1, 0, 0, 0, 3, 0, 1, 250m));
        var service = new Mock<IDashboardService>();
        service.SetupSequence(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(second);
        var time = new MutableTimeProvider(new DateTimeOffset(Today.AddHours(8), TimeSpan.Zero));
        var viewModel = new DashboardViewModel(service.Object, Mock.Of<IUserDialogService>(), time);

        await viewModel.InitialLoadTask;
        Assert.True(viewModel.HasLoaded);
        Assert.Equal(1, viewModel.ActiveReaders);
        Assert.Equal(5, viewModel.TotalCopies);
        Assert.Equal(Today.AddHours(8), viewModel.LastUpdated!.Value.DateTime);

        time.SetNow(new DateTimeOffset(Today.AddHours(9), TimeSpan.Zero));
        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.ActiveReaders);
        Assert.Equal(7, viewModel.TotalCopies);
        Assert.Equal(250m, viewModel.OutstandingFees);
        Assert.Equal(Today.AddHours(9), viewModel.LastUpdated!.Value.DateTime);
        service.Verify(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ViewModel_FormatsOutstandingFeesAsEnUsCurrency()
    {
        var service = new Mock<IDashboardService>();
        service.Setup(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataWithSnapshot(new DashboardSnapshot(1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1234.56m)));

        var viewModel = new DashboardViewModel(service.Object, Mock.Of<IUserDialogService>(), new FixedTimeProvider(Today));
        await viewModel.InitialLoadTask;

        Assert.Equal("$1,234.56", viewModel.OutstandingFeesText);
    }

    [Fact]
    public async Task ViewModel_ReportsInitialLoadFailureWithoutThrowing()
    {
        var service = new Mock<IDashboardService>();
        var dialog = new Mock<IUserDialogService>();
        service.Setup(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var viewModel = new DashboardViewModel(service.Object, dialog.Object, new FixedTimeProvider(Today));
        await viewModel.InitialLoadTask;

        Assert.False(viewModel.HasLoaded);
        Assert.Null(viewModel.Data);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("Dashboard data is unavailable.", viewModel.DashboardLoadMessage);
        dialog.Verify(mock => mock.ShowError("Không thể tải dữ liệu Dashboard: database unavailable", "Lỗi"), Times.Once);
    }

    [Fact]
    public async Task ViewModel_RefreshFailureKeepsLastCompleteSnapshotAndTimestamp()
    {
        var service = new Mock<IDashboardService>();
        service.SetupSequence(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataWithSnapshot(new DashboardSnapshot(1, 4, 2, 1, 0, 0, 0, 1, 1, 0, 0, 50m)))
            .ThrowsAsync(new InvalidOperationException("temporary outage"));
        var dialog = new Mock<IUserDialogService>();
        var time = new MutableTimeProvider(new DateTimeOffset(Today.AddHours(10), TimeSpan.Zero));
        var viewModel = new DashboardViewModel(service.Object, dialog.Object, time);
        await viewModel.InitialLoadTask;
        DateTimeOffset originalTimestamp = viewModel.LastUpdated!.Value;

        time.SetNow(originalTimestamp.AddHours(1));
        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasLoaded);
        Assert.Equal(4, viewModel.TotalCopies);
        Assert.Equal(originalTimestamp, viewModel.LastUpdated);
        dialog.Verify(mock => mock.ShowError("Không thể tải dữ liệu Dashboard: temporary outage", "Lỗi"), Times.Once);
    }

    [Fact]
    public async Task ViewModel_DeduplicatesOverlappingRefreshCalls()
    {
        var pending = new TaskCompletionSource<DashboardData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<IDashboardService>();
        service.Setup(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>())).Returns(pending.Task);
        var viewModel = new DashboardViewModel(service.Object, Mock.Of<IUserDialogService>(), new FixedTimeProvider(Today));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsLoading);
        service.Verify(mock => mock.GetDashboardDataAsync(It.IsAny<CancellationToken>()), Times.Once);
        pending.SetResult(DataWithSnapshot(DashboardSnapshot.Empty));
        await viewModel.InitialLoadTask;
        Assert.False(viewModel.IsLoading);
    }

    private static DashboardData DataWithSnapshot(DashboardSnapshot snapshot) => new(
        snapshot,
        Array.Empty<DashboardRecentBorrow>(),
        Array.Empty<DashboardRecentReturn>(),
        Enumerable.Range(0, 7)
            .Select(offset => new DashboardCirculationPoint(Today.AddDays(offset - 6), 0, 0))
            .ToArray());

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetNow(DateTimeOffset value) => _utcNow = value;
    }
}
