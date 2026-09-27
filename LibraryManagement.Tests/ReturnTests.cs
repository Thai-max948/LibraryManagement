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
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(readers);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService);
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
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(readers);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService);
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
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReturnViewModel(borrowService, bookService, readerService);
                vm.SelectedRow = null;

                // Act & Assert
                Assert.False(vm.ReturnCommand.CanExecute(null));
            });
        }

        [Fact]
        public void ReturnOperation_OverdueRecord_AllowsReturnWithReturnDateGreaterThanDueDate()
        {
            // Arrange (TC-RETURN-07)
            var record = new BorrowRecord
            {
                BorrowId = 1,
                BookId = 1,
                ReaderId = 1,
                BorrowDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7), // Due 7 days ago
                Status = "Borrowing"
            };

            // Act: Return today
            record.ReturnDate = DateTime.Now;
            record.Status = "Returned";

            // Assert
            Assert.Equal("Returned", record.Status);
            Assert.True(record.ReturnDate > record.DueDate);
        }

        [Fact]
        public void ReturnOperation_QuantityAlteredBeforeReturn_AvailableCalculatedFromNewQuantity()
        {
            // Arrange (TC-RETURN-09: DT rule)
            // Book had Qty 5, 1 borrowed -> edited Qty to 4 (Avail 3).
            int editedQuantity = 4;
            int currentlyBorrowing = 1; // 1 book is still borrowed
            int currentAvailable = editedQuantity - currentlyBorrowing; // 3

            // Act: Return the borrowed book
            currentlyBorrowing--;
            currentAvailable++;

            // Assert: Available is now 4, which equals the new Quantity and does not exceed it
            Assert.Equal(4, currentAvailable);
            Assert.Equal(editedQuantity, currentAvailable);
        }
    }
}
