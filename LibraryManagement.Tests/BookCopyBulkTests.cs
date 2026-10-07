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

    [Theory]
    [InlineData(BookCopyStatuses.Available, true)]
    [InlineData(BookCopyStatuses.Damaged, true)]
    [InlineData(BookCopyStatuses.UnderRepair, true)]
    [InlineData(BookCopyStatuses.Lost, true)]
    [InlineData(BookCopyStatuses.Borrowed, false)]
    [InlineData(BookCopyStatuses.Retired, false)]
    public void ManualTransitions_ExposeRetirementOnlyWhenRulesAllow(string currentStatus, bool canRetire)
    {
        var transitions = BookCopyService.GetManualTransitions(currentStatus);

        Assert.Equal(canRetire, transitions.Contains(BookCopyStatuses.Retired));
        Assert.DoesNotContain(BookCopyStatuses.Borrowed, transitions);
        Assert.DoesNotContain(currentStatus, transitions);
        Assert.All(transitions, nextStatus => Assert.True(BookCopyStatusRules.CanChangeManually(currentStatus, nextStatus)));
    }

    [Theory]
    [InlineData(BookCopyStatuses.Damaged)]
    [InlineData(BookCopyStatuses.UnderRepair)]
    [InlineData(BookCopyStatuses.Lost)]
    public void MarkAvailable_RecoversEligibleCopy(string currentStatus)
    {
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetById(7)).Returns(new BookCopy { CopyId = 7, Status = currentStatus });
        copies.Setup(repository => repository.SetStatus(7, BookCopyStatuses.Available)).Returns(true);

        new BookCopyService(copies.Object).MarkAvailable(7);

        copies.Verify(repository => repository.GetById(7), Times.Once);
        copies.Verify(repository => repository.SetStatus(7, BookCopyStatuses.Available), Times.Once);
    }

    [Theory]
    [InlineData(BookCopyStatuses.Available, "Only lost or repair-needed copies can be marked available.")]
    [InlineData(BookCopyStatuses.Borrowed, "This copy is currently borrowed and must be returned through the Return workflow.")]
    [InlineData(BookCopyStatuses.Retired, "Retired copies cannot be returned to circulation.")]
    public void MarkAvailable_RejectsIneligibleCopyWithoutMutation(string currentStatus, string expectedMessage)
    {
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetById(7)).Returns(new BookCopy { CopyId = 7, Status = currentStatus });
        var service = new BookCopyService(copies.Object);

        var exception = Assert.Throws<BusinessRuleException>(() => service.MarkAvailable(7));

        Assert.Equal(expectedMessage, exception.Message);
        copies.Verify(repository => repository.SetStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(BookCopyStatuses.Available)]
    [InlineData(BookCopyStatuses.Damaged)]
    [InlineData(BookCopyStatuses.UnderRepair)]
    [InlineData(BookCopyStatuses.Lost)]
    public void RetireCopy_UsesExistingAllowedTransition(string currentStatus)
    {
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetById(7)).Returns(new BookCopy { CopyId = 7, Status = currentStatus });
        copies.Setup(repository => repository.SetStatus(7, BookCopyStatuses.Retired)).Returns(true);

        new BookCopyService(copies.Object).RetireCopy(7);

        copies.Verify(repository => repository.SetStatus(7, BookCopyStatuses.Retired), Times.Once);
    }

    [Theory]
    [InlineData(BookCopyStatuses.Borrowed)]
    [InlineData(BookCopyStatuses.Retired)]
    public void RetireCopy_RejectsBorrowedAndRetiredCopies(string currentStatus)
    {
        var copies = new Mock<BookCopyRepository>();
        copies.Setup(repository => repository.GetById(7)).Returns(new BookCopy { CopyId = 7, Status = currentStatus });

        Assert.Throws<BusinessRuleException>(() => new BookCopyService(copies.Object).RetireCopy(7));

        copies.Verify(repository => repository.SetStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }
}
