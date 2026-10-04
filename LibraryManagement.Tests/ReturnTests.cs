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

            var books = new List<Book> { new Book { BookId = 1, Title = "Refactoring" } };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Martin Fowler", IsDeleted = false } };
            var activeBorrows = new List<BorrowRecord>
            {
                new BorrowRecord
                {
                    BorrowId = 10,
                    BookId = 1,
                    ReaderId = 1,
                    BorrowDate = DateTime.Today.AddDays(-5),
                    DueDate = DateTime.Today.AddDays(2),
                    Status = "Borrowing"
                }
            };

            mockBookRepo.Setup(r => r.GetAll()).Returns(books);
            mockBookRepo.Setup(r => r.GetAllIncludingArchived()).Returns(books);
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(readers);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService, new Mock<IUserDialogService>().Object);
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal(10, vm.ActiveBorrowings[0].BorrowId);
                Assert.Equal("Refactoring", vm.ActiveBorrowings[0].BookTitle);
                Assert.Equal("Martin Fowler", vm.ActiveBorrowings[0].ReaderName);
            });
        }

        [Fact]
        public void ReturnViewModel_SearchByReaderOrBook_FiltersActiveBorrowings()
        {
            // Arrange (TC-RETURN-06)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var books = new List<Book>
            {
                new Book { BookId = 1, Title = "Clean Architecture" },
                new Book { BookId = 2, Title = "Design Patterns" }
            };
            var readers = new List<Reader>
            {
                new Reader { ReaderId = 1, FullName = "Uncle Bob", IsDeleted = false },
                new Reader { ReaderId = 2, FullName = "Gang of Four", IsDeleted = false }
            };
            var activeBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, BookId = 1, ReaderId = 1, Status = "Borrowing" },
                new BorrowRecord { BorrowId = 2, BookId = 2, ReaderId = 2, Status = "Borrowing" }
            };

            mockBookRepo.Setup(r => r.GetAll()).Returns(books);
            mockBookRepo.Setup(r => r.GetAllIncludingArchived()).Returns(books);
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(readers);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService, new Mock<IUserDialogService>().Object);
                Assert.Equal(2, vm.ActiveBorrowings.Count);

                // Act: Search for "Clean"
                vm.SearchText = "Clean";

                // Assert
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal("Clean Architecture", vm.ActiveBorrowings[0].BookTitle);

                // Act: Search for "Gang"
                vm.SearchText = "Gang";

                // Assert
                Assert.Single(vm.ActiveBorrowings);
                Assert.Equal("Gang of Four", vm.ActiveBorrowings[0].ReaderName);
            });
        }

        [Fact]
        public void ReturnViewModel_NoRecordSelected_ReturnCommandCannotExecute()
        {
            // Arrange (TC-RETURN-02)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBookRepo.Setup(r => r.GetAll()).Returns(new List<Book>());
            mockBookRepo.Setup(r => r.GetAllIncludingArchived()).Returns(new List<Book>());
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());

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
            circulation.Setup(x => x.GetBorrowingBooks()).Returns(new List<BorrowRecord>());

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
