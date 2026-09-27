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
    public class HistoryTests
    {
        private (Mock<BookRepository>, Mock<ReaderRepository>, Mock<BorrowRepository>, HistoryViewModel) CreateSut(
            List<Book>? books = null,
            List<Reader>? readers = null,
            List<BorrowRecord>? history = null,
            List<BorrowRecord>? borrowing = null)
        {
            var mockBookRepo = new Mock<BookRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var defaultBooks = books ?? new List<Book>();
            var defaultReaders = readers ?? new List<Reader>();
            var defaultHistory = history ?? new List<BorrowRecord>();
            var defaultBorrowing = borrowing ?? new List<BorrowRecord>();

            mockBookRepo.Setup(r => r.GetAll()).Returns(defaultBooks);
            mockReaderRepo.Setup(r => r.GetAll(It.IsAny<bool>())).Returns(defaultReaders);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(defaultBorrowing);
            mockBorrowRepo.Setup(r => r.GetHistory(
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            )).Returns(defaultHistory);

            var bookService = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);
            var readerService = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            HistoryViewModel vm = null!;
            StaHelper.RunInSta(() =>
            {
                vm = new HistoryViewModel(borrowService, bookService, readerService);
            });

            return (mockBookRepo, mockReaderRepo, mockBorrowRepo, vm);
        }

        [Fact]
        public void Load_HistoryRecords_DisplaysRecordsProperly()
        {
            // Arrange (TC-HISTORY-01)
            var books = new List<Book> { new Book { BookId = 1, Title = "C# in Depth" } };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Jon Skeet", IsDeleted = false } };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord
                {
                    BorrowId = 1,
                    BookId = 1,
                    ReaderId = 1,
                    BorrowDate = DateTime.Today.AddDays(-10),
                    DueDate = DateTime.Today.AddDays(-3),
                    ReturnDate = DateTime.Today.AddDays(-2),
                    Status = "Returned"
                },
                new BorrowRecord
                {
                    BorrowId = 2,
                    BookId = 1,
                    ReaderId = 1,
                    BorrowDate = DateTime.Today.AddDays(-2),
                    DueDate = DateTime.Today.AddDays(5),
                    ReturnDate = null,
                    Status = "Borrowing"
                }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history, new List<BorrowRecord> { history[1] });

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                Assert.Equal(2, vm.Records.Count);
                Assert.Contains(vm.Records, r => r.ReaderName == "Jon Skeet" && r.BookTitle == "C# in Depth" && r.Status == "Returned");
                Assert.Contains(vm.Records, r => r.ReaderName == "Jon Skeet" && r.BookTitle == "C# in Depth" && r.Status == "Borrowing");
            });
        }

        [Fact]
        public void Filter_StatusBorrowing_QueriesOnlyBorrowingRecords()
        {
            // Arrange (TC-HISTORY-02)
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SelectedStatus = "Borrowing";

                // Assert
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, "Borrowing", null, null), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_StatusReturned_QueriesOnlyReturnedRecords()
        {
            // Arrange (TC-HISTORY-03)
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SelectedStatus = "Returned";

                // Assert
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, "Returned", null, null), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_StatusAll_QueriesWithNullStatus()
        {
            // Arrange (TC-HISTORY-04)
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SelectedStatus = "All";

                // Assert
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, null, null, null), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_SearchByReader_FiltersRecordsByReaderName()
        {
            // Arrange (TC-HISTORY-05)
            var books = new List<Book> { new Book { BookId = 1, Title = "Domain-Driven Design" } };
            var readers = new List<Reader>
            {
                new Reader { ReaderId = 1, FullName = "Eric Evans", IsDeleted = false },
                new Reader { ReaderId = 2, FullName = "Martin Fowler", IsDeleted = false }
            };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, BookId = 1, ReaderId = 1, BorrowDate = DateTime.Today.AddDays(-5), DueDate = DateTime.Today.AddDays(5), Status = "Borrowing" },
                new BorrowRecord { BorrowId = 2, BookId = 1, ReaderId = 2, BorrowDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(-2), Status = "Returned", ReturnDate = DateTime.Today.AddDays(-1) }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history);

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SearchText = "Eric";

                // Assert
                Assert.Single(vm.Records);
                Assert.Equal("Eric Evans", vm.Records[0].ReaderName);
            });
        }

        [Fact]
        public void Filter_SearchByBook_FiltersRecordsByBookTitle()
        {
            // Arrange (TC-HISTORY-06)
            var books = new List<Book>
            {
                new Book { BookId = 1, Title = "Clean Architecture" },
                new Book { BookId = 2, Title = "Refactoring" }
            };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Bob Martin", IsDeleted = false } };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, BookId = 1, ReaderId = 1, BorrowDate = DateTime.Today.AddDays(-5), DueDate = DateTime.Today.AddDays(5), Status = "Borrowing" },
                new BorrowRecord { BorrowId = 2, BookId = 2, ReaderId = 1, BorrowDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(-2), Status = "Returned", ReturnDate = DateTime.Today.AddDays(-1) }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history);

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SearchText = "Refactoring";

                // Assert
                Assert.Single(vm.Records);
                Assert.Equal("Refactoring", vm.Records[0].BookTitle);
            });
        }

        [Fact]
        public void Filter_CombineStatusAndDateRange_PassesParametersToRepository()
        {
            // Arrange (TC-HISTORY-07)
            var from = new DateTime(2026, 9, 1);
            var to = new DateTime(2026, 9, 30);
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.SelectedStatus = "Returned";
                vm.FromDate = from;
                vm.ToDate = to;

                // Assert
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, "Returned", from, to), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_FromDateEqualsToDate_PassesSameDates()
        {
            // Arrange (TC-HISTORY-08)
            var today = DateTime.Today;
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act
            StaHelper.RunInSta(() =>
            {
                vm.FromDate = today;
                vm.ToDate = today;

                // Assert
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, null, today, today), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_FromDateGreaterThanToDate_HandlesGracefullyWithoutCrash()
        {
            // Arrange (TC-HISTORY-09)
            var from = DateTime.Today.AddDays(5);
            var to = DateTime.Today.AddDays(-5);
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                vm.FromDate = from;
                vm.ToDate = to;

                mockBorrowRepo.Verify(r => r.GetHistory(null, null, null, from, to), Times.AtLeastOnce);
                Assert.NotNull(vm.Records);
            });
        }

        [Fact]
        public void Filter_OnlyFromDateOrOnlyToDate_PassesSingleBoundary()
        {
            // Arrange (TC-HISTORY-10)
            var date = new DateTime(2026, 9, 15);
            var (_, _, mockBorrowRepo, vm) = CreateSut();

            StaHelper.RunInSta(() =>
            {
                // Act 1: only FromDate
                vm.FromDate = date;
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, null, date, null), Times.AtLeastOnce);

                // Act 2: reset fromDate and set only ToDate
                vm.FromDate = null;
                vm.ToDate = date;
                mockBorrowRepo.Verify(r => r.GetHistory(null, null, null, null, date), Times.AtLeastOnce);
            });
        }

        [Fact]
        public void Filter_NoMatchingResults_GridIsEmptyWithoutCrash()
        {
            // Arrange (TC-HISTORY-11)
            var books = new List<Book> { new Book { BookId = 1, Title = "C# Guide" } };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Alice", IsDeleted = false } };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, BookId = 1, ReaderId = 1, BorrowDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7), Status = "Borrowing" }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history);

            StaHelper.RunInSta(() =>
            {
                // Act
                vm.SearchText = "NonExistentKeywordXYZ";

                // Assert
                Assert.Empty(vm.Records);
            });
        }

        [Fact]
        public void Filter_ClearFilter_RestoresFullRecordsList()
        {
            // Arrange (TC-HISTORY-12)
            var books = new List<Book> { new Book { BookId = 1, Title = "C# Guide" } };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Alice", IsDeleted = false } };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord { BorrowId = 1, BookId = 1, ReaderId = 1, BorrowDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7), Status = "Borrowing" }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history);

            StaHelper.RunInSta(() =>
            {
                // Act: apply search filter then clear
                vm.SearchText = "NonExistent";
                Assert.Empty(vm.Records);

                vm.SearchText = string.Empty;

                // Assert
                Assert.Single(vm.Records);
                Assert.Equal("Alice", vm.Records[0].ReaderName);
            });
        }

        [Fact]
        public void FilterOverdueCommand_FiltersOnlyOverdueRecords()
        {
            // Arrange
            var books = new List<Book>
            {
                new Book { BookId = 1, Title = "Overdue Book" },
                new Book { BookId = 2, Title = "Active Book" }
            };
            var readers = new List<Reader> { new Reader { ReaderId = 1, FullName = "Alice", IsDeleted = false } };
            var history = new List<BorrowRecord>
            {
                new BorrowRecord
                {
                    BorrowId = 1,
                    BookId = 1,
                    ReaderId = 1,
                    BorrowDate = DateTime.Today.AddDays(-20),
                    DueDate = DateTime.Today.AddDays(-5), // Overdue!
                    Status = "Borrowing"
                },
                new BorrowRecord
                {
                    BorrowId = 2,
                    BookId = 2,
                    ReaderId = 1,
                    BorrowDate = DateTime.Today.AddDays(-2),
                    DueDate = DateTime.Today.AddDays(10), // Not overdue
                    Status = "Borrowing"
                }
            };

            var (_, _, _, vm) = CreateSut(books, readers, history, history);

            StaHelper.RunInSta(() =>
            {
                // Act
                vm.FilterOverdueCommand.Execute(null);

                // Assert
                Assert.Equal("Overdue", vm.SelectedStatus);
                Assert.Single(vm.Records);
                Assert.True(vm.Records[0].IsOverdue);
                Assert.Equal("Overdue Book", vm.Records[0].BookTitle);
            });
        }
    }
}
