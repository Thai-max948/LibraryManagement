using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using System.Windows.Controls;
using Xunit;

namespace LibraryManagement.Tests
{
    public class BookTests
    {
        [Theory]
        [InlineData("en", "English")]
        [InlineData("vi", "Vietnamese")]
        [InlineData("ja", "Japanese")]
        public void LanguageDisplay_UsesFriendlyNameWithoutChangingStoredCode(string code, string expected)
        {
            var book = new Book { Language = code };

            Assert.Equal(expected, book.LanguageDisplay);
            Assert.Equal(code, book.Language);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void LanguageDisplay_HandlesMissingCode(string? code)
            => Assert.Equal("Unknown", new Book { Language = code }.LanguageDisplay);

        [Fact]
        public void LanguageDisplay_HandlesUnknownCodeAndBookOptionsKeepStoredValue()
        {
            Assert.Equal("xx", new Book { Language = " xx " }.LanguageDisplay);

            var english = Assert.Single(LanguageCatalog.Options, option => option.DisplayName == "English");
            Assert.Equal("en", english.Code);
            Assert.Equal("en", LanguageCatalog.ResolveCode(english, english.DisplayName));
            Assert.Equal("en", LanguageCatalog.ResolveCode(null, " English "));
            Assert.Equal("xx", LanguageCatalog.ResolveCode(null, " xx "));
            var editVietnamese = Assert.Single(LanguageCatalog.GetBookOptions("vi"), option => option.Code == "vi");
            Assert.Equal("Vietnamese", editVietnamese.DisplayName);
            var editUnknown = Assert.Single(LanguageCatalog.GetBookOptions("xx"), option => option.Code == "xx");
            Assert.Equal("xx", editUnknown.DisplayName);
        }

        [Theory]
        [InlineData(" O'Reilly & Nhà xuất bản Trẻ ", " EN ", "O'Reilly & Nhà xuất bản Trẻ", "en")]
        [InlineData("   ", "   ", null, null)]
        [InlineData(null, null, null, null)]
        public void AddBook_NormalizesOptionalMetadata(string? publisher, string? language, string? expectedPublisher, string? expectedLanguage)
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.AddWithCopies(It.IsAny<Book>(), 0)).Returns(17);
            var book = new Book { Title = "Book", Author = "Author", PublishYear = 2026,
                Publisher = publisher, Language = language };

            Assert.Equal(17, new BookService(repository.Object, new Mock<BorrowRepository>().Object).AddBook(book));
            Assert.Equal(expectedPublisher, book.Publisher);
            Assert.Equal(expectedLanguage, book.Language);
            repository.Verify(repo => repo.AddWithCopies(book, 0), Times.Once);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(100000, 5000)]
        [InlineData(300000, 15000)]
        [InlineData(199999.99, 10000)]
        [InlineData(0.10, 0.01)]
        public void AddBook_DerivesRentalPriceFromBookPrice(decimal bookPrice, decimal expectedRentalPrice)
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.AddWithCopies(It.IsAny<Book>(), 0)).Returns(18);
            var book = new Book
            {
                Title = "Book", Author = "Author", PublishYear = 2026, Quantity = 0,
                ReplacementValue = bookPrice, RentalPrice = 999m
            };

            Assert.Equal(18, new BookService(repository.Object, new Mock<BorrowRepository>().Object).AddBook(book));
            Assert.Equal(expectedRentalPrice, book.RentalPrice);
            repository.Verify(repo => repo.AddWithCopies(It.Is<Book>(stored =>
                stored.ReplacementValue == bookPrice && stored.RentalPrice == expectedRentalPrice), 0), Times.Once);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1.234)]
        [InlineData(10000000000000000d)]
        public void AddBook_RejectsInvalidBookPriceBeforePersistence(decimal bookPrice)
        {
            var repository = new Mock<BookRepository>();
            var book = new Book
            {
                Title = "Book", Author = "Author", PublishYear = 2026, Quantity = 0,
                ReplacementValue = bookPrice
            };

            Assert.Throws<BusinessRuleException>(() =>
                new BookService(repository.Object, new Mock<BorrowRepository>().Object).AddBook(book));
            repository.Verify(repo => repo.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UpdateBook_PreservesBookPriceAndRecalculatesRentalPriceForOlderCallers()
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.GetById(7)).Returns(new Book
            {
                BookId = 7, Quantity = 1, AvailableQuantity = 1,
                ReplacementValue = 500m, RentalPrice = 25m
            });
            repository.Setup(repo => repo.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            var update = new Book
            {
                BookId = 7, Title = "Book", Author = "Author", PublishYear = 2026,
                Quantity = 1
            };

            new BookService(repository.Object, new Mock<BorrowRepository>().Object).UpdateBook(update);

            Assert.Equal(500m, update.ReplacementValue);
            Assert.Equal(25m, update.RentalPrice);
            repository.Verify(repo => repo.UpdateWithCopies(update), Times.Once);
        }

        [Fact]
        public void UpdateBook_RecalculatesRentalPriceAfterEachBookPriceChange()
        {
            var persisted = new Book
            {
                BookId = 7, Quantity = 1, AvailableQuantity = 1,
                ReplacementValue = 100000m, RentalPrice = 5000m
            };
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.GetById(7)).Returns(() => persisted);
            repository.Setup(repo => repo.UpdateWithCopies(It.IsAny<Book>())).Callback<Book>(book =>
            {
                persisted.ReplacementValue = book.ReplacementValue;
                persisted.RentalPrice = book.RentalPrice;
            }).Returns(true);
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

            foreach (var (bookPrice, rentalPrice) in new[]
            {
                (100000m, 5000m),
                (200000m, 10000m),
                (300000m, 15000m)
            })
            {
                var update = new Book
                {
                    BookId = 7, Title = "Book", Author = "Author", PublishYear = 2026, Quantity = 1,
                    ReplacementValue = bookPrice, RentalPrice = 999999m
                };

                service.UpdateBook(update);

                Assert.Equal(rentalPrice, update.RentalPrice);
                Assert.Equal(rentalPrice, persisted.RentalPrice);
            }
        }

        [Fact]
        public void BookPricingPolicy_UsesConfiguredRateAndCurrencyRounding()
        {
            var policy = new BookPricingPolicy(0.04m);
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.AddWithCopies(It.IsAny<Book>(), 0)).Returns(19);
            var book = new Book
            {
                Title = "Book", Author = "Author", PublishYear = 2026, Quantity = 0,
                ReplacementValue = 100000m
            };

            Assert.Equal(4000m, policy.CalculateRentalPrice(100000m));
            Assert.Equal(19, new BookService(repository.Object, new Mock<BorrowRepository>().Object, policy).AddBook(book));
            Assert.Equal(4000m, book.RentalPrice);
            Assert.Equal(0.01m, BookPricingPolicy.Default.CalculateRentalPrice(0.10m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BookPricingPolicy(-0.01m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BookPricingPolicy(1.01m));
        }

        [Fact]
        public void AddBookDialog_RecalculatesRentalPriceAsBookPriceChangesAndKeepsItReadOnly()
        {
            StaHelper.RunInSta(() =>
            {
                var dialog = new LibraryManagement.Views.Books.AddBookDialog();
                var bookPrice = GetTextBox(dialog, "ReplacementValueBox");
                var rentalPrice = GetTextBox(dialog, "RentalPriceBox");

                Assert.True(rentalPrice.IsReadOnly);
                bookPrice.Text = "100000";
                Assert.Equal(5000m, decimal.Parse(rentalPrice.Text, CultureInfo.CurrentCulture));
                bookPrice.Text = "200000";
                Assert.Equal(10000m, decimal.Parse(rentalPrice.Text, CultureInfo.CurrentCulture));
                bookPrice.Text = "-1";
                Assert.Equal(string.Empty, rentalPrice.Text);
                bookPrice.Text = "not a price";
                Assert.Equal(string.Empty, rentalPrice.Text);
            });
        }

        [Fact]
        public void EditBookDialog_RecalculatesRentalPriceWhenBookPriceChanges()
        {
            StaHelper.RunInSta(() =>
            {
                var dialog = new LibraryManagement.Views.Books.EditBookDialog(new Book
                {
                    BookId = 7, Title = "Book", Author = "Author", PublishYear = 2026, Quantity = 0,
                    ReplacementValue = 300000m, RentalPrice = 15000m
                });
                var bookPrice = GetTextBox(dialog, "ReplacementValueBox");
                var rentalPrice = GetTextBox(dialog, "RentalPriceBox");

                bookPrice.Text = "400000";

                Assert.Equal(20000m, decimal.Parse(rentalPrice.Text, CultureInfo.CurrentCulture));
                Assert.True(rentalPrice.IsReadOnly);
            });
        }

        private static TextBox GetTextBox(object dialog, string name)
            => (TextBox)(dialog.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(dialog)
                ?? throw new InvalidOperationException($"Could not find dialog field '{name}'."));

        [Fact]
        public void UpdateBook_NormalizesMetadataWithoutChangingCopyCount()
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.GetById(7)).Returns(new Book { BookId = 7, Quantity = 2, AvailableQuantity = 2 });
            repository.Setup(repo => repo.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            var book = new Book { BookId = 7, Title = "Book", Author = "Author", PublishYear = 2026,
                Quantity = 2, Publisher = "  Nhà xuất bản Trẻ  ", Language = " Vi " };

            new BookService(repository.Object, new Mock<BorrowRepository>().Object).UpdateBook(book);

            Assert.Equal("Nhà xuất bản Trẻ", book.Publisher);
            Assert.Equal("vi", book.Language);
            Assert.Equal(2, book.Quantity);
            repository.Verify(repo => repo.UpdateWithCopies(book), Times.Once);
        }

        [Fact]
        public void BooksViewModel_FiltersLanguageWithoutChangingRepositoryData()
        {
            var repository = new Mock<BookRepository>();
            var catalog = new List<Book>
            {
                new() { BookId = 1, Language = "vi" },
                new() { BookId = 2, Language = "en" },
                new() { BookId = 3 }
            };
            SetupBooksPage(repository, catalog);
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

            StaHelper.RunInSta(() =>
            {
                var viewModel = new BooksViewModel(service);
                viewModel.LanguageCodeFilter = " VI ";
                Assert.Equal(3, viewModel.Books.Count);
                viewModel.ApplyFiltersCommand.Execute(null);
                Assert.Equal(1, Assert.Single(viewModel.Books).BookId);
                Assert.Equal("en", LanguageCatalog.FilterOptions.Single(option => option.DisplayName == "English").Code);
                viewModel.LanguageCodeFilter = LanguageCatalog.UnknownFilterCode;
                viewModel.ApplyFiltersCommand.Execute(null);
                Assert.Equal(3, Assert.Single(viewModel.Books).BookId);
                viewModel.LanguageCodeFilter = "All";
                viewModel.ApplyFiltersCommand.Execute(null);
                Assert.Equal(3, viewModel.Books.Count);
            });
        }

        [Fact]
        public void BooksViewModel_LoadsPublisherValuesForGridIncludingNullAndArchivedBooks()
        {
            var repository = new Mock<BookRepository>();
            var activeBooks = new List<Book>
            {
                new() { BookId = 1, Publisher = "Prentice Hall" },
                new() { BookId = 2, Publisher = null },
                new() { BookId = 3, Publisher = string.Empty }
            };
            var archivedBook = new Book { BookId = 4, Publisher = "O'Reilly Media", Status = BookStatuses.Archived };
            SetupBooksPage(repository, activeBooks, new List<Book> { archivedBook });
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

            StaHelper.RunInSta(() =>
            {
                var viewModel = new BooksViewModel(service);
                Assert.Equal("Prentice Hall", viewModel.Books[0].Publisher);
                Assert.Null(viewModel.Books[1].Publisher);
                Assert.Equal(string.Empty, viewModel.Books[2].Publisher);

                viewModel.ViewScope = BookStatusFilter.Archived;
                Assert.Equal("O'Reilly Media", Assert.Single(viewModel.Books).Publisher);
            });
        }
        [Theory]
        [InlineData("978-0-13-235088-4", "9780132350884")]
        [InlineData("0-13-235088-2", "9780132350884")]
        [InlineData(" 9780132350884 ", "9780132350884")]
        [InlineData(null, null)]
        [InlineData("  ", null)]
        public void Isbn_NormalizesValidValues(string? input, string? expected)
        {
            Assert.Equal(expected, Isbn.Normalize(input));
        }

        [Theory]
        [InlineData("9780132350885")]
        [InlineData("0132350883")]
        [InlineData("97801323508A4")]
        [InlineData("123")]
        public void Isbn_RejectsInvalidValues(string input)
        {
            Assert.Throws<BusinessRuleException>(() => Isbn.Normalize(input));
        }

        [Fact]
        public void AddBook_ExistingIsbn_OffersExistingBookInsteadOfCreatingDuplicate()
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.GetByIsbn("9780132350884")).Returns(new Book { BookId = 42 });
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);
            var book = new Book { Title = "Clean Code", Author = "Robert Martin", PublishYear = 2008,
                Isbn = "0-13-235088-2", Quantity = 1 };

            var exception = Assert.Throws<DuplicateBookIsbnException>(() => service.AddBook(book));
            Assert.Equal(42, exception.ExistingBookId);
            repository.Verify(repo => repo.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UpdateBook_ExistingIsbnOnAnotherBook_IsRejected()
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(repo => repo.GetById(5)).Returns(new Book { BookId = 5, Quantity = 1, AvailableQuantity = 1 });
            repository.Setup(repo => repo.GetByIsbn("9780132350884")).Returns(new Book { BookId = 42 });
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

            var exception = Assert.Throws<DuplicateBookIsbnException>(() => service.UpdateBook(new Book
            {
                BookId = 5, Title = "Another book", Author = "Author", PublishYear = 2026,
                Quantity = 1, Isbn = "978-0-13-235088-4"
            }));
            Assert.Equal(42, exception.ExistingBookId);
            repository.Verify(repo => repo.UpdateWithCopies(It.IsAny<Book>()), Times.Never);
        }

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

            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(10);
            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            int newId = service.AddBook(book);

            // Assert
            Assert.Equal(10, newId);
            Assert.Equal(5, book.AvailableQuantity); // AvailableQuantity set equal to Quantity
            mockBookRepo.Verify(r => r.AddWithCopies(book, 5), Times.Once);
        }

        [Theory]
        [InlineData("Available")]
        [InlineData("Borrowed")]
        [InlineData("Lost")]
        [InlineData("Damaged")]
        [InlineData("UnderRepair")]
        [InlineData("Retired")]
        public void BookCopyService_AcceptsCoreStatuses(string status)
        {
            Assert.True(BookCopyService.IsValidStatus(status));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Deleted")]
        [InlineData("available")]
        public void BookCopyService_RejectsUnknownStatuses(string status)
        {
            Assert.False(BookCopyService.IsValidStatus(status));
        }

        [Fact]
        public void BookCopyService_AddCopy_UsesGeneratedCopyIdentity()
        {
            var books = new Mock<BookRepository>();
            books.Setup(r => r.GetById(1)).Returns(new Book { BookId = 1, Status = BookStatuses.Active });
            var copies = new Mock<BookCopyRepository>();
            copies.Setup(r => r.AddGeneratedCopies(1, 1, BookCopyStatuses.Available, "Good")).Returns(new[] { 42 });
            var service = new BookCopyService(copies.Object, books.Object);

            Assert.Equal(42, service.AddCopy(1));
            copies.Verify(r => r.AddGeneratedCopies(1, 1, BookCopyStatuses.Available, "Good"), Times.Once);
        }

        [Fact]
        public void BookCopyService_ChangeStatus_RejectsUnknownStatus()
        {
            var service = new BookCopyService(new Mock<BookCopyRepository>().Object);
            Assert.Throws<BusinessRuleException>(() => service.ChangeStatus(1, "OnShelf"));
        }

        [Fact]
        public void BookCopyService_ChangeStatus_RejectsBorrowedStatusOutsideCirculation()
        {
            var service = new BookCopyService(new Mock<BookCopyRepository>().Object);
            Assert.Throws<BusinessRuleException>(() => service.ChangeStatus(1, BookCopyStatuses.Borrowed));
        }

        [Theory]
        [InlineData(BookCopyStatuses.Available, BookCopyStatuses.Damaged, true)]
        [InlineData(BookCopyStatuses.Damaged, BookCopyStatuses.UnderRepair, true)]
        [InlineData(BookCopyStatuses.UnderRepair, BookCopyStatuses.Available, true)]
        [InlineData(BookCopyStatuses.Lost, BookCopyStatuses.Available, true)]
        [InlineData(BookCopyStatuses.Damaged, BookCopyStatuses.Available, true)]
        [InlineData(BookCopyStatuses.Borrowed, BookCopyStatuses.Available, false)]
        [InlineData(BookCopyStatuses.Retired, BookCopyStatuses.Damaged, false)]
        [InlineData(BookCopyStatuses.Retired, BookCopyStatuses.Available, false)]
        [InlineData(BookCopyStatuses.Retired, BookCopyStatuses.Retired, true)]
        public void BookCopyStatusRules_ManualTransitions(string current, string next, bool allowed)
        {
            Assert.Equal(allowed, BookCopyStatusRules.CanChangeManually(current, next));
        }

        [Fact]
        public void ReturnCondition_IncludesPhysicalReturnAndLostResolution()
        {
            Assert.Equal(new[] { ReturnCondition.Normal, ReturnCondition.Damaged, ReturnCondition.NeedsRepair, ReturnCondition.Lost },
                Enum.GetValues<ReturnCondition>());
        }

        [Fact]
        public void BookCopyInventory_CountsEveryStatusWithoutTreatingRetiredAsActive()
        {
            var statuses = new[] { BookCopyStatuses.Available, BookCopyStatuses.Borrowed,
                BookCopyStatuses.Damaged, BookCopyStatuses.UnderRepair, BookCopyStatuses.Lost,
                BookCopyStatuses.Retired };
            var inventory = BookCopyInventory.FromCopies(statuses.Select(status => new BookCopy { Status = status }));

            Assert.Equal(6, inventory.TotalCopies);
            Assert.Equal(5, inventory.ActiveCopies);
            Assert.Equal(1, inventory.Available);
            Assert.Equal(1, inventory.Borrowed);
            Assert.Equal(2, inventory.DamagedUnderRepair);
            Assert.Equal(3, inventory.Unavailable);
            Assert.Equal(1, inventory.Retired);
            Assert.True(inventory.IsBalanced);
        }

        [Theory]
        [InlineData(BookCopyStatuses.Damaged)]
        [InlineData(BookCopyStatuses.UnderRepair)]
        public void BookCopyStatusDisplay_UsesMergedLabelAndRepairTarget(string status)
        {
            Assert.Equal(@"Damaged\UnderRepair", BookCopyStatusDisplay.GetLabel(status));
            Assert.Equal(BookCopyStatuses.UnderRepair,
                BookCopyStatusDisplay.GetStoredStatus(BookCopyStatusDisplay.DamagedUnderRepair));
        }

        [Fact]
        public void BookCopyService_AddCopy_DelegatesSingleGeneratedCopy()
        {
            var repository = new Mock<BookCopyRepository>();
            var books = new Mock<BookRepository>();
            books.Setup(r => r.GetById(3)).Returns(new Book { BookId = 3, Status = BookStatuses.Active });
            repository.Setup(r => r.AddGeneratedCopies(3, 1, BookCopyStatuses.Available, "Good")).Returns(new[] { 12 });
            var service = new BookCopyService(repository.Object, books.Object);

            Assert.Equal(12, service.AddCopy(3));
            repository.Verify(r => r.AddGeneratedCopies(3, 1, BookCopyStatuses.Available, "Good"), Times.Once);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.AddWithCopies(It.IsAny<Book>(), It.IsAny<int>())).Returns(1);
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
            mockBookRepo.Setup(r => r.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            mockBorrowRepo.Setup(r => r.CountActiveBorrowsByBook(1)).Returns(0);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "New Title", Author = "Author", PublishYear = 2020, Quantity = 5 };

            // Act
            service.UpdateBook(updated);

            // Assert
            mockBookRepo.Verify(r => r.UpdateWithCopies(It.Is<Book>(b => b.Title == "New Title")), Times.Once);
            mockBorrowRepo.Verify(r => r.CountActiveBorrowsByBook(1), Times.Once);
            mockBorrowRepo.Verify(r => r.GetBorrowingRecords(), Times.Never);
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
            mockBorrowRepo.Setup(r => r.CountActiveBorrowsByBook(1)).Returns(3);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Attempting to set Quantity to 2 (which is < borrowing of 3)
            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 2 };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.UpdateBook(updated));
            Assert.Equal("Không thể đặt Quantity nhỏ hơn số đang được mượn (3).", ex.Message);
            mockBorrowRepo.Verify(r => r.CountActiveBorrowsByBook(1), Times.Once);
            mockBorrowRepo.Verify(r => r.GetBorrowingRecords(), Times.Never);
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
            mockBookRepo.Setup(r => r.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            mockBorrowRepo.Setup(r => r.CountActiveBorrowsByBook(1)).Returns(3);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Setting Quantity to exactly 3
            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 3 };

            // Act
            service.UpdateBook(updated);

            // Assert
            Assert.Equal(0, updated.AvailableQuantity); // Available = 3 - 3 = 0
            mockBookRepo.Verify(r => r.UpdateWithCopies(updated), Times.Once);
            mockBorrowRepo.Verify(r => r.CountActiveBorrowsByBook(1), Times.Once);
            mockBorrowRepo.Verify(r => r.GetBorrowingRecords(), Times.Never);
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
            mockBookRepo.Setup(r => r.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            mockBorrowRepo.Setup(r => r.CountActiveBorrowsByBook(1)).Returns(3);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            var updated = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2020, Quantity = 8 };

            // Act
            service.UpdateBook(updated);

            // Assert
            Assert.Equal(5, updated.AvailableQuantity); // 8 - 3 = 5
            mockBorrowRepo.Verify(r => r.CountActiveBorrowsByBook(1), Times.Once);
            mockBorrowRepo.Verify(r => r.GetBorrowingRecords(), Times.Never);
        }

        [Fact]
        public void UpdateBook_UsesScalarActiveLoanCountWithoutLoadingAllBorrowings()
        {
            var bookRepository = new Mock<BookRepository>();
            var borrowRepository = new Mock<BorrowRepository>();
            bookRepository.Setup(repository => repository.GetById(1)).Returns(new Book
            {
                BookId = 1, Quantity = 5, AvailableQuantity = 2
            });
            bookRepository.Setup(repository => repository.UpdateWithCopies(It.IsAny<Book>())).Returns(true);
            borrowRepository.Setup(repository => repository.CountActiveBorrowsByBook(1)).Returns(1);
            var service = new BookService(bookRepository.Object, borrowRepository.Object);
            var book = new Book { BookId = 1, Title = "Title", Author = "Author", PublishYear = 2025, Quantity = 4 };

            service.UpdateBook(book);

            Assert.Equal(3, book.AvailableQuantity);
            borrowRepository.Verify(repository => repository.CountActiveBorrowsByBook(1), Times.Once);
            borrowRepository.Verify(repository => repository.GetBorrowingRecords(), Times.Never);
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
            mockBookRepo.Setup(r => r.UpdateWithCopies(It.IsAny<Book>())).Returns(true);

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
        public void DeleteBook_ArchivesInsteadOfDeleting()
        {
            // Arrange (TC-BOOK-31)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBookRepo.Setup(r => r.SetArchived(1, true)).Returns(true);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act
            service.DeleteBook(1);

            // Assert
            mockBookRepo.Verify(r => r.SetArchived(1, true), Times.Once);
        }

        [Fact]
        public void ArchiveBook_WhenRepositoryRejectsActiveLoan_PropagatesReason()
        {
            // Arrange (TC-BOOK-32)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBookRepo.Setup(r => r.SetArchived(1, true))
                .Throws(new BusinessRuleException("Không thể lưu trữ đầu sách khi còn phiếu mượn đang mở."));

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.ArchiveBook(1));
            Assert.Contains("phiếu mượn đang mở", ex.Message);
        }

        [Fact]
        public void DeleteBook_NonExistentBook_ThrowsBusinessRuleException()
        {
            // Arrange (TC-BOOK-33)
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockBookRepo.Setup(r => r.SetArchived(999, true)).Returns(false);

            var service = new BookService(mockBookRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.DeleteBook(999));
            Assert.Equal("Sách không tồn tại.", ex.Message);
        }

        [Fact]
        public void RestoreBook_UsesStatusUpdateWithoutDeletingCopies()
        {
            var repository = new Mock<BookRepository>();
            repository.Setup(r => r.SetArchived(7, false)).Returns(true);
            new BookService(repository.Object, new Mock<BorrowRepository>().Object).RestoreBook(7);
            repository.Verify(r => r.SetArchived(7, false), Times.Once);
        }

        [Fact]
        public void BooksViewModel_CanSwitchBetweenActiveAndArchivedCatalogs()
        {
            var repository = new Mock<BookRepository>();
            SetupBooksPage(repository,
                new List<Book> { new() { BookId = 1, Status = BookStatuses.Active } },
                new List<Book> { new() { BookId = 2, Status = BookStatuses.Archived } });
            var service = new BookService(repository.Object, new Mock<BorrowRepository>().Object);

            StaHelper.RunInSta(() =>
            {
                var viewModel = new BooksViewModel(service);
                Assert.Equal(1, Assert.Single(viewModel.Books).BookId);
                viewModel.ViewScope = BookStatusFilter.Archived;
                Assert.Equal(2, Assert.Single(viewModel.Books).BookId);
            });
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
            SetupBooksPage(mockBookRepo, bookList, metrics: new BookCatalogMetrics(15, 10, 5));

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

        [Fact]
        public void BooksViewModel_BorrowedStat_UsesCopyCountWhenAvailable()
        {
            var bookRepo = new Mock<BookRepository>();
            SetupBooksPage(bookRepo,
                new List<Book> { new() { BookId = 1, Quantity = 3, AvailableQuantity = 1, BorrowedCopies = 1 } },
                metrics: new BookCatalogMetrics(3, 1, 1));
            var service = new BookService(bookRepo.Object, new Mock<BorrowRepository>().Object);

            StaHelper.RunInSta(() =>
            {
                var viewModel = new BooksViewModel(service);
                Assert.Equal(1, viewModel.TotalBorrowed);
                Assert.Equal(3, viewModel.TotalBooks);
                Assert.Equal(1, viewModel.TotalAvailable);
            });
        }

        private static void SetupBooksPage(Mock<BookRepository> repository, IReadOnlyList<Book> activeBooks,
            IReadOnlyList<Book>? archivedBooks = null, BookCatalogMetrics? metrics = null)
        {
            var allBooks = activeBooks.Concat(archivedBooks ?? Array.Empty<Book>()).ToList();
            repository.Setup(repo => repo.GetCategoryFilterOptions()).Returns(new[]
            {
                new BookFilterOption(BookFilterCodes.All, "All categories"),
                new BookFilterOption(BookFilterCodes.Uncategorized, "Uncategorized"),
                new BookFilterOption("Programming", "Programming")
            });
            repository.Setup(repo => repo.GetPublisherFilterOptions()).Returns(new[]
            {
                new BookFilterOption(BookFilterCodes.All, "All publishers")
            });
            repository.Setup(repo => repo.GetDistinctAuthors()).Returns(allBooks
                .Where(book => !string.IsNullOrWhiteSpace(book.Author))
                .Select(book => book.Author.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(author => author, StringComparer.OrdinalIgnoreCase)
                .ToArray());
            var configuredPrices = allBooks.Where(book => book.ReplacementValue is not null)
                .Select(book => book.ReplacementValue!.Value).ToArray();
            repository.Setup(repo => repo.GetBookPriceRange()).Returns(configuredPrices.Length == 0
                ? new BookPriceRange(null, null)
                : new BookPriceRange(configuredPrices.Min(), configuredPrices.Max()));
            repository.Setup(repo => repo.GetActiveCatalogMetrics()).Returns(metrics ?? new BookCatalogMetrics(0, 0, 0));
            repository.Setup(repo => repo.GetPaged(It.IsAny<BookSearchQuery>())).Returns((BookSearchQuery query) =>
            {
                IEnumerable<Book> source = query.Status switch
                {
                    BookStatusFilter.Archived => archivedBooks ?? Array.Empty<Book>(),
                    BookStatusFilter.All => activeBooks.Concat(archivedBooks ?? Array.Empty<Book>()),
                    _ => activeBooks
                };
                if (!string.IsNullOrWhiteSpace(query.LanguageCode) && query.LanguageCode != LanguageCatalog.AllFilterCode)
                    source = query.LanguageCode == LanguageCatalog.UnknownFilterCode
                        ? source.Where(book => string.IsNullOrWhiteSpace(book.Language))
                        : source.Where(book => string.Equals(book.Language, query.LanguageCode, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(query.Category) && query.Category != BookFilterCodes.All)
                    source = query.Category == BookFilterCodes.Uncategorized
                        ? source.Where(book => string.IsNullOrWhiteSpace(book.Category))
                        : source.Where(book => string.Equals(book.Category, query.Category, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(query.Author))
                    source = source.Where(book => string.Equals(book.Author.Trim(), query.Author, StringComparison.OrdinalIgnoreCase));
                if (query.MinBookPrice is decimal minPrice)
                    source = source.Where(book => book.ReplacementValue is decimal price && price >= minPrice);
                if (query.MaxBookPrice is decimal maxPrice)
                    source = source.Where(book => book.ReplacementValue is decimal price && price <= maxPrice);
                var results = source.ToList();
                int totalPages = results.Count == 0 ? 1 : (int)Math.Ceiling(results.Count / (double)query.PageSize);
                int pageNumber = Math.Min(query.PageNumber, totalPages);
                var items = results.Skip((pageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
                return new PagedResult<Book>(items, results.Count, pageNumber, query.PageSize);
            });
        }
    }
}
