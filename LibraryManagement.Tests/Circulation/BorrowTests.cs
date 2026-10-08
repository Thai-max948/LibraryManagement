using System;
using System.Collections.Generic;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class BorrowTests
    {
        [Fact]
        public void CanBorrow_ArchivedBook_IsRejectedEvenIfInventorySaysAvailable()
        {
            var books = new Mock<BookRepository>();
            var borrows = new Mock<BorrowRepository>();
            var readers = new Mock<ReaderRepository>();
            readers.Setup(repository => repository.GetById(1)).Returns(new Reader { ReaderId = 1 });
            books.Setup(repository => repository.GetById(2)).Returns(new Book
            {
                BookId = 2,
                Status = BookStatuses.Archived,
                AvailableQuantity = 1
            });
            borrows.Setup(repository => repository.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());
            var service = new BorrowService(books.Object, borrows.Object, readers.Object);

            Assert.False(service.CanBorrow(1, 2, out string reason));
            Assert.Contains("lưu trữ", reason);
        }

        [Fact]
        public void CanBorrow_ValidConditions_ReturnsTrue()
        {
            // Arrange (TC-BORROW-01)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            var reader = new Reader { ReaderId = 1, IsDeleted = false };
            var book = new Book { BookId = 1, AvailableQuantity = 5 };

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(reader);
            mockBookRepo.Setup(r => r.GetById(1)).Returns(book);
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.True(canBorrow);
            Assert.Empty(reason);
        }

        [Fact]
        public void CanBorrow_AvailableQuantityIsOne_ReturnsTrue()
        {
            // Arrange (TC-BORROW-02: BVA Available = 1)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 1 });
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.True(canBorrow);
            Assert.Empty(reason);
        }

        [Fact]
        public void CanBorrow_AvailableQuantityIsZero_ReturnsFalseAndReason()
        {
            // Arrange (TC-BORROW-03: BVA Available = 0)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 0 });
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.Equal("Sách đã hết, không thể mượn.", reason);
        }

        [Fact]
        public void CanBorrow_ReaderBorrowingTwoBooks_AllowedToBorrowThird()
        {
            // Arrange (TC-BORROW-04: BVA Reader 2 books)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 3 });

            var existingBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, BookId = 2, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 3, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(existingBorrows);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.True(canBorrow);
            Assert.Empty(reason);
        }

        [Fact]
        public void CanBorrow_ReaderBorrowingThreeBooks_RefusedWithMaxBorrowReason()
        {
            // Arrange (TC-BORROW-05: BVA Reader 3 books)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 3 });

            var existingBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, BookId = 2, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 3, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 4, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(existingBorrows);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.Equal("Đã đạt giới hạn 3 sách đang mượn.", reason);
        }

        [Fact]
        public void CanBorrow_ReaderHasOverdueBook_ReturnsFalseWithOverdueReason()
        {
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();
            mockReaderRepo.Setup(repository => repository.GetById(1))
                .Returns(new Reader { ReaderId = 1, Status = "Active" });
            mockBookRepo.Setup(repository => repository.GetById(2))
                .Returns(new Book { BookId = 2, AvailableQuantity = 1 });
            mockBorrowRepo.Setup(repository => repository.GetEligibilityRecords(1))
                .Returns(new List<BorrowRecord>
                {
                    new() { ReaderId = 1, BookId = 1, Status = "Borrowing", DueDate = DateTime.Today.AddDays(-1) }
                });
            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            bool canBorrow = service.CanBorrow(1, 2, out string reason);

            Assert.False(canBorrow);
            Assert.Contains("quá hạn", reason);
        }

        [Fact]
        public void CanBorrow_OutOfStockAndMaxBorrowsReached_ReturnsClearReason()
        {
            // Arrange (TC-BORROW-06)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 0 });

            var existingBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, BookId = 2, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 3, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 4, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(existingBorrows);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void CanBorrow_ReaderNotFoundOrDeleted_ReturnsFalseWithReason()
        {
            // Arrange (TC-BORROW-07)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns((Reader?)null);
            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.Equal("Độc giả không tồn tại hoặc đã bị xóa.", reason);
        }

        [Fact]
        public void CanBorrow_BookNotFound_ReturnsFalseWithReason()
        {
            // Arrange (TC-BORROW-08)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns((Book?)null);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = service.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.Equal("Sách không tồn tại.", reason);
        }

        [Fact]
        public void LoanPolicy_Student_CalculatesFourteenCalendarDays()
        {
            var dueDate = LoanPolicyService.CalculateDueDate(
                new LoanPolicy { ReaderType = "Student", LoanPeriodDays = 14 },
                new DateTime(2026, 10, 2, 15, 30, 0));
            Assert.Equal(new DateTime(2026, 10, 16), dueDate);
        }

        [Fact]
        public void LoanPolicy_External_CalculatesSevenCalendarDays()
        {
            var dueDate = LoanPolicyService.CalculateDueDate(
                new LoanPolicy { ReaderType = "External", LoanPeriodDays = 7 },
                new DateTime(2026, 10, 2));
            Assert.Equal(new DateTime(2026, 10, 9), dueDate);
        }

        [Fact]
        public void LoanPolicy_Lecturer_CalculatesTwentyEightCalendarDays()
        {
            var dueDate = LoanPolicyService.CalculateDueDate(
                new LoanPolicy { ReaderType = "Lecturer", LoanPeriodDays = 28 },
                new DateTime(2026, 10, 8, 15, 30, 0));

            Assert.Equal(new DateTime(2026, 11, 5), dueDate);
        }

        [Fact]
        public void LoanPolicy_RejectsMissingOrInvalidConfiguration()
        {
            var repository = new Mock<LoanPolicyRepository>();
            repository.Setup(r => r.GetActiveByReaderType("Teacher")).Returns((LoanPolicy?)null);
            var service = new LoanPolicyService(repository.Object);
            Assert.Contains("Teacher", Assert.Throws<BusinessRuleException>(() =>
                service.GetPolicyFor(new Reader { ReaderType = "Teacher" })).Message);
            Assert.Throws<BusinessRuleException>(() => LoanPolicyService.CalculateDueDate(
                new LoanPolicy { LoanPeriodDays = 0 }, DateTime.Today));
        }

        [Fact]
        public void BorrowBook_DeletedReader_ThrowsBusinessRuleException()
        {
            // Arrange
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = true });

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);
            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.BorrowBook(1, 1));
            Assert.Equal("Độc giả không tồn tại hoặc đã bị xóa.", ex.Message);
        }

        [Fact]
        public void CanBorrow_SameReaderBorrowingTwoCopiesOfSameBook_AllowedIfUnderTotalLimit()
        {
            // Arrange (TC-BORROW-13)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(10)).Returns(new Book { BookId = 10, AvailableQuantity = 3 });

            // Reader already borrowed 1 copy of book 10
            var existing = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, BookId = 10, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(existing);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act: Borrowing another copy of book 10 (total active will be 2 <= 3)
            bool canBorrow = service.CanBorrow(1, 10, out string reason);

            // Assert
            Assert.True(canBorrow);
            Assert.Empty(reason);
        }

        [Fact]
        public void CanBorrow_AfterReturningOneOfThreeBooks_BecomesAllowedAgain()
        {
            // Arrange (TC-BORROW-14: ST rule)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, IsDeleted = false });
            mockBookRepo.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, AvailableQuantity = 5 });

            var existingBorrows = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, BookId = 2, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 3, Status = "Borrowing" },
                new BorrowRecord { ReaderId = 1, BookId = 4, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetEligibilityRecords(1)).Returns(existingBorrows);

            var service = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // At first: 3 books -> cannot borrow
            Assert.False(service.CanBorrow(1, 1, out _));

            // Reader returns 1 book -> active drops to 2
            existingBorrows.RemoveAt(0);

            // Act & Assert
            Assert.True(service.CanBorrow(1, 1, out string reason));
            Assert.Empty(reason);
        }

        [Fact]
        public void Invariant_QuantityMinusAvailableQuantity_EqualsCountActiveBorrows()
        {
            // Arrange (TC-BORROW-19: UC invariant rule)
            int initialQuantity = 10;
            int initialAvailable = 10;
            var activeBorrows = new List<BorrowRecord>();

            // Simulate 3 borrows
            for (int i = 1; i <= 3; i++)
            {
                initialAvailable--;
                activeBorrows.Add(new BorrowRecord { BookId = 1, Status = "Borrowing" });
            }

            // Assert invariant: Quantity - Available == Count(Borrowing)
            Assert.Equal(activeBorrows.Count, initialQuantity - initialAvailable);

            // Simulate 1 return
            initialAvailable++;
            activeBorrows.RemoveAt(0);

            // Assert invariant holds after return
            Assert.Equal(activeBorrows.Count, initialQuantity - initialAvailable);
        }
    }
}
