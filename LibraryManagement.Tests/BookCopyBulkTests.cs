using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public class BookCopyBulkTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 3 ", 3)]
    [InlineData("100", 100)]
    public void QuantityText_ParsesValidIntegers(string text, int expected)
        => Assert.Equal(expected, BookCopyService.ParseQuantity(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    public void QuantityText_RejectsInvalidInput(string? text)
    {
        var exception = Assert.Throws<BusinessRuleException>(() => BookCopyService.ParseQuantity(text));
        Assert.Equal("Số lượng bản sách phải là số nguyên từ 1 đến 100.", exception.Message);
    }

    [Theory]
    [InlineData(1, "BK-000001")]
    [InlineData(53, "BK-000053")]
    [InlineData(1234, "BK-001234")]
    [InlineData(999999, "BK-999999")]
    [InlineData(1000000, "BK-1000000")]
    public void Barcode_UsesActualCopyId(int copyId, string expected)
        => Assert.Equal(expected, BookCopyBarcode.Format(copyId));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Barcode_RejectsNonPersistedCopyIds(int copyId)
        => Assert.Throws<ArgumentOutOfRangeException>(() => BookCopyBarcode.Format(copyId));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AddCopies_DelegatesOneAtomicBatch(int quantity)
    {
        var books = new Mock<BookRepository>();
        books.Setup(repository => repository.GetById(7)).Returns(new Book { BookId = 7, Status = BookStatuses.Active });
        var copies = new Mock<BookCopyRepository>();
        int[] ids = Enumerable.Range(53, quantity).ToArray();
        copies.Setup(repository => repository.AddGeneratedCopies(7, quantity, BookCopyStatuses.Available, "Good")).Returns(ids);

        var result = new BookCopyService(copies.Object, books.Object).AddCopies(7, quantity);

        Assert.Equal(ids, result);
        Assert.Equal(quantity, result.Distinct().Count());
        copies.Verify(repository => repository.AddGeneratedCopies(7, quantity, BookCopyStatuses.Available, "Good"), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void AddCopies_RejectsInvalidQuantityBeforeDatabase(int quantity)
    {
        var books = new Mock<BookRepository>();
        var copies = new Mock<BookCopyRepository>();
        var service = new BookCopyService(copies.Object, books.Object);

        Assert.Throws<BusinessRuleException>(() => service.AddCopies(7, quantity));
        books.Verify(repository => repository.GetById(It.IsAny<int>()), Times.Never);
        copies.Verify(repository => repository.AddGeneratedCopies(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void AddCopies_RejectsMissingBook()
    {
        var books = new Mock<BookRepository>();
        var copies = new Mock<BookCopyRepository>();

        var exception = Assert.Throws<BusinessRuleException>(() => new BookCopyService(copies.Object, books.Object).AddCopies(7, 1));
        Assert.Equal("Sách không tồn tại.", exception.Message);
        copies.Verify(repository => repository.AddGeneratedCopies(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void AddCopies_RejectsArchivedBook()
    {
        var books = new Mock<BookRepository>();
        books.Setup(repository => repository.GetById(7)).Returns(new Book { BookId = 7, Status = BookStatuses.Archived });
        var copies = new Mock<BookCopyRepository>();

        var exception = Assert.Throws<BusinessRuleException>(() => new BookCopyService(copies.Object, books.Object).AddCopies(7, 3));
        Assert.Equal("Không thể thêm bản sách cho đầu sách đã được archive.", exception.Message);
        copies.Verify(repository => repository.AddGeneratedCopies(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
