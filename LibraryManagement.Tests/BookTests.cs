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
    public class BookTests
    {
        [Fact]
        public void AddBook_ValidData_SetsAvailableQuantityAndReturnsId()
        {
            // Arrange (TC-BOOK-01)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var book = new Book
            {
                Title = "Clean Code",
                Author = "Robert C. Martin",
                Category = "Programming",
                PublishYear = 2008,
                Quantity = 5
            };

            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(10);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            int newId = service.AddBook(book);

            // Assert
            Assert.Equal(10, newId);
            Assert.Equal(5, book.AvailableQuantity); // AvailableQuantity set equal to Quantity
            mockBookRepo.Verify(r => r.Add(book), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void AddBook_EmptyTitle_ThrowsBusinessRuleException(string? emptyTitle)
        {
            // Arrange (TC-BOOK-02)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = emptyTitle!, Author = "Author", PublishYear = 2020, Quantity = 1 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddBook(book));
            Assert.Equal("Tiêu đề sách không được để trống.", ex.Message);
        }

        [Fact]
        public void AddBook_WhitespaceTitle_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-03)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "    ", Author = "Author", PublishYear = 2020, Quantity = 1 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddBook(book));
            Assert.Equal("Tiêu đề sách không được để trống.", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AddBook_EmptyOrWhitespaceAuthor_ThrowsBusinessRuleException(string? author)
        {
            // Arrange (TC-BOOK-04)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Valid Title", Author = author!, PublishYear = 2020, Quantity = 1 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddBook(book));
            Assert.Equal("Tác giả không được để trống.", ex.Message);
        }

        [Fact]
        public void AddBook_EmptyCategory_IsAllowed()
        {
            // Arrange (TC-BOOK-05)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Title", Author = "Author", Category = "", PublishYear = 2020, Quantity = 1 };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
        }

        [Theory]
        [InlineData(-1)] // TC-BOOK-06
        [InlineData(0)]  // TC-BOOK-07
        public void AddBook_InvalidPublishYear_ThrowsBusinessRuleException(int year)
        {
            // Arrange
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Title", Author = "Author", PublishYear = year, Quantity = 1 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddBook(book));
            Assert.Equal("Năm xuất bản không hợp lệ.", ex.Message);
        }

        [Fact]
        public void AddBook_PublishYearOne_AllowedAsLowerBoundary()
        {
            // Arrange (TC-BOOK-08)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Ancient Manuscript", Author = "Author", PublishYear = 1, Quantity = 1 };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
        }

        [Fact]
        public void AddBook_NegativeQuantity_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-10)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Title", Author = "Author", PublishYear = 2020, Quantity = -1 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddBook(book));
            Assert.Equal("Số lượng không được âm.", ex.Message);
        }

        [Theory]
        [InlineData(0)]              // TC-BOOK-11
        [InlineData(1)]              // TC-BOOK-12
        [InlineData(int.MaxValue)]   // TC-BOOK-13
        public void AddBook_ValidQuantityBoundaries_SavesSuccessfully(int qty)
        {
            // Arrange
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var book = new Book { Title = "Title", Author = "Author", PublishYear = 2020, Quantity = qty };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
            Assert.Equal(qty, book.AvailableQuantity);
        }

        [Fact]
        public void AddBook_Max255Title_SavesSuccessfully()
        {
            // Arrange (TC-BOOK-16)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            string title255 = new string('A', 255);
            var book = new Book { Title = title255, Author = "Author", PublishYear = 2020, Quantity = 1 };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
        }

        [Fact]
        public void AddBook_TitleWithSpecialCharsQuotesAndInjectionPattern_PreservedAccurately()
        {
            // Arrange (TC-BOOK-19)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            const string specialTitle = "O'Reilly \"C#\"; -- % _";
            var book = new Book { Title = specialTitle, Author = "Author", PublishYear = 2020, Quantity = 1 };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
            Assert.Equal(specialTitle, book.Title);
        }

        [Fact]
        public void AddBook_VietnameseUnicode_PreservedAccurately()
        {
            // Arrange (TC-BOOK-20)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockBookRepo.Setup(r => r.Add(It.IsAny<Book>())).Returns(1);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            const string viTitle = "Lập trình C# căn bản";
            const string viAuthor = "Nguyễn Văn Ạ";
            var book = new Book { Title = viTitle, Author = viAuthor, PublishYear = 2024, Quantity = 3 };

            // Act
            int id = service.AddBook(book);

            // Assert
            Assert.Equal(1, id);
            Assert.Equal(viTitle, book.Title);
            Assert.Equal(viAuthor, book.Author);
        }

        [Fact]
        public void UpdateBook_ValidChange_UpdatesSuccessfully()
        {
            // Arrange (TC-BOOK-24)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var existing = new Book { BookId = 1, Title = "Old Title", Author = "Author", Quantity = 5, AvailableQuantity = 5 };
            mockBookRepo.Setup(r => r.GetById(1)).Returns(existing);
            mockBookRepo.Setup(r => r.Update(It.IsAny<Book>())).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "New Title", Author = "Author", PublishYear = 2020, Quantity = 5 };

            // Act
            service.UpdateBook(updated);

            // Assert
            mockBookRepo.Verify(r => r.Update(It.Is<Book>(b => b.Title == "New Title")), Times.Once);
        }

        [Fact]
        public void UpdateBook_QuantityLessThanBorrowingCount_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-26)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            // 5 total, 2 available -> 3 currently borrowed
            var existing = new Book { BookId = 1, Title = "Title", Author = "Author", Quantity = 5, AvailableQuantity = 2 };
            mockBookRepo.Setup(r => r.GetById(1)).Returns(existing);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Attempting to set Quantity to 2 (which is < borrowing of 3)
            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 2 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.UpdateBook(updated));
            Assert.Equal("Không thể đặt Quantity nhỏ hơn số đang được mượn (3).", ex.Message);
        }

        [Fact]
        public void UpdateBook_QuantityEqualToBorrowingCount_SetsAvailableToZero()
        {
            // Arrange (TC-BOOK-27)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            // 5 total, 2 available -> 3 borrowing
            var existing = new Book { BookId = 1, Title = "Title", Author = "Author", Quantity = 5, AvailableQuantity = 2 };
            mockBookRepo.Setup(r => r.GetById(1)).Returns(existing);
            mockBookRepo.Setup(r => r.Update(It.IsAny<Book>())).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Setting Quantity to exactly 3
            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 3 };

            // Act
            service.UpdateBook(updated);

            // Assert
            Assert.Equal(0, updated.AvailableQuantity); // Available = 3 - 3 = 0
            mockBookRepo.Verify(r => r.Update(updated), Times.Once);
        }

        [Fact]
        public void UpdateBook_IncreaseQuantity_IncreasesAvailableQuantityByDelta()
        {
            // Arrange (TC-BOOK-28)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            // 5 total, 2 available (3 borrowing) -> increase to 8 total
            var existing = new Book { BookId = 1, Title = "Title", Author = "Author", Quantity = 5, AvailableQuantity = 2 };
            mockBookRepo.Setup(r => r.GetById(1)).Returns(existing);
            mockBookRepo.Setup(r => r.Update(It.IsAny<Book>())).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 8 };

            // Act
            service.UpdateBook(updated);

            // Assert
            Assert.Equal(5, updated.AvailableQuantity); // 8 - 3 = 5
        }

        [Fact]
        public void UpdateBook_DecreaseQuantityWhenNoOneBorrowing_UpdatesAvailableQuantityToNewTotal()
        {
            // Arrange (TC-BOOK-29)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            // 5 total, 5 available (0 borrowing) -> reduce to 2 total
            var existing = new Book { BookId = 1, Title = "Title", Author = "Author", Quantity = 5, AvailableQuantity = 5 };
            mockBookRepo.Setup(r => r.GetById(1)).Returns(existing);
            mockBookRepo.Setup(r => r.Update(It.IsAny<Book>())).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 2 };

            // Act
            service.UpdateBook(updated);

            // Assert
            Assert.Equal(2, updated.AvailableQuantity); // 2 - 0 = 2
        }

        [Fact]
        public void UpdateBook_EmptyTitleOrAuthor_ThrowsValidationException()
        {
            // Arrange (TC-BOOK-30)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "", Author = "Author", PublishYear = 2020, Quantity = 5 };

            // Act & Assert
            Assert.Throws<BusinessRuleException>(() => service.UpdateBook(updated));
        }

        [Fact]
        public void DeleteBook_BookWithNoActiveBorrows_DeletesSuccessfully()
        {
            // Arrange (TC-BOOK-31)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBookRepo.Setup(r => r.Delete(1)).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            service.DeleteBook(1);

            // Assert
            mockBookRepo.Verify(r => r.Delete(1), Times.Once);
        }

        [Fact]
        public void DeleteBook_BookCurrentlyBorrowing_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-32)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var borrowing = new List<BorrowRecord> { new BorrowRecord { BookId = 1, Status = "Borrowing" } };
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(borrowing);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.DeleteBook(1));
            Assert.Equal("Không thể xóa sách đang được mượn.", ex.Message);
            mockBookRepo.Verify(r => r.Delete(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void DeleteBook_NonExistentBook_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-33)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBookRepo.Setup(r => r.Delete(999)).Returns(false);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.DeleteBook(999));
            Assert.Equal("Sách không tồn tại.", ex.Message);
        }

        [Fact]
        public void SearchBook_ByTitleOrAuthor_CallsRepositorySearch()
        {
            // Arrange (TC-BOOK-36 & TC-BOOK-37)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var expected = new List<Book> { new Book { BookId = 1, Title = "Clean Architecture" } };
            mockBookRepo.Setup(r => r.Search("Clean")).Returns(expected);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            var results = service.SearchBook("Clean");

            // Assert
            Assert.Single(results);
            Assert.Equal("Clean Architecture", results[0].Title);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void SearchBook_EmptyOrWhitespaceKeyword_ReturnsGetAll(string? keyword)
        {
            // Arrange (TC-BOOK-40)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var all = new List<Book> { new Book { BookId = 1 }, new Book { BookId = 2 } };
            mockBookRepo.Setup(r => r.GetAll()).Returns(all);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            var results = service.SearchBook(keyword!);

            // Assert
            Assert.Equal(2, results.Count);
            mockBookRepo.Verify(r => r.GetAll(), Times.Once);
            mockBookRepo.Verify(r => r.Search(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void SearchBook_NoMatchingResults_ReturnsEmptyList()
        {
            // Arrange (TC-BOOK-39)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBookRepo.Setup(r => r.Search("zzzzzz")).Returns(new List<Book>());

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            var results = service.SearchBook("zzzzzz");

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public void BooksViewModel_StatsCounters_CalculatedCorrectly()
        {
            // Arrange (TC-BOOK-42)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var bookList = new List<Book>
            {
                new Book { BookId = 1, Title = "Book 1", Quantity = 10, AvailableQuantity = 6 },
                new Book { BookId = 2, Title = "Book 2", Quantity = 5, AvailableQuantity = 4 }
            };
            mockBookRepo.Setup(r => r.GetAll()).Returns(bookList);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var vm = new BooksViewModel(service);

                Assert.Equal(15, vm.TotalBooks);     // 10 + 5
                Assert.Equal(10, vm.TotalAvailable); // 6 + 4
                Assert.Equal(5, vm.TotalBorrowed);   // (10-6) + (5-4) = 5
            });
        }
    }
}
