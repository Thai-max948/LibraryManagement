using System.Collections.Concurrent;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public sealed class HistoryTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(47, 6)]
    public void HistoryPage_UsesEightRecordsPerPage(int totalCount, int expectedPages)
    {
        var page = new HistoryPage { TotalCount = totalCount };

        Assert.Equal(8, HistoryQuery.DefaultPageSize);
        Assert.Equal(8, HistoryService.DefaultPageSize);
        Assert.Equal(8, new HistoryQuery().PageSize);
        Assert.Equal(8, page.PageSize);
        Assert.Equal(expectedPages, page.TotalPages);
    }

    [Fact]
    public void ViewModel_LoadsEightRowsAndClearsSelectionAndEventsOnPageChange()
    {
        var service = new RecordingHistoryService(query =>
        {
            int firstId = ((query.PageNumber - 1) * 8) + 1;
            int rowCount = query.PageNumber == 6 ? 7 : 8;
            var rows = Enumerable.Range(firstId, rowCount).Select(id => new HistoryRecordDto
            {
                BorrowId = id,
                ReaderName = "Reader",
                BookTitle = "Book",
                Status = "Returned",
                Events = new[]
                {
                    new CirculationAuditEvent
                    {
                        BorrowId = id,
                        EventType = CirculationAuditEventType.BorrowCreated,
                        ActorNameSnapshot = "History test",
                        OccurredAt = new DateTime(2026, 10, 1),
                        Note = $"event-{id}"
                    }
                }
            }).ToArray();
            return new HistoryPage
            {
                Records = rows,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalCount = 47
            };
        });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            Assert.Equal(8, service.Queries.Last().PageSize);
            Assert.Equal(8, vm.Records.Count);
            Assert.Equal(6, vm.TotalPages);
            Assert.Equal("Showing 1–8 of 47", vm.RecordsSummary);
            Assert.Equal("Page 1 / 6", vm.PageSummary);
            Assert.Equal(326d, vm.GridHeight);
            Assert.False(vm.HasPreviousPage);
            Assert.True(vm.HasNextPage);
            Assert.Null(vm.SelectedRecord);

            vm.SelectedRecord = vm.Records[0];
            Assert.Contains("event-1", vm.SelectedRecord!.AuditTimeline);
            vm.NextPageCommand.Execute(null);
            Assert.Null(vm.SelectedRecord);
            Assert.Equal(2, vm.PageNumber);

            for (int page = 3; page <= 6; page++)
                vm.NextPageCommand.Execute(null);

            Assert.Equal(6, vm.PageNumber);
            Assert.Equal(7, vm.Records.Count);
            Assert.Equal("Showing 41–47 of 47", vm.RecordsSummary);
            Assert.Equal(290d, vm.GridHeight);
            Assert.False(vm.HasNextPage);
            Assert.True(vm.HasPreviousPage);
            Assert.Null(vm.SelectedRecord);
            vm.NextPageCommand.Execute(null);
            Assert.Equal(6, vm.PageNumber);
        });
    }

    [Fact]
    public void ViewModel_ClampsToLastValidPageAndReloadsOnlyOnceWhenCountShrinks()
    {
        int pageTwoCalls = 0;
        var service = new RecordingHistoryService(query =>
        {
            if (query.PageNumber == 6)
                return new HistoryPage { PageNumber = 6, PageSize = 8, TotalCount = 9 };

            if (query.PageNumber == 2 && ++pageTwoCalls == 2)
                return new HistoryPage
                {
                    Records = new[] { new HistoryRecordDto { BorrowId = 9, ReaderName = "Reader" } },
                    PageNumber = 2,
                    PageSize = 8,
                    TotalCount = 9
                };

            int firstId = ((query.PageNumber - 1) * 8) + 1;
            return new HistoryPage
            {
                Records = Enumerable.Range(firstId, 8)
                    .Select(id => new HistoryRecordDto { BorrowId = id, ReaderName = "Reader" }).ToArray(),
                PageNumber = query.PageNumber,
                PageSize = 8,
                TotalCount = 47
            };
        });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            for (int page = 2; page <= 6; page++)
                vm.NextPageCommand.Execute(null);

            Assert.Equal(2, vm.PageNumber);
            Assert.Equal(7, service.Queries.Count);
            Assert.Equal(1, service.Queries.Count(query => query.PageNumber == 6));
            Assert.Equal(2, service.Queries.Count(query => query.PageNumber == 2));
            var lastPageRecord = Assert.Single(vm.Records);
            Assert.Equal(9, lastPageRecord.BorrowId);
            Assert.Equal(2, vm.TotalPages);
        });
    }

    [Fact]
    public void ViewModel_ShowsActualReturnDateAndDoesNotSubstituteDueDate()
    {
        var returnedAt = new DateTime(2026, 9, 18);
        var vm = CreateViewModel(new HistoryPage
        {
            Records = new[]
            {
                new HistoryRecordDto
                {
                    BorrowId = 2, ReaderName = "Reader", BookTitle = "History book", BookCopyId = 56,
                    Barcode = "BC-000056", BorrowDate = new DateTime(2026, 9, 1),
                    DueDate = new DateTime(2026, 9, 15), ReturnDate = returnedAt, Status = "Returned"
                },
                new HistoryRecordDto
                {
                    BorrowId = 1, ReaderName = "Reader", BookTitle = "History book", BookCopyId = null,
                    BorrowDate = new DateTime(2026, 9, 17), DueDate = new DateTime(2026, 10, 1),
                    ReturnDate = null, Status = "Borrowing"
                }
            },
            TotalCount = 2,
            OverdueCount = 0
        });

        StaHelper.RunInSta(() =>
        {
            Assert.Equal("18/09/2026", vm.Records[0].ReturnDate);
            Assert.Equal("15/09/2026", vm.Records[0].DueDate);
            Assert.Equal("—", vm.Records[1].ReturnDate);
            Assert.Equal("BC-000056", vm.Records[0].CopyLabel);
            Assert.Equal("Legacy / unknown copy", vm.Records[1].CopyLabel);
        });
    }

    [Fact]
    public async Task ViewModel_ChangesPageAndResetsToFirstPageWhenFiltersChange()
    {
        var service = new RecordingHistoryService(new HistoryPage
        {
            Records = new[] { new HistoryRecordDto { BorrowId = 1, ReaderName = "A" } },
            TotalCount = 101,
            OverdueCount = 4
        });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            vm.NextPageCommand.Execute(null);
            Assert.Equal(2, service.Queries.Last().PageNumber);
            int queryCount = service.Queries.Count;
            vm.SearchText = "BC-42";
            Assert.Equal(1, vm.PageNumber);
            Assert.Equal(queryCount, service.Queries.Count);
        });

        await Task.Delay(450);

        StaHelper.RunInSta(() =>
        {
            Assert.Equal(1, service.Queries.Last().PageNumber);
            Assert.Equal("BC-42", service.Queries.Last().SearchText);
            vm.SelectedStatus = "Overdue";
            Assert.Equal("Overdue", service.Queries.Last().Status);
            vm.NextPageCommand.Execute(null);
            Assert.Equal(2, service.Queries.Last().PageNumber);
            var fromDate = new DateTime(2026, 9, 10);
            vm.FromDate = fromDate;
            Assert.Equal(1, service.Queries.Last().PageNumber);
            Assert.Equal(fromDate, service.Queries.Last().FromDate);
            vm.NextPageCommand.Execute(null);
            Assert.Equal(2, service.Queries.Last().PageNumber);
            var toDate = new DateTime(2026, 9, 12);
            vm.ToDate = toDate;
            Assert.Equal(1, service.Queries.Last().PageNumber);
            Assert.Equal(toDate, service.Queries.Last().ToDate);
            vm.NextPageCommand.Execute(null);
            var pagedQuery = service.Queries.Last();
            Assert.Equal("BC-42", pagedQuery.SearchText);
            Assert.Equal("Overdue", pagedQuery.Status);
            Assert.Equal(fromDate, pagedQuery.FromDate);
            Assert.Equal(toDate, pagedQuery.ToDate);
            Assert.Equal(2, pagedQuery.PageNumber);
            Assert.Equal(13, vm.TotalPages);
            Assert.Equal(4, vm.OverdueCount);
            Assert.True(vm.HasOverdue);
        });
    }

    [Fact]
    public async Task ViewModel_RapidSearchLoadsOnlyLatestKeywordAfterDebounce()
    {
        var service = new RecordingHistoryService(new HistoryPage
        {
            Records = new[] { new HistoryRecordDto { BorrowId = 1, ReaderName = "A" } },
            TotalCount = 101,
            OverdueCount = 0
        });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));
        int queryCount = service.Queries.Count;

        StaHelper.RunInSta(() =>
        {
            vm.SearchText = "n";
            vm.SearchText = "ng";
            vm.SearchText = "ngu";
            Assert.Equal(1, vm.PageNumber);
            Assert.Equal(queryCount, service.Queries.Count);
        });

        await Task.Delay(100);
        Assert.Equal(queryCount, service.Queries.Count);
        await Task.Delay(450);

        Assert.Equal(queryCount + 1, service.Queries.Count);
        var query = service.Queries.Last();
        Assert.Equal("ngu", query.SearchText);
        Assert.Equal(1, query.PageNumber);
    }

    [Fact]
    public async Task ViewModel_EmptySearchPreservesDateFiltersAndPagedLoad()
    {
        var service = new RecordingHistoryService(new HistoryPage { TotalCount = 101 });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));
        var fromDate = new DateTime(2026, 9, 10);
        var toDate = new DateTime(2026, 9, 12);

        StaHelper.RunInSta(() =>
        {
            vm.FromDate = fromDate;
            vm.ToDate = toDate;
            vm.NextPageCommand.Execute(null);
        });
        Assert.Equal(2, vm.PageNumber);
        int queryCount = service.Queries.Count;

        StaHelper.RunInSta(() => vm.SearchText = "  ");
        Assert.Equal(1, vm.PageNumber);
        Assert.Equal(queryCount, service.Queries.Count);
        await Task.Delay(450);

        var query = service.Queries.Last();
        Assert.Equal(string.Empty, query.SearchText);
        Assert.Equal(fromDate, query.FromDate);
        Assert.Equal(toDate, query.ToDate);
        Assert.Equal(HistoryService.DefaultPageSize, query.PageSize);
        Assert.Equal(1, query.PageNumber);

        StaHelper.RunInSta(() => vm.NextPageCommand.Execute(null));
        var pagedQuery = service.Queries.Last();
        Assert.Equal(2, pagedQuery.PageNumber);
        Assert.Equal(string.Empty, pagedQuery.SearchText);
        Assert.Equal(fromDate, pagedQuery.FromDate);
        Assert.Equal(toDate, pagedQuery.ToDate);
    }

    [Fact]
    public async Task ViewModel_DateFiltersRemainImmediateAndCancelPendingSearch()
    {
        var service = new RecordingHistoryService(new HistoryPage { TotalCount = 101 });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            int queryCount = service.Queries.Count;
            vm.SearchText = "pending-date";
            vm.FromDate = new DateTime(2026, 9, 10);
            Assert.Equal(queryCount + 1, service.Queries.Count);
            Assert.Equal("pending-date", service.Queries.Last().SearchText);
            Assert.Equal(new DateTime(2026, 9, 10), service.Queries.Last().FromDate);

            queryCount = service.Queries.Count;
            vm.SearchText = "pending-to-date";
            vm.ToDate = new DateTime(2026, 9, 12);
            Assert.Equal(queryCount + 1, service.Queries.Count);
            Assert.Equal("pending-to-date", service.Queries.Last().SearchText);
            Assert.Equal(new DateTime(2026, 9, 12), service.Queries.Last().ToDate);
        });

        int finalQueryCount = service.Queries.Count;
        await Task.Delay(450);
        Assert.Equal(finalQueryCount, service.Queries.Count);
    }

    [Fact]
    public async Task ViewModel_PagingAndClearRemainImmediateAndCancelPendingSearch()
    {
        var service = new RecordingHistoryService(new HistoryPage { TotalCount = 101 });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            int queryCount = service.Queries.Count;
            vm.SearchText = "pending-next";
            vm.NextPageCommand.Execute(null);
            Assert.Equal(queryCount + 1, service.Queries.Count);
            Assert.Equal(2, service.Queries.Last().PageNumber);

            queryCount = service.Queries.Count;
            vm.PreviousPageCommand.Execute(null);
            Assert.Equal(queryCount + 1, service.Queries.Count);
            Assert.Equal(1, service.Queries.Last().PageNumber);

            queryCount = service.Queries.Count;
            vm.SearchText = "pending-clear";
            vm.ClearFiltersCommand.Execute(null);
            Assert.Equal(queryCount + 1, service.Queries.Count);
            Assert.Equal(string.Empty, service.Queries.Last().SearchText);
            Assert.Equal(1, service.Queries.Last().PageNumber);
        });

        int finalQueryCount = service.Queries.Count;
        await Task.Delay(450);
        Assert.Equal(finalQueryCount, service.Queries.Count);
    }

    [Fact]
    public void ViewModel_ClearFiltersResetsSearchStatusDatesAndPage()
    {
        var service = new RecordingHistoryService(new HistoryPage
        {
            Records = new[] { new HistoryRecordDto { BorrowId = 1, ReaderName = "A" } },
            TotalCount = 101,
            OverdueCount = 4
        });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));

        StaHelper.RunInSta(() =>
        {
            vm.SearchText = "reader";
            vm.SelectedStatus = "Returned";
            vm.FromDate = new DateTime(2026, 9, 10);
            vm.ToDate = new DateTime(2026, 9, 12);
            vm.NextPageCommand.Execute(null);

            vm.ClearFiltersCommand.Execute(null);

            Assert.Equal(string.Empty, vm.SearchText);
            Assert.Equal("All", vm.SelectedStatus);
            Assert.Null(vm.FromDate);
            Assert.Null(vm.ToDate);
            Assert.Equal(1, vm.PageNumber);
            Assert.Equal(string.Empty, service.Queries.Last().SearchText);
            Assert.Equal("All", service.Queries.Last().Status);
            Assert.Null(service.Queries.Last().FromDate);
            Assert.Null(service.Queries.Last().ToDate);
            Assert.Equal(1, service.Queries.Last().PageNumber);
        });
    }

    [Fact]
    public void ViewModel_InvalidDateRangeShowsMessageWithoutQuerying()
    {
        var service = new RecordingHistoryService(new HistoryPage { TotalCount = 0 });
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(service));
        int callsBeforeInvalidRange = service.Queries.Count;

        StaHelper.RunInSta(() =>
        {
            vm.ToDate = new DateTime(2026, 9, 10);
            vm.FromDate = new DateTime(2026, 9, 11);
            Assert.Contains("on or before", vm.ErrorMessage);
            Assert.Equal(callsBeforeInvalidRange + 1, service.Queries.Count);
            Assert.Empty(vm.Records);
        });
    }

    [Fact]
    public void HistoryService_ClampsPageSizeAndRejectsReversedDatesBeforeRepositoryCall()
    {
        var repository = new Mock<HistoryRepository>();
        repository.Setup(item => item.GetPage(It.IsAny<HistoryQuery>(), It.IsAny<DateTime>()))
            .Returns(new HistoryPage { TotalCount = 0 });
        var audits = new Mock<CirculationAuditRepository>();
        var service = new HistoryService(repository.Object, audits.Object, TimeProvider.System);

        service.GetPage(new HistoryQuery { PageNumber = 0, PageSize = 500 });
        repository.Verify(item => item.GetPage(
            It.Is<HistoryQuery>(query => query.PageNumber == 1 && query.PageSize == 100), It.IsAny<DateTime>()), Times.Once);

        Assert.Throws<ArgumentException>(() => service.GetPage(new HistoryQuery
        {
            FromDate = new DateTime(2026, 9, 11),
            ToDate = new DateTime(2026, 9, 10)
        }));
        repository.Verify(item => item.GetPage(It.IsAny<HistoryQuery>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public void BorrowRepository_RejectsUntrackedHistoryMutation()
    {
        Assert.Throws<NotSupportedException>(() => new BorrowRepository().Update(new BorrowRecord
        {
            BorrowId = 1,
            ReaderId = 2,
            BookId = 3,
            BorrowDate = DateTime.Today.AddDays(-1),
            DueDate = DateTime.Today,
            Status = "Returned"
        }));
    }

    private static HistoryViewModel CreateViewModel(HistoryPage page)
    {
        HistoryViewModel vm = null!;
        StaHelper.RunInSta(() => vm = new HistoryViewModel(new RecordingHistoryService(page)));
        return vm;
    }

    private sealed class RecordingHistoryService : HistoryService
    {
        private readonly Func<HistoryQuery, HistoryPage> _pageFactory;

        public RecordingHistoryService(HistoryPage page) : this(_ => page) { }

        public RecordingHistoryService(Func<HistoryQuery, HistoryPage> pageFactory)
        {
            _pageFactory = pageFactory;
        }

        public ConcurrentQueue<HistoryQuery> Queries { get; } = new();

        public override HistoryPage GetPage(HistoryQuery query)
        {
            Queries.Enqueue(query);
            return _pageFactory(query);
        }
    }
}
