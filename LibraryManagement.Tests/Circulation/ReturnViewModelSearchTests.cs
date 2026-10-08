using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;
using System.Collections.Concurrent;

namespace LibraryManagement.Tests;

public class ReturnViewModelSearchTests
{
    [Fact]
    public void Load_UsesCurrentSearchAndEmptyBackendResultClearsPreviousRows()
    {
        var service = new Mock<IReturnCirculationService>();
        var recentRow = new ActiveReturnLoanRow
        {
            BorrowId = 31,
            BookId = 4,
            BookCopyId = 8,
            ReaderName = "An",
            BookTitle = "Clean Code",
            Barcode = "BK-000008"
        };
        service.Setup(x => x.SearchActiveLoansForReturn(string.Empty, 100))
            .Returns(new List<ActiveReturnLoanRow> { recentRow });
        service.Setup(x => x.SearchActiveLoansForReturn("no match", 100))
            .Returns(new List<ActiveReturnLoanRow>());

        StaHelper.RunInSta(() =>
        {
            var vm = CreateViewModel(service.Object);

            Assert.Single(vm.ActiveBorrowings);
            Assert.Equal(31, vm.ActiveBorrowings[0].BorrowId);

            vm.SearchText = "no match";
            vm.Load();

            Assert.Empty(vm.ActiveBorrowings);
            service.Verify(x => x.SearchActiveLoansForReturn(string.Empty, 100), Times.Once);
            service.Verify(x => x.SearchActiveLoansForReturn("no match", 100), Times.Once);
            service.Verify(x => x.GetBorrowingBooks(), Times.Never);
        });
    }

    [Fact]
    public async Task SearchText_DebouncesRapidChangesAndClearingSearchRefreshes()
    {
        var calls = new ConcurrentQueue<string>();
        var latestRows = new List<ActiveReturnLoanRow>
        {
            new() { BorrowId = 32, BookId = 4, BookCopyId = 9, ReaderName = "An", BookTitle = "Clean Code" }
        };
        var service = new Mock<IReturnCirculationService>();
        service.Setup(x => x.SearchActiveLoansForReturn(It.IsAny<string?>(), 100))
            .Returns((string? query, int _) =>
            {
                string normalized = query ?? string.Empty;
                calls.Enqueue(normalized);
                return normalized == "abc" || normalized.Length == 0
                    ? latestRows
                    : new List<ActiveReturnLoanRow>();
            });

        var vm = StaHelper.RunInSta(() => CreateViewModel(service.Object));

        StaHelper.RunInSta(() => vm.SearchText = "a");
        await Task.Delay(80);
        StaHelper.RunInSta(() => vm.SearchText = "ab");
        await Task.Delay(80);
        StaHelper.RunInSta(() => vm.SearchText = "abc");

        Assert.Equal(new[] { string.Empty }, calls.ToArray());
        await Task.Delay(500);

        Assert.Equal(new[] { string.Empty, "abc" }, calls.ToArray());
        Assert.Equal(32, Assert.Single(vm.ActiveBorrowings).BorrowId);

        StaHelper.RunInSta(() => vm.SearchText = string.Empty);
        await Task.Delay(500);

        Assert.Equal(new[] { string.Empty, "abc", string.Empty }, calls.ToArray());
        Assert.Equal(32, Assert.Single(vm.ActiveBorrowings).BorrowId);
    }

    [Fact]
    public async Task BarcodeEnter_IsImmediateAndCancelsPendingGenericSearch()
    {
        const string barcode = "BK-000008";
        var calls = new ConcurrentQueue<string>();
        var loan = new BorrowRecord
        {
            BorrowId = 33,
            BookId = 4,
            BookCopyId = 8,
            ReaderId = 7,
            BorrowDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(7)
        };
        var service = new Mock<IReturnCirculationService>();
        service.Setup(x => x.SearchActiveLoansForReturn(It.IsAny<string?>(), 100))
            .Returns((string? query, int _) =>
            {
                calls.Enqueue(query ?? string.Empty);
                return new List<ActiveReturnLoanRow>();
            });
        service.Setup(x => x.FindActiveReturnByBarcode(barcode)).Returns(loan);

        var books = new Mock<BookRepository>();
        books.Setup(x => x.GetById(4)).Returns(new Book { BookId = 4, Title = "Clean Code" });
        var readers = new Mock<ReaderRepository>();
        readers.Setup(x => x.GetById(7)).Returns(new Reader { ReaderId = 7, FullName = "An" });
        var vm = StaHelper.RunInSta(() => new ReturnViewModel(service.Object,
            new BookService(books.Object, new BorrowRepository()),
            new ReaderService(readers.Object, new BorrowRepository()),
            new Mock<IUserDialogService>().Object));

        StaHelper.RunInSta(() =>
        {
            vm.SearchText = barcode;
            vm.SearchEnterCommand.Execute(null);
            Assert.Equal(33, vm.SelectedRow!.BorrowId);
        });

        service.Verify(x => x.FindActiveReturnByBarcode(barcode), Times.Once);
        await Task.Delay(500);
        Assert.Equal(new[] { string.Empty }, calls.ToArray());
        Assert.Equal(33, Assert.Single(vm.ActiveBorrowings).BorrowId);
    }

    [Fact]
    public async Task ReturnRefresh_IsImmediateAndCancelsPendingSearch()
    {
        var calls = new ConcurrentQueue<string>();
        var service = new Mock<IReturnCirculationService>();
        service.Setup(x => x.SearchActiveLoansForReturn(It.IsAny<string?>(), 100))
            .Returns((string? query, int _) =>
            {
                calls.Enqueue(query ?? string.Empty);
                return new List<ActiveReturnLoanRow>();
            });
        service.Setup(x => x.ReturnBook(34, ReturnCondition.Normal, string.Empty))
            .Returns(new ReturnResult(34, 0, 0, 0, DateTime.Today, DateTime.Today, ReturnCondition.Normal));
        var dialog = new Mock<IUserDialogService>();
        dialog.Setup(x => x.Confirm(It.IsAny<string>(), "Xác nhận trả sách", false)).Returns(true);
        var vm = StaHelper.RunInSta(() => CreateViewModel(service.Object, dialog: dialog.Object));

        StaHelper.RunInSta(() =>
        {
            vm.SearchText = "pending query";
            vm.SelectedRow = new ActiveBorrowRow
            {
                BorrowId = 34,
                BookId = 4,
                BookCopyId = 8,
                CopyBarcode = "BK-000008",
                ReaderName = "An",
                BookTitle = "Clean Code"
            };
            vm.ReturnCommand.Execute(null);
        });

        Assert.Equal(new[] { string.Empty, "pending query" }, calls.ToArray());
        await Task.Delay(500);
        Assert.Equal(new[] { string.Empty, "pending query" }, calls.ToArray());
        service.Verify(x => x.ReturnBook(34, ReturnCondition.Normal, string.Empty), Times.Once);
    }

    [Fact]
    public void Load_MapsDeletedReaderSnapshotAndLegacyLoanWithoutLoadingCopies()
    {
        var service = new Mock<IReturnCirculationService>();
        var dueDate = new DateTime(2026, 10, 15);
        var rows = new List<ActiveReturnLoanRow>
        {
            new()
            {
                BorrowId = 41, ReaderId = 9, ReaderName = "Former reader (Đã xóa)",
                BookId = 6, BookTitle = "Refactoring", BookCopyId = 18, Barcode = "BK-000018",
                BorrowDate = new DateTime(2026, 10, 1), DueDate = dueDate
            },
            new()
            {
                BorrowId = 42, ReaderId = 10, ReaderName = "Legacy reader (Đã xóa)",
                BookId = 7, BookTitle = "Design Patterns", BookCopyId = null,
                BorrowDate = new DateTime(2026, 10, 2), DueDate = dueDate
            }
        };
        service.Setup(x => x.SearchActiveLoansForReturn(string.Empty, 100)).Returns(rows);
        service.Setup(x => x.GetCurrentLateDays(dueDate)).Returns(3);

        var copyRepository = new Mock<BookCopyRepository>();
        copyRepository.Setup(x => x.GetByBookId(7)).Returns(new List<BookCopy>());
        var copyService = new BookCopyService(copyRepository.Object, new BookRepository());

        StaHelper.RunInSta(() =>
        {
            var vm = CreateViewModel(service.Object, copyService);

            Assert.Equal(2, vm.ActiveBorrowings.Count);
            var normal = vm.ActiveBorrowings[0];
            Assert.Equal(41, normal.BorrowId);
            Assert.Equal("Former reader (Đã xóa)", normal.ReaderName);
            Assert.Equal("Refactoring", normal.BookTitle);
            Assert.Equal("BK-000018", normal.CopyBarcode);
            Assert.Equal("01/10/2026", normal.BorrowDate);
            Assert.Equal("15/10/2026", normal.DueDate);
            Assert.Equal(3, normal.OverdueDays);

            var legacy = vm.ActiveBorrowings[1];
            Assert.Equal("Legacy reader (Đã xóa)", legacy.ReaderName);
            Assert.Null(legacy.BookCopyId);
            Assert.True(legacy.NeedsMapping);
            Assert.Equal("Cần đối chiếu", legacy.CopyBarcode);
            copyRepository.Verify(x => x.GetByBookId(It.IsAny<int>()), Times.Never);

            vm.SelectedRow = legacy;

            Assert.True(vm.NeedsLegacyMapping);
            copyRepository.Verify(x => x.GetByBookId(7), Times.Once);
            service.Verify(x => x.SearchActiveLoansForReturn(string.Empty, 100), Times.Once);
        });
    }

    [Fact]
    public void LegacyCopyMapping_RefreshesThroughBoundedSearchAndReselectsLoan()
    {
        var service = new Mock<IReturnCirculationService>();
        var before = new ActiveReturnLoanRow
        {
            BorrowId = 51,
            ReaderId = 11,
            ReaderName = "An",
            BookId = 12,
            BookTitle = "Clean Code",
            BookCopyId = null
        };
        var after = new ActiveReturnLoanRow
        {
            BorrowId = 51,
            ReaderId = 11,
            ReaderName = "An",
            BookId = 12,
            BookTitle = "Clean Code",
            BookCopyId = 24,
            Barcode = "BK-000024"
        };
        service.SetupSequence(x => x.SearchActiveLoansForReturn(string.Empty, 100))
            .Returns(new List<ActiveReturnLoanRow> { before })
            .Returns(new List<ActiveReturnLoanRow> { after });

        var legacyCopy = new BookCopy
        {
            CopyId = 24,
            BookId = 12,
            Barcode = "BK-000024",
            Status = BookCopyStatuses.Available
        };
        var copyRepository = new Mock<BookCopyRepository>();
        copyRepository.Setup(x => x.GetByBookId(12)).Returns(new List<BookCopy> { legacyCopy });
        var dialog = new Mock<IUserDialogService>();
        dialog.Setup(x => x.Confirm(It.IsAny<string>(), "Xác nhận barcode", false)).Returns(true);

        StaHelper.RunInSta(() =>
        {
            var vm = CreateViewModel(service.Object,
                new BookCopyService(copyRepository.Object, new BookRepository()), dialog.Object);
            vm.SelectedRow = vm.ActiveBorrowings[0];
            vm.SelectedLegacyCopy = Assert.Single(vm.LegacyCopyChoices);

            vm.LinkLegacyCopyCommand.Execute(null);

            Assert.Equal(51, vm.SelectedRow!.BorrowId);
            Assert.Equal(24, vm.SelectedRow.BookCopyId);
            Assert.False(vm.NeedsLegacyMapping);
            Assert.Equal("BK-000024", vm.SelectedRow.CopyBarcode);
            service.Verify(x => x.SearchActiveLoansForReturn(string.Empty, 100), Times.Exactly(2));
            service.Verify(x => x.LinkLegacyBorrowToCopy(51, 24), Times.Once);
        });
    }

    private static ReturnViewModel CreateViewModel(
        IReturnCirculationService circulation,
        BookCopyService? copyService = null,
        IUserDialogService? dialog = null)
    {
        var bookRepository = new Mock<BookRepository>();
        var readerRepository = new Mock<ReaderRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        return new ReturnViewModel(
            circulation,
            new BookService(bookRepository.Object, borrowRepository.Object),
            new ReaderService(readerRepository.Object, borrowRepository.Object),
            dialog ?? new Mock<IUserDialogService>().Object,
            copyService);
    }
}
