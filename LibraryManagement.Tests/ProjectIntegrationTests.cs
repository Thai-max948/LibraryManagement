using System;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ProjectIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
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
        book.BookId = bookId;
        book.Quantity = 6;
        books.UpdateBook(book);
        Assert.Equal(6, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Contains(books.SearchBook("Sách tích hợp"), b => b.BookId == bookId);

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
            circulation.BorrowBook(readerId, bookId, DateTime.Today, DateTime.Today.AddDays(7)));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
        readers.ToggleStatus(readerId);
        int borrowId = circulation.BorrowBook(readerId, bookId, DateTime.Today, DateTime.Today.AddDays(7));

        Assert.Equal(1, new BookRepository().GetById(bookId)!.AvailableQuantity);
        Assert.Contains(circulation.GetBorrowingBooks(), r => r.BorrowId == borrowId && r.ReaderId == readerId);
        Assert.Throws<BusinessRuleException>(() => new ReaderService().DeleteReader(readerId));

        var returnedAt = DateTime.Today.AddDays(2);
        circulation.ReturnBook(borrowId, returnedAt);
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
        var record = Assert.Single(circulation.GetHistory(readerId: readerId, status: "Returned"),
            r => r.BorrowId == borrowId);
        Assert.Equal(returnedAt, record.ReturnDate);
        Assert.Throws<BusinessRuleException>(() => circulation.ReturnBook(borrowId, returnedAt));
        Assert.Equal(2, new BookRepository().GetById(bookId)!.AvailableQuantity);
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
}
