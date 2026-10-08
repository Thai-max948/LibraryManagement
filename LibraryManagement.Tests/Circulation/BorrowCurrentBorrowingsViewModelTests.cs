using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;

namespace LibraryManagement.Tests;

public sealed class BorrowCurrentBorrowingsViewModelTests
{
    [Fact]
    public void InitializationLoadsPageOneWithFiveRowsAndMapsProjection()
    {
        var rows = new List<CurrentBorrowingRow>
        {
            CreateRow(42, "Reader A", "Recent book", new DateTime(2026, 10, 6), new DateTime(2026, 10, 20)),
            CreateRow(12, "Reader B", "Older book", new DateTime(2026, 10, 2), new DateTime(2026, 10, 16))
        };
        var pageRequests = new List<CurrentBorrowingPageQuery>();
        var context = CreateContext(rows, currentPage: query =>
        {
            pageRequests.Add(query);
            return new CurrentBorrowingPage(rows, 1, query.PageSize, 2);
        });

        Assert.Collection(context.ViewModel.CurrentBorrowings,
            row =>
            {
                Assert.Equal("Reader A", row.ReaderName);
                Assert.Equal("Recent book", row.BookTitle);
                Assert.Equal("06/10/2026", row.BorrowDate);
                Assert.Equal("20/10/2026", row.DueDate);
            },
            row =>
            {
                Assert.Equal("Reader B", row.ReaderName);
                Assert.Equal("Older book", row.BookTitle);
                Assert.Equal("02/10/2026", row.BorrowDate);
                Assert.Equal("16/10/2026", row.DueDate);
            });

        var request = Assert.Single(pageRequests);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(5, request.PageSize);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal(5, context.ViewModel.CurrentBorrowingsPageSize);
        Assert.Equal(2, context.ViewModel.CurrentBorrowingsTotalCount);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsTotalPages);
        Assert.Equal("Showing 1-2 of 2", context.ViewModel.ShowingBorrowingsText);
        Assert.Equal("Page 1 / 1", context.ViewModel.CurrentBorrowingsPageText);
        Assert.False(context.ViewModel.CanGoPreviousBorrowings);
        Assert.False(context.ViewModel.CanGoNextBorrowings);
        VerifyNoFullDatasetReads(context);
    }

    [Fact]
    public void DeletedReaderSnapshotAndLegacyLoanWithoutCopyAreDisplayedWithoutLookup()
    {
        var rows = new List<CurrentBorrowingRow>
        {
            new()
            {
                BorrowId = 9,
                ReaderId = 3,
                ReaderName = "Nguyễn Văn A (Đã xóa)",
                BookId = 6,
                BookTitle = "Clean Code",
                BookCopyId = null,
                Barcode = null,
                BorrowDate = new DateTime(2026, 10, 1),
                DueDate = new DateTime(2026, 10, 15)
            }
        };
        var context = CreateContext(rows);

        var row = Assert.Single(context.ViewModel.CurrentBorrowings);

        Assert.Equal("Nguyễn Văn A (Đã xóa)", row.ReaderName);
        Assert.Equal("Clean Code", row.BookTitle);
        context.BorrowRepository.Verify(repository => repository.GetCurrentBorrowingPage(
            It.Is<CurrentBorrowingPageQuery>(query => query.PageNumber == 1 && query.PageSize == 5)), Times.Once);
        VerifyNoFullDatasetReads(context);
    }

    [Fact]
    public void EmptyPageKeepsPageOneAndDisablesNavigation()
    {
        var context = CreateContext(new List<CurrentBorrowingRow>());

        Assert.Empty(context.ViewModel.CurrentBorrowings);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsTotalPages);
        Assert.Equal("Showing 0 of 0", context.ViewModel.ShowingBorrowingsText);
        Assert.False(context.ViewModel.PreviousBorrowingsPageCommand.CanExecute(null));
        Assert.False(context.ViewModel.NextBorrowingsPageCommand.CanExecute(null));
        context.BorrowRepository.Verify(repository => repository.GetCurrentBorrowingPage(
            It.IsAny<CurrentBorrowingPageQuery>()), Times.Once);
    }

    [Fact]
    public void PagingLoadsOnlyCurrentBorrowingsAndShowsCorrectRanges()
    {
        var pageRequests = new List<CurrentBorrowingPageQuery>();
        var pageRows = Enumerable.Range(1, 5)
            .Select(index => CreateRow(index, "Reader", $"Book {index}", new DateTime(2026, 10, 7), new DateTime(2026, 10, 21)))
            .ToList();
        var context = CreateContext(pageRows, currentPage: query =>
        {
            pageRequests.Add(query);
            int count = query.PageNumber == 3 ? 2 : 5;
            return new CurrentBorrowingPage(pageRows.Take(count).ToList(), query.PageNumber, query.PageSize, 12);
        });
        var readerSuggestion = new Reader { ReaderId = 700, FullName = "Existing suggestion" };
        var bookSuggestion = new Book { BookId = 800, Title = "Existing book suggestion" };
        context.ViewModel.RecommendedReaders.Add(readerSuggestion);
        context.ViewModel.RecommendedBooks.Add(bookSuggestion);

        Assert.True(context.ViewModel.NextBorrowingsPageCommand.CanExecute(null));
        context.ViewModel.NextBorrowingsPageCommand.Execute(null);
        Assert.Equal(2, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal("Showing 6-10 of 12", context.ViewModel.ShowingBorrowingsText);
        Assert.True(context.ViewModel.PreviousBorrowingsPageCommand.CanExecute(null));

        context.ViewModel.NextBorrowingsPageCommand.Execute(null);
        Assert.Equal(3, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal("Showing 11-12 of 12", context.ViewModel.ShowingBorrowingsText);
        Assert.False(context.ViewModel.NextBorrowingsPageCommand.CanExecute(null));

        context.ViewModel.NextBorrowingsPageCommand.Execute(null);
        Assert.Equal(3, context.ViewModel.CurrentBorrowingsPageNumber);
        context.ViewModel.PreviousBorrowingsPageCommand.Execute(null);
        Assert.Equal(2, context.ViewModel.CurrentBorrowingsPageNumber);
        context.ViewModel.PreviousBorrowingsPageCommand.Execute(null);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.False(context.ViewModel.PreviousBorrowingsPageCommand.CanExecute(null));

        Assert.Equal(new[] { 1, 2, 3, 2, 1 }, pageRequests.Select(query => query.PageNumber));
        Assert.All(pageRequests, query => Assert.Equal(5, query.PageSize));
        Assert.Same(readerSuggestion, Assert.Single(context.ViewModel.RecommendedReaders));
        Assert.Same(bookSuggestion, Assert.Single(context.ViewModel.RecommendedBooks));
    }

    [Fact]
    public void RepositoryClampingToLastValidPageIsReflectedInViewModel()
    {
        var pageRequests = new List<int>();
        var context = CreateContext(new List<CurrentBorrowingRow>(), currentPage: query =>
        {
            pageRequests.Add(query.PageNumber);
            return query.PageNumber == 3
                ? new CurrentBorrowingPage(new List<CurrentBorrowingRow>(), 2, query.PageSize, 10)
                : new CurrentBorrowingPage(new List<CurrentBorrowingRow>(), query.PageNumber, query.PageSize, 11);
        });

        context.ViewModel.NextBorrowingsPageCommand.Execute(null);
        context.ViewModel.NextBorrowingsPageCommand.Execute(null);

        Assert.Equal(new[] { 1, 2, 3 }, pageRequests);
        Assert.Equal(2, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal(2, context.ViewModel.CurrentBorrowingsTotalPages);
    }

    [Fact]
    public void SuccessfulBorrowResetsToPageOneAndImmediatelyRefreshesProjection()
    {
        bool borrowCompleted = false;
        var pageRequests = new List<int>();
        var pageRows = Enumerable.Range(1, 5)
            .Select(index => CreateRow(index, "Reader", $"Book {index}", new DateTime(2026, 10, 7), new DateTime(2026, 10, 21)))
            .ToList();
        var afterBorrow = new List<CurrentBorrowingRow>
        {
            CreateRow(99, "Reader", "Newest loan", new DateTime(2026, 10, 7), new DateTime(2026, 10, 21))
        };
        var context = CreateContext(pageRows, borrowBooks: (readerId, copyIds) =>
        {
            Assert.Equal(5, readerId);
            Assert.Equal(new[] { 17 }, copyIds);
            borrowCompleted = true;
            return new[] { 99 };
        }, currentPage: query =>
        {
            pageRequests.Add(query.PageNumber);
            if (borrowCompleted && query.PageNumber == 1)
                return new CurrentBorrowingPage(afterBorrow, 1, query.PageSize, 13);

            return new CurrentBorrowingPage(pageRows, query.PageNumber, query.PageSize, 12);
        });

        var reader = new Reader { ReaderId = 5, FullName = "Reader", ReaderType = "Student" };
        context.ReaderRepository.Setup(repository => repository.GetById(5)).Returns(reader);
        context.CopyRepository.Setup(repository => repository.GetAvailableByBookId(6)).Returns(new List<BookCopy>
        {
            new() { CopyId = 17, BookId = 6, Barcode = "BK-000017", Status = BookCopyStatuses.Available }
        });
        context.PolicyRepository.Setup(repository => repository.GetActiveByReaderType("Student"))
            .Returns(new LoanPolicy { ReaderType = "Student", LoanPeriodDays = 14, IsActive = true });
        context.ViewModel.SelectedReader = reader;
        context.ViewModel.SelectedBook = new Book { BookId = 6, Title = "Newest loan", Status = BookStatuses.Active };
        context.ViewModel.AddSelectedCopyCommand.Execute(null);

        context.ViewModel.NextBorrowingsPageCommand.Execute(null);
        context.ViewModel.BorrowCommand.Execute(null);

        Assert.Equal(new[] { 1, 2, 1 }, pageRequests);
        Assert.Equal(1, context.ViewModel.CurrentBorrowingsPageNumber);
        Assert.Equal("Newest loan", Assert.Single(context.ViewModel.CurrentBorrowings).BookTitle);
        context.BorrowRepository.Verify(repository => repository.GetBorrowingRecords(), Times.Never);
        context.BookRepository.Verify(repository => repository.GetAll(), Times.Never);
        context.ReaderRepository.Verify(repository => repository.GetAll(It.IsAny<bool>()), Times.Never);
        context.Dialog.Verify(dialog => dialog.ShowInfo("Mượn thành công 1 sách.", "Thành công"), Times.Once);
    }

    [Fact]
    public void SelectedCopiesRespectRemainingSlotsCanBeRemovedAndClearWhenReaderChanges()
    {
        var context = CreateContext(new List<CurrentBorrowingRow>());
        var firstReader = new Reader { ReaderId = 5, FullName = "Reader A", ReaderType = "Student" };
        var secondReader = new Reader { ReaderId = 6, FullName = "Reader B", ReaderType = "Student" };
        context.ReaderRepository.Setup(repository => repository.GetById(5)).Returns(firstReader);
        context.ReaderRepository.Setup(repository => repository.GetById(6)).Returns(secondReader);
        context.BorrowRepository.Setup(repository => repository.GetEligibilityRecords(5)).Returns(new List<BorrowRecord>
        {
            new() { Status = "Borrowing", DueDate = DateTime.Today.AddDays(10) }
        });
        context.BorrowRepository.Setup(repository => repository.GetEligibilityRecords(6)).Returns(new List<BorrowRecord>());
        context.CopyRepository.Setup(repository => repository.GetAvailableByBookId(6)).Returns(new List<BookCopy>
        {
            new() { CopyId = 17, BookId = 6, Barcode = "BK-000017", Status = BookCopyStatuses.Available },
            new() { CopyId = 18, BookId = 6, Barcode = "BK-000018", Status = BookCopyStatuses.Available },
            new() { CopyId = 19, BookId = 6, Barcode = "BK-000019", Status = BookCopyStatuses.Available }
        });

        context.ViewModel.SelectedReader = firstReader;
        Assert.Equal(1, context.ViewModel.CurrentActiveLoans);
        Assert.Equal(2, context.ViewModel.RemainingBorrowSlots);
        context.ViewModel.SelectedBook = new Book { BookId = 6, Title = "Clean Code", Status = BookStatuses.Active };

        context.ViewModel.SelectedCopy = context.ViewModel.AvailableCopies[0];
        context.ViewModel.AddSelectedCopyCommand.Execute(null);
        context.ViewModel.SelectedCopy = context.ViewModel.AvailableCopies[1];
        context.ViewModel.AddSelectedCopyCommand.Execute(null);

        Assert.Equal(new[] { 17, 18 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.CopyId));
        Assert.Equal(0, context.ViewModel.RemainingSelectionSlots);
        Assert.False(context.ViewModel.AddSelectedCopyCommand.CanExecute(null));
        Assert.True(context.ViewModel.CanConfirmBorrow);
        Assert.Equal("📖 Xác Nhận Mượn 2 Sách", context.ViewModel.ConfirmBorrowText);

        context.ViewModel.RemoveSelectedCopyCommand.Execute(context.ViewModel.SelectedBorrowCopies[0]);
        Assert.Equal(new[] { 18 }, context.ViewModel.SelectedBorrowCopies.Select(copy => copy.CopyId));
        Assert.Equal(1, context.ViewModel.RemainingSelectionSlots);

        context.ViewModel.SelectedReader = secondReader;
        Assert.Empty(context.ViewModel.SelectedBorrowCopies);
        Assert.Equal(3, context.ViewModel.RemainingBorrowSlots);
    }

    private static CurrentBorrowingRow CreateRow(int borrowId, string readerName, string bookTitle,
        DateTime borrowDate, DateTime dueDate) => new()
        {
            BorrowId = borrowId,
            ReaderId = 1,
            ReaderName = readerName,
            BookId = 1,
            BookTitle = bookTitle,
            BookCopyId = 1,
            Barcode = "BK-000001",
            BorrowDate = borrowDate,
            DueDate = dueDate
        };

    private static TestContext CreateContext(List<CurrentBorrowingRow> rows,
        Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? borrowBooks = null,
        Func<CurrentBorrowingPageQuery, CurrentBorrowingPage>? currentPage = null)
    {
        var bookRepository = new Mock<BookRepository>();
        var borrowRepository = new Mock<BorrowRepository>();
        var readerRepository = new Mock<ReaderRepository>();
        var copyRepository = new Mock<BookCopyRepository>();
        var policyRepository = new Mock<LoanPolicyRepository>();
        var dialog = new Mock<IUserDialogService>();

        borrowRepository.Setup(repository => repository.GetCurrentBorrowingPage(
                It.IsAny<CurrentBorrowingPageQuery>()))
            .Returns((CurrentBorrowingPageQuery query) => currentPage?.Invoke(query)
                ?? new CurrentBorrowingPage(rows, query.PageNumber, query.PageSize, rows.Count));
        borrowRepository.Setup(repository => repository.GetEligibilityRecords(It.IsAny<int>()))
            .Returns(new List<BorrowRecord>());
        readerRepository.Setup(repository => repository.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<Reader>());
        readerRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<Reader>(Array.Empty<Reader>(), false));
        bookRepository.Setup(repository => repository.SearchForBorrow(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Array.Empty<BorrowBookSuggestion>());
        bookRepository.Setup(repository => repository.SearchForBorrow(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new BorrowRecommendationPage<BorrowBookSuggestion>(Array.Empty<BorrowBookSuggestion>(), false));
        copyRepository.Setup(repository => repository.GetAvailableByBookId(It.IsAny<int>()))
            .Returns(new List<BookCopy>());
        policyRepository.Setup(repository => repository.GetActiveByReaderType(It.IsAny<string>()))
            .Returns((string readerType) => new LoanPolicy
            {
                ReaderType = readerType,
                LoanPeriodDays = 14,
                IsActive = true
            });

        var borrowService = new BorrowService(bookRepository.Object, borrowRepository.Object,
            readerRepository.Object, copyRepository.Object);
        var viewModel = new BorrowViewModel(dialog.Object, borrowService,
            new BookService(bookRepository.Object, borrowRepository.Object),
            new BookCopyService(copyRepository.Object, bookRepository.Object),
            new ReaderService(readerRepository.Object, borrowRepository.Object),
            new LoanPolicyService(policyRepository.Object), borrowBooks);
        return new TestContext(viewModel, borrowRepository, bookRepository, readerRepository,
            copyRepository, policyRepository, dialog);
    }

    private static void VerifyNoFullDatasetReads(TestContext context)
    {
        context.BorrowRepository.Verify(repository => repository.GetBorrowingRecords(), Times.Never);
        context.BookRepository.Verify(repository => repository.GetAll(), Times.Never);
        context.ReaderRepository.Verify(repository => repository.GetAll(It.IsAny<bool>()), Times.Never);
    }

    private sealed record TestContext(BorrowViewModel ViewModel,
        Mock<BorrowRepository> BorrowRepository,
        Mock<BookRepository> BookRepository,
        Mock<ReaderRepository> ReaderRepository,
        Mock<BookCopyRepository> CopyRepository,
        Mock<LoanPolicyRepository> PolicyRepository,
        Mock<IUserDialogService> Dialog);
}
