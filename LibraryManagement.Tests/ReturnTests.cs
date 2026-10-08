using System;
using System.Collections.Generic;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class ReturnTests
    {
        [Fact]
        public void DamagedReturn_RequiresConditionNoteBeforeDatabaseWork()
        {
            var service = new BorrowService();
            var error = Assert.Throws<BusinessRuleException>(() =>
                service.ReturnBook(1, ReturnCondition.Damaged, "  "));
            Assert.Contains("ghi chú", error.Message);
        }

        [Fact]
        public void ReturnViewModel_Load_PopulatesActiveBorrowings()
        {
            // Arrange (TC-RETURN-01 / TC-RETURN-06)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockCopyRepo = new Mock<BookCopyRepository>();
            var activeBorrows = new List<ActiveReturnLoanRow>
            {
                new ActiveReturnLoanRow
                {
                    BorrowId = 10,
                    BookId = 1,
                    ReaderId = 1,
                    ReaderName = "Martin Fowler",
                    BookTitle = "Refactoring",
                    BookCopyId = 3,
                    Barcode = "BK-000003",
                    BorrowDate = DateTime.Today.AddDays(-5),
                    DueDate = DateTime.Today.AddDays(2)
                }
            };

            mockBorrowRepo.Setup(r => r.SearchActiveLoansForReturn("", 100)).Returns(activeBorrows);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService,
                    new Mock<IUserDialogService>().Object, new BookCopyService(mockCopyRepo.Object, mockBookRepo.Object));
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal(10, vm.ActiveBorrowings[0].BorrowId);
                Assert.Equal("Refactoring", vm.ActiveBorrowings[0].BookTitle);
                Assert.Equal("Martin Fowler", vm.ActiveBorrowings[0].ReaderName);
                Assert.Equal("BK-000003", vm.ActiveBorrowings[0].CopyBarcode);
                Assert.Equal(DateTime.Today.AddDays(-5).ToString("dd/MM/yyyy"), vm.ActiveBorrowings[0].BorrowDate);
                Assert.Equal(DateTime.Today.AddDays(2).ToString("dd/MM/yyyy"), vm.ActiveBorrowings[0].DueDate);
            });
            mockBorrowRepo.Verify(r => r.SearchActiveLoansForReturn("", 100), Times.Once);
            mockBorrowRepo.Verify(r => r.GetBorrowingRecords(), Times.Never);
            mockBookRepo.Verify(r => r.GetAll(), Times.Never);
            mockBookRepo.Verify(r => r.GetAllIncludingArchived(), Times.Never);
            mockReaderRepo.Verify(r => r.GetAll(true), Times.Never);
            mockCopyRepo.Verify(r => r.GetByBookId(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ReturnViewModel_SearchByReaderOrBook_UsesBackendResults()
        {
            // Arrange (TC-RETURN-06)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var activeBorrows = new List<ActiveReturnLoanRow>
            {
                new ActiveReturnLoanRow
                {
                    BorrowId = 1, BookId = 1, ReaderId = 1, ReaderName = "Uncle Bob",
                    BookTitle = "Clean Architecture", BookCopyId = 101, Barcode = "BK-000101"
                },
                new ActiveReturnLoanRow
                {
                    BorrowId = 2, BookId = 2, ReaderId = 2, ReaderName = "Gang of Four",
                    BookTitle = "Design Patterns", BookCopyId = 102, Barcode = "BK-000102"
                }
            };

            mockBorrowRepo.Setup(r => r.SearchActiveLoansForReturn("", 100)).Returns(activeBorrows);
            mockBorrowRepo.Setup(r => r.SearchActiveLoansForReturn("Clean", 100)).Returns(new List<ActiveReturnLoanRow> { activeBorrows[0] });
            mockBorrowRepo.Setup(r => r.SearchActiveLoansForReturn("Gang", 100)).Returns(new List<ActiveReturnLoanRow> { activeBorrows[1] });

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService, new Mock<IUserDialogService>().Object);
                Assert.Equal(2, vm.ActiveBorrowings.Count);

                // Act: Search for "Clean"
                vm.SearchText = "Clean";
                vm.Load();

                // Assert
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal("Clean Architecture", vm.ActiveBorrowings[0].BookTitle);

                // Act: Search for "Gang"
                vm.SearchText = "Gang";
                vm.Load();

                // Assert
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal("Gang of Four", vm.ActiveBorrowings[0].ReaderName);
            });
            mockBorrowRepo.Verify(r => r.SearchActiveLoansForReturn("Clean", 100), Times.Once);
            mockBorrowRepo.Verify(r => r.SearchActiveLoansForReturn("Gang", 100), Times.Once);
        }

        [Fact]
        public void ReturnViewModel_NoRecordSelected_ReturnCommandCannotExecute()
        {
            // Arrange (TC-RETURN-02)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBorrowRepo.Setup(r => r.SearchActiveLoansForReturn("", 100)).Returns(new List<ActiveReturnLoanRow>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService, new Mock<IUserDialogService>().Object);
                vm.SelectedRow = null;

                // Act & Assert
                Assert.False(vm.ReturnCommand.CanExecute(null));
            });
        }

        [Fact]
        public void ReturnViewModel_ConfirmedReturn_DelegatesSuccessMessage()
        {
            StaHelper.RunInSta(() =>
            {
                var h = CreateReturnViewModel();
                var result = new ReturnResult(10, 2, 3, 4, DateTime.Today, DateTime.Today, ReturnCondition.Normal);
                h.Circulation.Setup(x => x.ReturnBook(10, ReturnCondition.Normal, string.Empty)).Returns(result);
                h.Dialog.Setup(x => x.Confirm(It.IsAny<string>(), "Xác nhận trả sách", false)).Returns(true);
                h.ViewModel.SelectedRow = new ActiveBorrowRow
                {
                    BorrowId = 10, BookId = 3, BookCopyId = 4, CopyBarcode = "BC-4",
                    BookTitle = "Clean Code", ReaderName = "An"
                };

                h.ViewModel.ReturnCommand.Execute(null);

                h.Circulation.Verify(x => x.ReturnBook(10, ReturnCondition.Normal, string.Empty), Times.Once);
                h.Circulation.Verify(x => x.SearchActiveLoansForReturn(string.Empty, 100), Times.Exactly(2));
                h.Dialog.Verify(x => x.ShowInfo(It.Is<string>(message => message.Contains("phiếu #10")), "Kết quả trả sách"), Times.Once);
            });
        }

        [Fact]
        public void ReturnViewModel_RejectedReturnDoesNotCallService()
        {
            StaHelper.RunInSta(() =>
            {
                var h = CreateReturnViewModel();
                h.Dialog.Setup(x => x.Confirm(It.IsAny<string>(), "Xác nhận trả sách", false)).Returns(false);
                h.ViewModel.SelectedRow = new ActiveBorrowRow
                {
                    BorrowId = 10, BookId = 3, BookCopyId = 4, CopyBarcode = "BC-4",
                    BookTitle = "Clean Code", ReaderName = "An"
                };

                h.ViewModel.ReturnCommand.Execute(null);

                h.Circulation.Verify(x => x.ReturnBook(It.IsAny<int>(), It.IsAny<ReturnCondition>(), It.IsAny<string?>()), Times.Never);
                h.Dialog.Verify(x => x.ShowInfo(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            });
        }

        [Fact]
        public void ReturnViewModel_ReturnErrorIsDelegatedToDialogService()
        {
            StaHelper.RunInSta(() =>
            {
                var h = CreateReturnViewModel();
                h.Dialog.Setup(x => x.Confirm(It.IsAny<string>(), "Xác nhận trả sách", false)).Returns(true);
                h.Circulation.Setup(x => x.ReturnBook(10, ReturnCondition.Normal, string.Empty))
                    .Throws(new BusinessRuleException("Không thể trả phiếu này."));
                h.ViewModel.SelectedRow = new ActiveBorrowRow
                {
                    BorrowId = 10, BookId = 3, BookCopyId = 4, CopyBarcode = "BC-4",
                    BookTitle = "Clean Code", ReaderName = "An"
                };

                h.ViewModel.ReturnCommand.Execute(null);

                h.Dialog.Verify(x => x.ShowError("Không thể trả phiếu này.", "Không thể trả sách"), Times.Once);
            });
        }

        private static (ReturnViewModel ViewModel, Mock<IReturnCirculationService> Circulation,
            Mock<IUserDialogService> Dialog) CreateReturnViewModel()
        {
            var circulation = new Mock<IReturnCirculationService>();
            circulation.Setup(x => x.SearchActiveLoansForReturn(string.Empty, It.IsAny<int>()))
                .Returns(new List<ActiveReturnLoanRow>());

            var books = new Mock<BookRepository>();
            books.Setup(x => x.GetAllIncludingArchived()).Returns(new List<Book>());
            var readers = new Mock<ReaderRepository>();
            readers.Setup(x => x.GetAll(true)).Returns(new List<Reader>());
            var repositoryBorrowing = new Mock<BorrowRepository>();
            repositoryBorrowing.Setup(x => x.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            var bookService = new BookService(books.Object, repositoryBorrowing.Object);
            var readerService = new ReaderService(readers.Object, repositoryBorrowing.Object);
            var dialog = new Mock<IUserDialogService>();
            var viewModel = new ReturnViewModel(circulation.Object, bookService, readerService, dialog.Object);
            return (viewModel, circulation, dialog);
        }

    }
}
