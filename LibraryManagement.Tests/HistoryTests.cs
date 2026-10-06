using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public sealed class HistoryTests
{
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
    public void ViewModel_ChangesPageAndResetsToFirstPageWhenFiltersChange()
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
            vm.SearchText = "BC-42";
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
            Assert.Equal(3, vm.TotalPages);
            Assert.Equal(4, vm.OverdueCount);
            Assert.True(vm.HasOverdue);
        });
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
            FromDate = new DateTime(2026, 9, 11), ToDate = new DateTime(2026, 9, 10)
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

    private sealed class RecordingHistoryService(HistoryPage page) : HistoryService
    {
        public List<HistoryQuery> Queries { get; } = new();

        public override HistoryPage GetPage(HistoryQuery query)
        {
            Queries.Add(query);
            return page;
        }
    }
}
