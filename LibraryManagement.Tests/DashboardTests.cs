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
    public class DashboardTests
    {
        [Fact]
        public void LoadData_MatchesDatabaseCalculations()
        {
            // Arrange (TC-DASH-01)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var books = new List<Book>
            {
                new Book { BookId = 1, Title = "C# 10", Quantity = 10, AvailableQuantity = 7 },
                new Book { BookId = 2, Title = "SQL", Quantity = 5, AvailableQuantity = 3 }
            };
            var readers = new List<Reader>
            {
                new Reader { ReaderId = 1, FullName = "Reader 1", IsDeleted = false },
                new Reader { ReaderId = 2, FullName = "Reader 2", IsDeleted = false }
            };
            var activeBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 101, Status = "Borrowing", DueDate = DateTime.Now.AddDays(3) },
                new BorrowRecord { BorrowId = 102, Status = "Borrowing", DueDate = DateTime.Now.AddDays(5) }
            };

            mockBookRepo.Setup(r => r.GetAll()).Returns(books);
            mockReaderRepo.Setup(r => r.GetAll(false)).Returns(readers);
            mockReaderRepo.Setup(r => r.GetAll(true)).Returns(readers);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            var vm = new DashboardViewModel(bookService, readerService, borrowService);

            // Assert
            Assert.Equal(15, vm.TotalBooks); // 10 + 5
            Assert.Equal(10, vm.AvailableBooks); // 7 + 3
            Assert.Equal(2, vm.TotalReaders); // 2 readers
            Assert.Equal(2, vm.CurrentlyBorrowed); // 2 records
        }

        [Fact]
        public void LoadData_OverdueCalculation_OnlyCountsPastDueDates()
        {
            // Arrange (TC-DASH-02)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var activeBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, DueDate = DateTime.Today.AddDays(-1), Status = "Borrowing" }, // Overdue
                new BorrowRecord { BorrowId = 2, DueDate = DateTime.Today, Status = "Borrowing" },            // Today (not overdue)
                new BorrowRecord { BorrowId = 3, DueDate = DateTime.Today.AddDays(1), Status = "Borrowing" }   // Tomorrow (not overdue)
            };

            mockBookRepo.Setup(r => r.GetAll()).Returns(new List<Book>());
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            var vm = new DashboardViewModel(bookService, readerService, borrowService);

            // Assert
            Assert.Equal(1, vm.OverdueBooks); // Only yesterday is strictly < DateTime.Now.Date
        }

        [Fact]
        public void LoadData_EmptyDatabase_AllStatsZeroAndNoException()
        {
            // Arrange (TC-DASH-03)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBookRepo.Setup(r => r.GetAll()).Returns(new List<Book>());
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            var vm = new DashboardViewModel(bookService, readerService, borrowService);

            // Assert
            Assert.Equal(0, vm.TotalBooks);
            Assert.Equal(0, vm.AvailableBooks);
            Assert.Equal(0, vm.TotalReaders);
            Assert.Equal(0, vm.CurrentlyBorrowed);
            Assert.Equal(0, vm.OverdueBooks);
            Assert.Empty(vm.RecentBorrowings);
            Assert.Empty(vm.RecentReturnings);
        }

        [Fact]
        public void LoadData_AfterBorrowOperation_CountersReflectStateChange()
        {
            // Arrange (TC-DASH-04)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var books = new List<Book> { new Book { BookId = 1, Quantity = 5, AvailableQuantity = 5 } };
            var activeBorrows = new List<BorrowRecord>();

            mockBookRepo.Setup(r => r.GetAll()).Returns(books);
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            var vm = new DashboardViewModel(bookService, readerService, borrowService);
            Assert.Equal(5, vm.AvailableBooks);
            Assert.Equal(0, vm.CurrentlyBorrowed);

            // Simulate Borrow (Available - 1, Currently Borrowed + 1)
            books[0].AvailableQuantity = 4;
            activeBorrows.Add(new BorrowRecord { BorrowId = 1, Status = "Borrowing", DueDate = DateTime.Now.AddDays(7) });

            // Act
            vm.LoadData();

            // Assert
            Assert.Equal(4, vm.AvailableBooks);
            Assert.Equal(1, vm.CurrentlyBorrowed);
        }

        [Fact]
        public void LoadData_AfterReturnOperation_CountersReflectStateChange()
        {
            // Arrange (TC-DASH-05)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var books = new List<Book> { new Book { BookId = 1, Quantity = 5, AvailableQuantity = 4 } };
            var activeBorrows = new List<BorrowRecord> { new BorrowRecord { BorrowId = 1, Status = "Borrowing", DueDate = DateTime.Now.AddDays(7) } };

            mockBookRepo.Setup(r => r.GetAll()).Returns(books);
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader>());
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeBorrows);
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(new List<BorrowRecord>());

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            var vm = new DashboardViewModel(bookService, readerService, borrowService);
            Assert.Equal(4, vm.AvailableBooks);
            Assert.Equal(1, vm.CurrentlyBorrowed);

            // Simulate Return (Available + 1, Currently Borrowed - 1)
            books[0].AvailableQuantity = 5;
            activeBorrows.Clear();

            // Act
            vm.LoadData();

            // Assert
            Assert.Equal(5, vm.AvailableBooks);
            Assert.Equal(0, vm.CurrentlyBorrowed);
        }

        [Fact]
        public void LoadData_RecentBorrowings_RetainsRecordAfterReturned()
        {
            // Arrange (TC-DASH-06)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var history = new List<BorrowRecord>
            {
                new BorrowRecord
                {
                    BorrowId = 10,
                    BookId = 1,
                    ReaderId = 1,
                    BorrowDate = DateTime.Now.AddDays(-2),
                    DueDate = DateTime.Now.AddDays(5),
                    ReturnDate = DateTime.Now,
                    Status = "Returned" // Returned record must still appear in RecentBorrowings!
                }
            };

            mockBookRepo.Setup(r => r.GetAll()).Returns(new List<Book> { new Book { BookId = 1, Title = "Clean Architecture" } });
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader> { new Reader { ReaderId = 1, FullName = "Bob" } });
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(history);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            var vm = new DashboardViewModel(bookService, readerService, borrowService);

            // Assert
            Assert.Single(vm.RecentBorrowings);
            Assert.Equal("Bob", vm.RecentBorrowings[0].ReaderName);
            Assert.Equal("Clean Architecture", vm.RecentBorrowings[0].BookTitle);
        }

        [Fact]
        public void LoadData_RecentReturnings_ReturnsExactly5LatestOrderedByReturnDateDesc()
        {
            // Arrange (TC-DASH-07)
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var now = DateTime.Now;
            var history = new List<BorrowRecord>();
            for (int i = 1; i <= 7; i++)
            {
                history.Add(new BorrowRecord
                {
                    BorrowId = i,
                    BookId = 1,
                    ReaderId = 1,
                    Status = "Returned",
                    ReturnDate = now.AddDays(i)
                });
            }

            mockBookRepo.Setup(r => r.GetAll()).Returns(new List<Book> { new Book { BookId = 1, Title = "C# Mastery" } });
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(new List<Reader> { new Reader { ReaderId = 1, FullName = "Alice" } });
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBorrowRepo.Setup(r => r.GetHistory(null, null, null, null, null)).Returns(history);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            var vm = new DashboardViewModel(bookService, readerService, borrowService);

            // Assert
            Assert.Equal(5, vm.RecentReturnings.Count);
            // Latest return was now.AddDays(7), which should be first in the list
            Assert.Equal(now.AddDays(7).ToString("dd/MM/yyyy"), vm.RecentReturnings[0].ReturnDate);
        }
    }
}
