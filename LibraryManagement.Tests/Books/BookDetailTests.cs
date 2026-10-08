using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public class BookDetailTests
{
    [Fact]
    public void Detail_LoadsFreshBookAndOnlyItsCopies_WithCountsFromCopyStatuses()
    {
        var books = new Mock<BookRepository>();
        var createdAt = new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddDays(1);
        books.Setup(repository => repository.GetById(10)).Returns(new Book
        {
            BookId = 10,
            Title = "Clean Code",
            Isbn = "9780132350884",
            Publisher = "Prentice Hall",
            Language = "en",
            Quantity = 99,
            ReplacementValue = 42.50m,
            RentalPrice = 2.13m,
            AvailableQuantity = 99,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        });
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetByBookId(10)).Returns(new List<BookCopy>
        {
            new() { BookId = 10, CopyId = 1, Status = BookCopyStatuses.Available },
            new() { BookId = 10, CopyId = 2, Status = BookCopyStatuses.Borrowed },
            new() { BookId = 10, CopyId = 3, Status = BookCopyStatuses.Damaged },
            new() { BookId = 10, CopyId = 4, Status = BookCopyStatuses.UnderRepair },
            new() { BookId = 10, CopyId = 5, Status = BookCopyStatuses.Lost },
            new() { BookId = 10, CopyId = 6, Status = BookCopyStatuses.Retired },
            new() { BookId = 11, CopyId = 7, Status = BookCopyStatuses.Available }
        });

        var detail = Create(10, books, copies);

        Assert.Equal("Clean Code", detail.CurrentBook!.Title);
        Assert.Equal("9780132350884", detail.CurrentBook.Isbn);
        Assert.Equal("Prentice Hall", detail.CurrentBook.Publisher);
        Assert.Equal("en", detail.CurrentBook.Language);
        Assert.Equal("English", detail.CurrentBook.LanguageDisplay);
        Assert.Equal(42.50m, detail.CurrentBook.ReplacementValue);
        Assert.Equal(2.13m, detail.CurrentBook.RentalPrice);
        Assert.Equal(createdAt, detail.CurrentBook.CreatedAt);
        Assert.Equal(updatedAt, detail.CurrentBook.UpdatedAt);
        Assert.Equal(6, detail.Copies.Count);
        Assert.All(detail.Copies, copy => Assert.Equal(10, copy.BookId));
        Assert.Equal(6, detail.Inventory.TotalCopies);
        Assert.Equal(5, detail.Inventory.ActiveCopies);
        Assert.Equal(1, detail.Inventory.Available);
        Assert.Equal(1, detail.Inventory.Borrowed);
        Assert.Equal(2, detail.Inventory.DamagedUnderRepair);
        Assert.Equal(1, detail.Inventory.Lost);
        Assert.Equal(1, detail.Inventory.Retired);
        Assert.True(detail.Inventory.IsBalanced);
    }

    [Fact]
    public void Detail_RefreshReloadsCopiesAndInventoryAfterCopyManagement()
    {
        var books = new Mock<BookRepository>();
        books.Setup(repository => repository.GetById(10)).Returns(new Book { BookId = 10, Status = BookStatuses.Active });
        var physicalCopies = new List<BookCopy>
        {
            new() { BookId = 10, CopyId = 1, Status = BookCopyStatuses.Available }
        };
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetByBookId(10)).Returns(() => physicalCopies.ToList());
        copies.Setup(repository => repository.SetStatus(1, BookCopyStatuses.UnderRepair))
            .Callback(() => physicalCopies[0].Status = BookCopyStatuses.UnderRepair).Returns(true);

        var detail = Create(10, books, copies);
        Assert.Equal(1, detail.Inventory.Available);

        new BookCopyService(copies.Object, books.Object).ChangeStatus(1, BookCopyStatuses.UnderRepair);
        detail.Refresh();

        Assert.Equal(1, detail.Inventory.TotalCopies);
        Assert.Equal(1, detail.Inventory.ActiveCopies);
        Assert.Equal(0, detail.Inventory.Available);
        Assert.Equal(1, detail.Inventory.DamagedUnderRepair);
        Assert.Equal(BookCopyStatuses.UnderRepair, Assert.Single(detail.Copies).Status);
    }

    [Fact]
    public void Detail_ZeroCopyArchivedBook_RemainsViewableButCannotManageCopies()
    {
        var books = new Mock<BookRepository>();
        books.Setup(repository => repository.GetById(10)).Returns(new Book
        {
            BookId = 10,
            Title = "Archived catalog entry",
            Status = BookStatuses.Archived
        });
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetByBookId(10)).Returns(new List<BookCopy>());

        var detail = Create(10, books, copies);

        Assert.True(detail.IsArchived);
        Assert.False(detail.CanManage);
        Assert.True(detail.HasNoCopies);
        Assert.Equal(0, detail.Inventory.TotalCopies);
        Assert.Empty(detail.Copies);
        copies.Verify(repository => repository.GetByBookId(10), Times.Once);
    }

    [Fact]
    public void Detail_ArchiveRestore_RefreshesBookAndKeepsCopiesReadOnly()
    {
        var book = new Book { BookId = 10, Title = "Book" };
        var physicalCopies = new List<BookCopy>
        {
            new() { BookId = 10, CopyId = 1, Status = BookCopyStatuses.Available }
        };
        var books = new Mock<BookRepository>();
        books.Setup(repository => repository.GetById(10)).Returns(() => book);
        books.Setup(repository => repository.SetArchived(10, true)).Callback(() => book.Status = BookStatuses.Archived).Returns(true);
        books.Setup(repository => repository.SetArchived(10, false)).Callback(() => book.Status = BookStatuses.Active).Returns(true);
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetByBookId(10)).Returns(() => physicalCopies.ToList());

        var detail = Create(10, books, copies);
        Assert.Equal(1, detail.Inventory.Available);

        detail.Archive();
        Assert.True(detail.IsArchived);
        Assert.False(detail.CanManage);
        detail.Restore();
        Assert.True(detail.CanManage);
        Assert.Equal(1, detail.Inventory.Available);
        Assert.Equal(BookCopyStatuses.Available, Assert.Single(detail.Copies).Status);
        copies.Verify(repository => repository.SetStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Detail_MissingBookId_IsHandledWithoutLoadingCopies()
    {
        var books = new Mock<BookRepository>();
        var copies = new Mock<BookCopyRepository>();

        Assert.Throws<BusinessRuleException>(() => Create(99, books, copies));
        copies.Verify(repository => repository.GetByBookId(It.IsAny<int>()), Times.Never);
    }

    private static BookDetailViewModel Create(int bookId, Mock<BookRepository> books, Mock<BookCopyRepository> copies)
        => new(bookId, new BookService(books.Object, new Mock<BorrowRepository>().Object),
            new BookCopyService(copies.Object, books.Object));
}
