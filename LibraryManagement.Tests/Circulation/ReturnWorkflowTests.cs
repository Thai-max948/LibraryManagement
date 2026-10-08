using System;
using System.Collections.Generic;
using LibraryManagement.ViewModels;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public class ReturnWorkflowTests
{
    [Fact]
    public void SearchSupportsReaderBookAndBarcode_EnterSelectsLoanWithoutReturningIt()
    {
        const string barcode = "BK-000002";
        var loan = new BorrowRecord
        {
            BorrowId = 9,
            BookId = 1,
            BookCopyId = 2,
            ReaderId = 3,
            BorrowDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(7)
        };
        var circulation = new Mock<IReturnCirculationService>();
        circulation.Setup(x => x.SearchActiveLoansForReturn(It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<ActiveReturnLoanRow>
            {
                new()
                {
                    BorrowId = loan.BorrowId, BookId = loan.BookId, BookCopyId = loan.BookCopyId,
                    ReaderId = loan.ReaderId, ReaderName = "An", BookTitle = "Clean Code",
                    Barcode = barcode, BorrowDate = loan.BorrowDate, DueDate = loan.DueDate
                }
            });
        circulation.Setup(x => x.GetCurrentLateDays(loan.DueDate)).Returns(0);
        circulation.Setup(x => x.FindActiveReturnByBarcode(barcode)).Returns(loan);
        circulation.Setup(x => x.FindActiveReturnByBarcode("BK-999999"))
            .Throws(new BusinessRuleException("Barcode không tồn tại."));
        circulation.Setup(x => x.FindActiveReturnByBarcode("BK-000003"))
            .Throws(new BusinessRuleException("Không tìm thấy phiếu mượn đang hoạt động của barcode này."));

        var books = new Mock<BookRepository>();
        books.Setup(x => x.GetAllIncludingArchived()).Returns(new List<Book>
            { new() { BookId = 1, Title = "Clean Code" } });
        books.Setup(x => x.GetById(1)).Returns(new Book { BookId = 1, Title = "Clean Code" });
        var readers = new Mock<ReaderRepository>();
        readers.Setup(x => x.GetAll(true)).Returns(new List<Reader>
            { new() { ReaderId = 3, FullName = "An" } });
        readers.Setup(x => x.GetById(3)).Returns(new Reader { ReaderId = 3, FullName = "An" });
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(x => x.GetByBookId(1)).Returns(new List<BookCopy>
            { new() { CopyId = 2, BookId = 1, Barcode = barcode, Status = BookCopyStatuses.Borrowed } });
        StaHelper.RunInSta(() =>
        {
            var vm = new ReturnViewModel(circulation.Object, new BookService(books.Object, new BorrowRepository()),
                new ReaderService(readers.Object, new BorrowRepository()), new Mock<IUserDialogService>().Object,
                new BookCopyService(copies.Object, books.Object));

            vm.SearchText = "An";
            vm.Load();
            Assert.Single(vm.ActiveBorrowings);
            vm.SearchText = "Clean Code";
            vm.Load();
            Assert.Single(vm.ActiveBorrowings);

            vm.SearchText = barcode;
            vm.SearchEnterCommand.Execute(null);
            Assert.Equal(9, vm.SelectedRow!.BorrowId);
            Assert.Equal("An", vm.SelectedRow.ReaderName);
            Assert.Single(vm.ActiveBorrowings);
            Assert.True(vm.ReturnCommand.CanExecute(null));
            circulation.Verify(x => x.SearchActiveLoansForReturn("An", 100), Times.Once);
            circulation.Verify(x => x.SearchActiveLoansForReturn("Clean Code", 100), Times.Once);
            circulation.Verify(x => x.SearchActiveLoansForReturn(barcode, 100), Times.Never);
            circulation.Verify(x => x.FindActiveReturnByBarcode(barcode), Times.Once);
            circulation.Verify(x => x.ReturnBook(It.IsAny<int>(), It.IsAny<ReturnCondition>(), It.IsAny<string?>()), Times.Never);
            circulation.Verify(x => x.ReturnBookByBarcode(It.IsAny<string>(), It.IsAny<ReturnCondition>(), It.IsAny<string?>()), Times.Never);

            vm.SearchText = "BK-999999";
            vm.SearchEnterCommand.Execute(null);
            Assert.Null(vm.SelectedRow);
            Assert.Empty(vm.ActiveBorrowings);
            Assert.Contains("Không tìm thấy barcode", vm.SearchErrorMessage);

            vm.SearchText = "BK-000003";
            vm.SearchEnterCommand.Execute(null);
            Assert.Null(vm.SelectedRow);
            Assert.Contains("Không có phiếu mượn đang hoạt động", vm.SearchErrorMessage);
        });
    }

    [Fact]
    public void OutcomeCarriesAuthoritativeDueAndResolvedDatesWithoutCalculatingFeeDays()
    {
        var due = new DateTime(2026, 10, 1, 9, 0, 0);
        var loan = new BorrowRecord { BorrowId = 12, ReaderId = 7, BookId = 20, BookCopyId = 8, DueDate = due };
        DateTime resolvedAt = due.Date.AddDays(5).AddHours(23);
        var result = ReturnResult.FromLoan(loan, resolvedAt, ReturnCondition.Normal);
        Assert.Equal(due, result.DueDate);
        Assert.Equal(resolvedAt, result.ResolvedAt);
        Assert.Null(result.LateDays);
        Assert.False(result.IsOverdue);
        Assert.Equal(12, result.BorrowId);
        Assert.Equal(20, result.BookId);
        Assert.Equal(7, result.ReaderId);
        Assert.Equal(8, result.BookCopyId);
        Assert.NotNull(result.ReturnedAt);
    }

    [Theory]
    [InlineData(ReturnCondition.Damaged)]
    [InlineData(ReturnCondition.NeedsRepair)]
    [InlineData(ReturnCondition.Lost)]
    public void NonNormalOutcome_RequiresFeeReviewEvenWithoutOverdue(ReturnCondition disposition)
    {
        var loan = new BorrowRecord { BookCopyId = 3, DueDate = DateTime.Today };
        var result = ReturnResult.FromLoan(loan, DateTime.Today, disposition);
        Assert.True(result.RequiresFeeProcessing);
        Assert.Equal(disposition == ReturnCondition.Lost, result.IsLost);
        Assert.Equal(disposition == ReturnCondition.Lost, result.ReturnedAt == null);
    }

    [Theory]
    [InlineData(ReturnCondition.Damaged, DamageSeverity.Minor)]
    [InlineData(ReturnCondition.NeedsRepair, DamageSeverity.Major)]
    public void DamageOutcomeCarriesExplicitFeeDamageLevel(ReturnCondition disposition, DamageSeverity expected)
    {
        var loan = new BorrowRecord { BookCopyId = 3, DueDate = DateTime.Today };
        ReturnResult result = ReturnResult.FromLoan(loan, DateTime.Today, disposition);
        Assert.Equal(expected, result.DamageLevel);
    }

    [Theory]
    [InlineData(ReturnCondition.Damaged)]
    [InlineData(ReturnCondition.NeedsRepair)]
    public void RepairDisposition_RequiresNoteBeforeDatabaseWork(ReturnCondition condition)
    {
        Assert.Throws<BusinessRuleException>(() => new BorrowService().ReturnBook(1, condition, " "));
    }

    [Fact]
    public void InvalidDisposition_IsRejectedBeforeDatabaseWork() =>
        Assert.Throws<BusinessRuleException>(() => new BorrowService().ReturnBook(1, (ReturnCondition)123));

    [Fact]
    public void BarcodeLookup_UsesExactCopyAndActiveLoan()
    {
        var copies = new Mock<BookCopyRepository>();
        var loans = new Mock<BorrowRepository>();
        copies.Setup(x => x.GetByBarcode("BC0002")).Returns(new BookCopy { CopyId = 2, Status = BookCopyStatuses.Borrowed });
        var loan = new BorrowRecord { BorrowId = 4, BookCopyId = 2, Status = "Borrowing" };
        loans.Setup(x => x.GetActiveByCopyId(2)).Returns(loan);
        var service = new BorrowService(new BookRepository(), loans.Object, new ReaderRepository(), copies.Object);
        Assert.Same(loan, service.FindActiveReturnByBarcode("BC0002"));
        copies.Verify(x => x.GetByBarcode("BC0002"), Times.Once);
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode("BC000"));
        loans.Verify(x => x.GetActiveByCopyId(It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void BarcodeLookup_NonBorrowedCopyOrMissingLoanIsRejected()
    {
        var copies = new Mock<BookCopyRepository>();
        var loans = new Mock<BorrowRepository>();
        var service = new BorrowService(new BookRepository(), loans.Object, new ReaderRepository(), copies.Object);
        copies.Setup(x => x.GetByBarcode("A")).Returns(new BookCopy { CopyId = 1, Status = BookCopyStatuses.Available });
        copies.Setup(x => x.GetByBarcode("B")).Returns(new BookCopy { CopyId = 2, Status = BookCopyStatuses.Borrowed });
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode("A"));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode("B"));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(" "));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode("B "));
        Assert.Throws<BusinessRuleException>(() => service.FindActiveReturnByBarcode(new string('x', 101)));
        loans.Verify(x => x.GetActiveByCopyId(1), Times.Never);
    }
}
