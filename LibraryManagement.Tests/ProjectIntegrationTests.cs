using System;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ProjectIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void CirculationAudit_RecordsBorrowReturnAndLostWithAuthenticatedActor()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Audit event reader", StudentId = $"AUDIT-EVENT-{Guid.NewGuid():N}", Phone = "0901234567"
        });
        int bookId = books.AddBook(new Book
        {
            Title = "Circulation audit", Author = "Author", PublishYear = 2026, Quantity = 0, ReplacementValue = 300000m
        });
        int copyId = copies.AddCopy(bookId);
        var service = new BorrowService();

        int borrowId = service.BorrowBook(readerId, copyId);
        service.ReturnBook(borrowId, ReturnCondition.Damaged, "Rách bìa");
        var events = service.GetAuditEvents(new[] { borrowId });

        Assert.Collection(events,
            created =>
            {
                Assert.Equal(CirculationAuditEventType.BorrowCreated, created.EventType);
                Assert.Equal(copyId, created.BookCopyId);
                Assert.Equal(AuthService.CurrentUser!.Id, created.ActorUserId);
                Assert.Equal(AuthService.CurrentUser.FullName, created.ActorNameSnapshot);
            },
            returned =>
            {
                Assert.Equal(CirculationAuditEventType.ReturnedDamaged, returned.EventType);
                Assert.Equal(copyId, returned.BookCopyId);
                Assert.Equal("Rách bìa", returned.Note);
                Assert.Equal(AuthService.CurrentUser!.Id, returned.ActorUserId);
            });
        Assert.Equal(BookCopyStatuses.Damaged, Assert.Single(copies.GetCopies(bookId)).Status);

        int lostCopyId = copies.AddCopy(bookId);
        int lostReaderId = new ReaderService().AddReader(new Reader
        {
            FullName = "Audit lost reader", StudentId = $"AUDIT-LOST-{Guid.NewGuid():N}", Phone = "0901234568"
        });
        int lostBorrowId = service.BorrowBook(lostReaderId, lostCopyId);
        service.MarkAsLost(lostBorrowId, "Không tìm thấy");
        var lostEvent = Assert.Single(service.GetAuditEvents(new[] { lostBorrowId }), e => e.EventType == CirculationAuditEventType.MarkedLost);
        Assert.Equal(CirculationAuditEventType.MarkedLost, lostEvent.EventType);
        Assert.Equal("Không tìm thấy", lostEvent.Note);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BookAudit_TimestampsTrackMetadataWithoutInventoryOrLifecycleChanges()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int bookId = books.AddBook(new Book
        {
            Title = "Audit timestamps", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        var created = books.GetBookById(bookId)!;
        Assert.NotNull(created.CreatedAt);
        Assert.Null(created.UpdatedAt);
        Assert.InRange(created.CreatedAt!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

        int copyId = copies.AddCopy(bookId);
        var copy = Assert.Single(copies.GetCopies(bookId));
        Assert.NotEqual(default, copy.CreatedAt);
        Assert.Null(books.GetBookById(bookId)!.UpdatedAt);

        var toEdit = books.GetBookById(bookId)!;
        toEdit.Title = "Audit timestamps edited";
        books.UpdateBook(toEdit);
        var edited = books.GetBookById(bookId)!;
        Assert.Equal(created.CreatedAt, edited.CreatedAt);
        Assert.NotNull(edited.UpdatedAt);
        Assert.InRange(edited.UpdatedAt!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

        copies.ChangeStatus(copyId, BookCopyStatuses.Retired);
        Assert.Equal(edited.UpdatedAt, books.GetBookById(bookId)!.UpdatedAt);
        books.ArchiveBook(bookId);
        var archived = books.GetBookById(bookId)!;
        Assert.Equal(created.CreatedAt, archived.CreatedAt);
        Assert.Equal(edited.UpdatedAt, archived.UpdatedAt);
        Assert.NotNull(archived.ArchivedAt);
        books.RestoreBook(bookId);
        var restored = books.GetBookById(bookId)!;
        Assert.Equal(created.CreatedAt, restored.CreatedAt);
        Assert.Equal(edited.UpdatedAt, restored.UpdatedAt);
        Assert.Null(restored.ArchivedAt);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Archive_ReportsCurrentCopyBlockersAndPreservesTheirStatuses()
    {
        var books = new BookService();
        var copies = new BookCopyService();
        int bookId = books.AddBook(new Book
        {
            Title = "Archive blockers", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        int available = copies.AddCopy(bookId);
        int repair = copies.AddCopy(bookId);
        copies.ChangeStatus(repair, BookCopyStatuses.UnderRepair);

        var error = Assert.Throws<BusinessRuleException>(() => books.ArchiveBook(bookId));
        Assert.Contains("Available: 1", error.Message);
        Assert.Contains("UnderRepair: 1", error.Message);
        Assert.Equal(BookStatuses.Active, books.GetBookById(bookId)!.Status);
        Assert.Equal(BookCopyStatuses.Available, copies.GetCopies(bookId).Single(copy => copy.CopyId == available).Status);
        Assert.Equal(BookCopyStatuses.UnderRepair, copies.GetCopies(bookId).Single(copy => copy.CopyId == repair).Status);

        copies.ChangeStatus(available, BookCopyStatuses.Retired);
        copies.ChangeStatus(repair, BookCopyStatuses.Retired);
        books.ArchiveBook(bookId);
        Assert.NotNull(books.GetBookById(bookId)!.ArchivedAt);
        Assert.All(copies.GetCopies(bookId), copy => Assert.Equal(BookCopyStatuses.Retired, copy.Status));
        books.RestoreBook(bookId);
        var restored = books.GetBookById(bookId)!;
        Assert.Null(restored.ArchivedAt);
        Assert.All(copies.GetCopies(bookId), copy => Assert.Equal(BookCopyStatuses.Retired, copy.Status));
        int newCopy = copies.AddCopy(bookId);
        Assert.Equal(BookCopyStatuses.Available, copies.GetCopies(bookId).Single(copy => copy.CopyId == newCopy).Status);
        Assert.Equal(2, copies.GetCopies(bookId).Count(copy => copy.Status == BookCopyStatuses.Retired));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BookMetadata_RoundTripsOptionalValuesAndUpdates()
    {
        var service = new BookService();
        int bookId = service.AddBook(new Book
        {
            Title = "Metadata optional", Author = "Author", PublishYear = 2026, Quantity = 0,
            Publisher = "  ", Language = null, ReplacementValue = 1250.50m, RentalPrice = 10.25m
        });
        var stored = service.GetBookById(bookId)!;
        Assert.Null(stored.Publisher);
        Assert.Null(stored.Language);
        Assert.Equal(1250.50m, stored.ReplacementValue);
        Assert.Equal(62.53m, stored.RentalPrice);
        Assert.Equal("Unknown", stored.LanguageDisplay);

        stored.Publisher = " Nhà xuất bản Trẻ ";
        stored.Language = " JA ";
        stored.ReplacementValue = 1400m;
        stored.RentalPrice = 12.50m;
        service.UpdateBook(stored);
        var updated = service.GetBookById(bookId)!;
        Assert.Equal("Nhà xuất bản Trẻ", updated.Publisher);
        Assert.Equal("ja", updated.Language);
        Assert.Equal(1400m, updated.ReplacementValue);
        Assert.Equal(70m, updated.RentalPrice);
        Assert.Equal("Japanese", updated.LanguageDisplay);
        var listed = service.GetAllBooks().Single(book => book.BookId == bookId);
        Assert.Equal(1400m, listed.ReplacementValue);
        Assert.Equal(70m, listed.RentalPrice);
        Assert.Contains(service.SearchBook("Nhà xuất bản"), book => book.BookId == bookId);
        Assert.Empty(new BookCopyService().GetCopies(bookId));

        service.ArchiveBook(bookId);
        var archived = service.GetArchivedBooks().Single(book => book.BookId == bookId);
        Assert.Equal(1400m, archived.ReplacementValue);
        Assert.Equal(70m, archived.RentalPrice);
        service.RestoreBook(bookId);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BookDetail_ReadsCopyManagementChangesAndSurvivesArchiveRestore()
    {
        var books = new BookService();
        int bookId = books.AddBook(new Book
        {
            Title = "Detail integration", Author = "Author", Publisher = " O'Reilly & Nhà xuất bản Trẻ ",
            Language = " VI ", PublishYear = 2026, Quantity = 0
        });
        int otherBookId = books.AddBook(new Book
        {
            Title = "Other detail integration", Author = "Author", PublishYear = 2026, Quantity = 1
        });
        var detail = new BookDetailViewModel(bookId);
        Assert.Equal("O'Reilly & Nhà xuất bản Trẻ", detail.CurrentBook!.Publisher);
        Assert.Equal("vi", detail.CurrentBook.Language);
        Assert.Equal("Vietnamese", detail.CurrentBook.LanguageDisplay);
        Assert.Contains(books.SearchBook("O'Reilly"), book => book.BookId == bookId);
        Assert.True(detail.HasNoCopies);
        Assert.Equal(0, detail.Inventory.TotalCopies);

        var copies = new BookCopyService();
        copies.AddCopies(bookId, 1);
        detail.Refresh();
        Assert.Single(detail.Copies);
        Assert.Equal(1, detail.Inventory.Available);
        Assert.All(detail.Copies, copy => Assert.Equal(bookId, copy.BookId));
        Assert.DoesNotContain(detail.Copies, copy => copy.BookId == otherBookId);
        copies.ChangeStatus(Assert.Single(detail.Copies).CopyId, BookCopyStatuses.Retired);
        detail.Refresh();
        Assert.Equal(1, detail.Inventory.Retired);
        Assert.Equal(0, detail.Inventory.ActiveCopies);

        detail.Archive();
        Assert.True(detail.IsArchived);
        Assert.Equal("vi", detail.CurrentBook!.Language);
        Assert.Equal("O'Reilly & Nhà xuất bản Trẻ", detail.CurrentBook.Publisher);
        Assert.Throws<BusinessRuleException>(() => copies.AddCopies(bookId, 1));
        detail.Restore();
        Assert.Equal(BookCopyStatuses.Retired, Assert.Single(detail.Copies).Status);
        Assert.True(detail.CanManage);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ArchiveAndRestore_PreserveCopiesHistoryAndCatalogRules()
    {
        var service = new BookService();
        var copies = new BookCopyService();
        var circulation = new BorrowService();
        int emptyBookId = service.AddBook(new Book
        {
            Title = "Empty archive test", Author = "Author", PublishYear = 2026, Quantity = 0
        });
        service.ArchiveBook(emptyBookId);
        Assert.Equal(BookStatuses.Archived, new BookRepository().GetById(emptyBookId)!.Status);
        Assert.NotNull(new BookRepository().GetById(emptyBookId)!.ArchivedAt);
        Assert.DoesNotContain(service.GetAllBooks(), book => book.BookId == emptyBookId);
        Assert.DoesNotContain(service.SearchBook("Empty archive test"), book => book.BookId == emptyBookId);
        Assert.Contains(service.GetArchivedBooks(), book => book.BookId == emptyBookId);
        Assert.Throws<BusinessRuleException>(() => copies.AddCopy(emptyBookId));
        service.RestoreBook(emptyBookId);
        Assert.Null(new BookRepository().GetById(emptyBookId)!.ArchivedAt);

        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Archive reader", StudentId = $"ARCH-{Guid.NewGuid():N}", Phone = "0901234567"
        });
        int bookId = service.AddBook(new Book
        {
            Title = "Archive flow test", Author = "Author", PublishYear = 2026, Quantity = 1
        });
        int copyId = Assert.Single(copies.GetCopies(bookId)).CopyId;
        Assert.Throws<BusinessRuleException>(() => service.ArchiveBook(bookId));
        int loanId = circulation.BorrowBook(readerId, CopyIdFor(bookId));
        Assert.Throws<BusinessRuleException>(() => service.ArchiveBook(bookId));
        Assert.Throws<BusinessRuleException>(() => copies.ChangeStatus(copyId, BookCopyStatuses.Retired));
        circulation.ReturnBook(loanId, ReturnCondition.Damaged, "Sách bị hỏng");
        Assert.Throws<BusinessRuleException>(() => service.ArchiveBook(bookId));
        copies.ChangeStatus(copyId, BookCopyStatuses.Retired);
        service.ArchiveBook(bookId);

        var archived = new BookRepository().GetById(bookId)!;
        Assert.Equal(BookStatuses.Archived, archived.Status);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(BookCopyStatuses.Retired, Assert.Single(copies.GetCopies(bookId)).Status);
        Assert.Throws<BusinessRuleException>(() => copies.ChangeStatus(copyId, BookCopyStatuses.Available));
        Assert.Throws<BusinessRuleException>(() => copies.ChangeStatus(copyId, BookCopyStatuses.Retired));
        Assert.Equal(BookCopyStatuses.Retired, Assert.Single(copies.GetCopies(bookId)).Status);

        int lostBookId = service.AddBook(new Book
        {
            Title = "Lost archive test", Author = "Author", PublishYear = 2026, Quantity = 1
        });
        int lostCopyId = Assert.Single(copies.GetCopies(lostBookId)).CopyId;
        copies.ChangeStatus(lostCopyId, BookCopyStatuses.Lost);
        Assert.Throws<BusinessRuleException>(() => service.ArchiveBook(lostBookId));
        copies.ChangeStatus(lostCopyId, BookCopyStatuses.Retired);
        service.ArchiveBook(lostBookId);
        Assert.Throws<BusinessRuleException>(() => circulation.BorrowBook(readerId, CopyIdFor(bookId)));
        Assert.False(circulation.CanBorrow(readerId, bookId, out _));
        Assert.Contains(circulation.GetHistory(readerId: readerId), record => record.BorrowId == loanId);
        service.RestoreBook(bookId);
        Assert.Equal(BookStatuses.Active, new BookRepository().GetById(bookId)!.Status);
        Assert.Null(new BookRepository().GetById(bookId)!.ArchivedAt);
        Assert.Equal(BookCopyStatuses.Retired, Assert.Single(copies.GetCopies(bookId)).Status);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Isbn_CreatesSearchesAndRejectsDuplicateEdition()
    {
        var service = new BookService();
        int bookId = service.AddBook(new Book { Title = "ISBN test", Author = "Author", PublishYear = 2026,
            Quantity = 1, Isbn = "0-13-235088-2" });
        Assert.Equal("9780132350884", new BookRepository().GetById(bookId)!.Isbn);
        Assert.Equal(bookId, Assert.Single(service.SearchBook("978-0-13-235088-4")).BookId);
        Assert.Equal(bookId, Assert.Single(service.SearchBook("9780132350884")).BookId);
        var edition = new BookRepository().GetById(bookId)!;
        edition.Title = "ISBN test updated";
        service.UpdateBook(edition);
        Assert.Equal("9780132350884", new BookRepository().GetById(bookId)!.Isbn);

        var duplicate = Assert.Throws<DuplicateBookIsbnException>(() => service.AddBook(new Book
        {
            Title = "Duplicate edition", Author = "Author", PublishYear = 2026,
            Quantity = 1, Isbn = "9780132350884"
        }));
        Assert.Equal(bookId, duplicate.ExistingBookId);

        using var connection = LibraryManagement.Data.Database.GetConnection();
        connection.Open();
        using var command = new Microsoft.Data.SqlClient.SqlCommand(@"
            INSERT INTO Books (Title, Author, PublishYear, Quantity, AvailableQuantity, ISBN)
            VALUES ('SQL duplicate', 'Author', 2026, 0, 0, @ISBN)", connection);
        command.Parameters.AddWithValue("@ISBN", "9780132350884");
        Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() => command.ExecuteNonQuery());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void LegacyInventoryBackfill_LeavesOpenLoansUnlinkedForBarcodeReview()
    {
        var copies = new BookCopyService().GetCopies(1);
        Assert.Equal(11, copies.Count);
        var activeLoan = Assert.Single(new BorrowRepository().GetBorrowingRecords(), record => record.BookId == 1);
        Assert.Null(activeLoan.BookCopyId);
        Assert.Equal(3, copies.Count(copy => copy.Status == BookCopyStatuses.UnderRepair && copy.Condition == "LegacyUnverified"));
        Assert.Equal(copies.Count(copy => copy.Status == BookCopyStatuses.Available), new BookRepository().GetById(1)!.AvailableQuantity);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ReaderAndBook_CreateEditSearchAndReloadFromSql()
    {
        var readers = new ReaderService();
        var books = new BookService();
        var reader = new Reader
        {
            ReaderType = "External", FullName = "Độc giả tích hợp", IdentityNumber = "001201007788",
            Phone = "0912345678", Email = "", Address = "Hà Nội"
        };
        int readerId = readers.AddReader(reader);
        Assert.Equal(readerId, reader.ReaderId);
        Assert.Equal(readerId, Assert.Single(readers.SearchReader(reader.FormattedId, "External", "Active")).ReaderId);

        reader.FullName = "Độc giả đã sửa";
        reader.Status = "Suspended";
        reader.SuspensionReason = "Vi phạm quy định mượn sách";
        readers.UpdateReader(reader);
        var persistedReader = readers.GetReaderById(readerId)!;
        Assert.Equal("Độc giả đã sửa", persistedReader.FullName);
        Assert.Equal("Suspended", persistedReader.Status);
        Assert.Equal("Vi phạm quy định mượn sách", persistedReader.SuspensionReason);
        Assert.NotNull(persistedReader.SuspendedDate);
        Assert.Equal("*********788", persistedReader.DisplayIdentification);
        Assert.Empty(readers.SearchReader(reader.FormattedId, "External", "Active"));

        var book = new Book { Title = "Sách tích hợp", Author = "Tác giả", Category = "Test", PublishYear = 2026, Quantity = 4 };
        int bookId = books.AddBook(book);
        Assert.Equal(4, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(4, new BookCopyService().GetCopies(bookId).Count);
        Assert.All(new BookCopyService().GetCopies(bookId), copy => Assert.Equal(BookCopyStatuses.Available, copy.Status));
        var copyManager = new BookCopyService();
        int manuallyAddedCopyId = copyManager.AddCopy(bookId);
        Assert.Equal(5, new BookRepository().GetById(bookId)!.Quantity);
        copyManager.ChangeStatus(manuallyAddedCopyId, BookCopyStatuses.Damaged);
        Assert.Equal(4, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(BookCopyStatuses.Damaged, Assert.Single(copyManager.GetCopies(bookId), c => c.CopyId == manuallyAddedCopyId).Status);
        book.BookId = bookId;
        book.Quantity = 6;
        books.UpdateBook(book);
        Assert.Equal(5, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Contains(books.SearchBook("Sách tích hợp"), b => b.BookId == bookId);

        book.Quantity = 5;
        books.UpdateBook(book);
        Assert.Equal(5, new BookCopyService().GetCopies(bookId).Count(copy => copy.Status != BookCopyStatuses.Retired));
        Assert.Equal(4, new BookRepository().GetById(bookId)!.AvailableQuantity);

        book.Quantity = 3;
        books.UpdateBook(book);
        Assert.Equal(3, new BookCopyService().GetCopies(bookId).Count(copy => copy.Status != BookCopyStatuses.Retired));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);

        var duplicate = new Reader { ReaderType = "External", FullName = "Trùng định danh",
            IdentityNumber = reader.IdentityNumber, Phone = "0987654321" };
        Assert.Throws<BusinessRuleException>(() => readers.AddReader(duplicate));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void BorrowAndReturn_PersistsHistoryAndKeepsInventoryConsistent()
    {
        var reader = new Reader { FullName = "Người mượn tích hợp", StudentId = "SV-INTEGRATION-1", Phone = "0901234567" };
        var readers = new ReaderService();
        int readerId = readers.AddReader(reader);
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Sách mượn tích hợp", Author = "Tác giả", PublishYear = 2026, Quantity = 2
        });
        var circulation = new BorrowService();
        readers.ToggleStatus(readerId);
        Assert.Throws<BusinessRuleException>(() =>
            circulation.BorrowBook(readerId, CopyIdFor(bookId)));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
        readers.ToggleStatus(readerId);
        int borrowId = circulation.BorrowBook(readerId, CopyIdFor(bookId));

        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        var borrowed = Assert.Single(new BookCopyService().GetCopies(bookId), copy => copy.Status == BookCopyStatuses.Borrowed);
        Assert.Equal(borrowed.CopyId, Assert.Single(circulation.GetBorrowingBooks(), r => r.BorrowId == borrowId).CopyId);
        Assert.Contains(circulation.GetBorrowingBooks(), r => r.BorrowId == borrowId && r.ReaderId == readerId);
        Assert.Throws<BusinessRuleException>(() => new ReaderService().DeleteReader(readerId));

        using (var connection = LibraryManagement.Data.Database.GetConnection())
        {
            connection.Open();
            using var corruptCache = new Microsoft.Data.SqlClient.SqlCommand(
                "UPDATE Books SET Quantity = 0, AvailableQuantity = 0 WHERE BookId = @BookId", connection);
            corruptCache.Parameters.AddWithValue("@BookId", bookId);
            corruptCache.ExecuteNonQuery();
            using var duplicateLoan = new Microsoft.Data.SqlClient.SqlCommand(@"
                INSERT INTO BorrowRecords (BookId, CopyId, ReaderId, BorrowDate, DueDate, Status)
                SELECT BookId, CopyId, ReaderId, BorrowDate, DueDate, 'Borrowing'
                FROM BorrowRecords WHERE BorrowId = @BorrowId", connection);
            duplicateLoan.Parameters.AddWithValue("@BorrowId", borrowId);
            Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() => duplicateLoan.ExecuteNonQuery());
        }
        Assert.Equal(2, new BookRepository().GetById(bookId)!.Quantity);
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(1, Assert.Single(new BookRepository().Search("Sách mượn tích hợp"), b => b.BookId == bookId).AvailableQuantity);
        Assert.True(circulation.CanBorrow(readerId, bookId, out _));

        circulation.ReturnBook(borrowId, ReturnCondition.Normal);
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
        using (var connection = LibraryManagement.Data.Database.GetConnection())
        {
            connection.Open();
            using var check = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT Quantity, AvailableQuantity FROM Books WHERE BookId = @BookId", connection);
            check.Parameters.AddWithValue("@BookId", bookId);
            using var inventory = check.ExecuteReader();
            Assert.True(inventory.Read());
            // Circulation treats BookCopies as inventory authority and never repairs these legacy counters.
            Assert.Equal(0, inventory.GetInt32(0));
            Assert.Equal(0, inventory.GetInt32(1));
        }
        Assert.Equal(BookCopyStatuses.Available, new BookCopyService().GetCopies(bookId).Single(copy => copy.CopyId == borrowed.CopyId).Status);
        var record = Assert.Single(circulation.GetHistory(readerId: readerId, status: "Returned"),
            r => r.BorrowId == borrowId);
        Assert.NotNull(record.ReturnDate);
        Assert.Equal(ReturnCondition.Normal.ToString(), record.ReturnCondition);
        Assert.Throws<BusinessRuleException>(() => new BookService().DeleteBook(bookId));
        Assert.Throws<BusinessRuleException>(() => circulation.ReturnBook(borrowId, ReturnCondition.Normal));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void DamagedReturn_RepairAndLostReturn_KeepInventoryConsistent()
    {
        var readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Người mượn trạng thái", StudentId = $"SV-{Guid.NewGuid():N}", Phone = "0901234567"
        });
        var bookId = new BookService().AddBook(new Book
        {
            Title = "Sách kiểm tra trạng thái", Author = "Tác giả", PublishYear = 2026, Quantity = 1,
            ReplacementValue = 300000m
        });
        var circulation = new BorrowService();
        var copies = new BookCopyService();
        var copyId = Assert.Single(copies.GetCopies(bookId)).CopyId;

        var damagedLoan = circulation.BorrowBook(readerId, copyId);
        circulation.ReturnBook(damagedLoan, ReturnCondition.Damaged, "Bìa bị hỏng");
        Assert.Equal(0, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(BookCopyStatuses.Damaged, Assert.Single(copies.GetCopies(bookId)).Status);
        copies.ChangeStatus(copyId, BookCopyStatuses.UnderRepair);
        copies.ChangeStatus(copyId, BookCopyStatuses.Available);
        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);

        var lostReaderId = new ReaderService().AddReader(new Reader
        {
            FullName = "Người báo mất", StudentId = $"SV-LOST-{Guid.NewGuid():N}", Phone = "0901234568"
        });
        var lostLoan = circulation.BorrowBook(lostReaderId, copyId);
        circulation.MarkAsLost(lostLoan, "Độc giả báo thất lạc");
        Assert.Equal(0, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Equal(BookCopyStatuses.Lost, Assert.Single(copies.GetCopies(bookId)).Status);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ReaderEligibility_OverdueLoanBlocksConfirmAndRefreshesAfterReturn()
    {
        var readers = new ReaderService();
        var circulation = new BorrowService();
        int readerId = readers.AddReader(new Reader
        {
            FullName = "Độc giả kiểm tra eligibility",
            StudentId = "SV-ELIGIBILITY-1",
            Phone = "0909876543"
        });
        int overdueBookId = new BookService().AddBook(new Book
        {
            Title = "Sách quá hạn tích hợp", Author = "Tác giả", PublishYear = 2026, Quantity = 1
        });
        int nextBookId = new BookService().AddBook(new Book
        {
            Title = "Sách mượn tiếp", Author = "Tác giả", PublishYear = 2026, Quantity = 1
        });

        int overdueBorrowId = circulation.BorrowBook(readerId, CopyIdFor(overdueBookId));
        using (var connection = LibraryManagement.Data.Database.GetConnection())
        {
            connection.Open();
            using var command = new Microsoft.Data.SqlClient.SqlCommand(
                "UPDATE BorrowRecords SET BorrowDate = @BorrowDate, DueDate = @DueDate WHERE BorrowId = @BorrowId", connection);
            command.Parameters.AddWithValue("@BorrowId", overdueBorrowId);
            command.Parameters.AddWithValue("@BorrowDate", DateTime.Today.AddDays(-10));
            command.Parameters.AddWithValue("@DueDate", DateTime.Today.AddDays(-1));
            command.ExecuteNonQuery();
        }

        var blocked = new ReaderEligibilityService().CheckEligibility(readerId);
        Assert.False(blocked.IsEligible);
        Assert.Equal(1, blocked.OverdueLoans);
        var exception = Assert.Throws<BusinessRuleException>(() =>
            circulation.BorrowBook(readerId, CopyIdFor(nextBookId)));
        Assert.Contains("quá hạn", exception.Message);
        Assert.Equal(1, new BookRepository().GetById(nextBookId)!.AvailableQuantity);

        circulation.ReturnBook(overdueBorrowId, ReturnCondition.Normal);
        Assert.True(new ReaderEligibilityService().CheckEligibility(readerId).IsEligible);
        circulation.BorrowBook(readerId, CopyIdFor(nextBookId));
        Assert.Equal(0, new BookRepository().GetById(nextBookId)!.AvailableQuantity);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void ReaderLifecycle_ActiveInactiveReactivate_PreservesIdentityAndHistory()
    {
        var readers = new ReaderService();
        var circulation = new BorrowService();
        int readerId = readers.AddReader(new Reader
        {
            FullName = "Độc giả lifecycle",
            StudentId = "SV-LIFECYCLE-INTEGRATION",
            Phone = "0911122233"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Sách lifecycle", Author = "Tác giả", PublishYear = 2026, Quantity = 1
        });

        readers.SuspendReader(readerId, "Kiểm tra lifecycle action");
        var suspended = readers.GetReaderById(readerId)!;
        Assert.Equal("Suspended", suspended.Status);
        Assert.Equal("Kiểm tra lifecycle action", suspended.SuspensionReason);
        Assert.Throws<BusinessRuleException>(() =>
            circulation.BorrowBook(readerId, CopyIdFor(bookId)));
        readers.ReactivateReader(readerId);

        int borrowId = circulation.BorrowBook(readerId, CopyIdFor(bookId));

        Assert.Throws<BusinessRuleException>(() => readers.DeactivateReader(readerId));
        circulation.ReturnBook(borrowId, ReturnCondition.Normal);
        readers.DeactivateReader(readerId);

        var inactive = readers.GetReaderById(readerId)!;
        Assert.Equal("Inactive", inactive.Status);
        Assert.False(new ReaderEligibilityService().CheckEligibility(readerId).IsEligible);
        Assert.Throws<BusinessRuleException>(() => readers.DeleteReader(readerId));
        Assert.Throws<BusinessRuleException>(() =>
            circulation.BorrowBook(readerId, CopyIdFor(bookId)));

        readers.ReactivateReader(readerId);
        var reactivated = readers.GetReaderById(readerId)!;
        Assert.Equal(readerId, reactivated.ReaderId);
        Assert.Equal("Active", reactivated.Status);
        Assert.Single(circulation.GetHistory(readerId: readerId));
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void AccountRegistration_LoginAndPasswordVerification_UseSql()
    {
        var auth = new AuthService();
        string email = $"integration-{Guid.NewGuid():N}@example.test";
        var registered = auth.Register("Nhân viên tích hợp", email, "StrongPassword123!");
        Assert.True(registered.Success, registered.Message);
        Assert.NotNull(registered.User);

        AuthService.CurrentUser = null;
        var loggedIn = auth.Login(email, "StrongPassword123!");
        Assert.True(loggedIn.Success, loggedIn.Message);
        Assert.Equal(registered.User.Id, loggedIn.User!.Id);
        Assert.True(new UserRepository().VerifyPassword(loggedIn.User.Id, "StrongPassword123!"));
        Assert.False(auth.Login(email, "wrong-password").Success);
    }

    private static int CopyIdFor(int bookId) => new BookCopyService().GetCopies(bookId)[0].CopyId;
}
