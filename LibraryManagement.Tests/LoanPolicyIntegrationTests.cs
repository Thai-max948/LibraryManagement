using System;
using System.Linq;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class LoanPolicyIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void StudentAndExternal_NewLoansUseCurrentPolicyAndSnapshotDays()
    {
        CheckLoan("Student", 14);
        CheckLoan("External", 7);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void PolicyChange_AffectsNewLoansButNotExistingDueDate()
    {
        int readerId = NewReader("Student");
        int first = new BorrowService().BorrowBook(readerId, NewCopy());
        var original = new BorrowRepository().GetById(first)!;
        SetPolicy("Student", 21, true);
        try
        {
            int second = new BorrowService().BorrowBook(readerId, NewCopy());
            var current = new BorrowRepository().GetById(second)!;
            Assert.Equal(original.DueDate, new BorrowRepository().GetById(first)!.DueDate);
            Assert.Equal(14, original.LoanPeriodDaysApplied);
            Assert.Equal(21, current.LoanPeriodDaysApplied);
            Assert.Equal(current.BorrowDate.Date.AddDays(21), current.DueDate);
        }
        finally { SetPolicy("Student", 14, true); }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void MissingActivePolicy_RejectsBeforeClaim()
    {
        int readerId = NewReader("External");
        int copyId = NewCopy();
        SetPolicy("External", 7, false);
        try
        {
            Assert.Throws<BusinessRuleException>(() => new BorrowService().BorrowBook(readerId, copyId));
            Assert.Equal(BookCopyStatuses.Available, CopyStatus(copyId));
            Assert.DoesNotContain(new BorrowRepository().GetBorrowingRecords(), record => record.BookCopyId == copyId);
        }
        finally { SetPolicy("External", 7, true); }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Database_RejectsNonPositiveLoanPeriod()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(
            "UPDATE dbo.LoanPolicies SET LoanPeriodDays = 0 WHERE ReaderType = 'Student'", connection);
        Assert.Throws<SqlException>(() => command.ExecuteNonQuery());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Migration_PreservesExistingLoanDueDates()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var before = new SqlCommand(
            "SELECT TOP (1) BorrowId, DueDate FROM dbo.BorrowRecords ORDER BY BorrowId", connection);
        int loanId;
        DateTime dueDate;
        using (var reader = before.ExecuteReader())
        {
            Assert.True(reader.Read());
            loanId = reader.GetInt32(0);
            dueDate = reader.GetDateTime(1);
        }
        LoanPolicyMigration.Apply();
        Assert.Equal(dueDate, new BorrowRepository().GetById(loanId)!.DueDate);
    }

    private static void CheckLoan(string readerType, int days)
    {
        int readerId = NewReader(readerType);
        int copyId = NewCopy();
        int loanId = new BorrowService().BorrowBook(readerId, copyId);
        var loan = new BorrowRepository().GetById(loanId)!;
        Assert.Equal(copyId, loan.BookCopyId);
        Assert.Equal(days, loan.LoanPeriodDaysApplied);
        Assert.Equal(loan.BorrowDate.Date.AddDays(days), loan.DueDate);
        Assert.InRange(loan.BorrowDate, DateTime.Now.AddMinutes(-2), DateTime.Now.AddMinutes(1));
    }

    private static int NewReader(string readerType) => new ReaderRepository().Add(new Reader
    {
        FullName = "Policy reader", ReaderType = readerType,
        StudentId = readerType == "Student" ? $"POL-{Guid.NewGuid():N}" : null,
        IdentityNumber = readerType == "External" ? Guid.NewGuid().ToString("N") : null
    });

    private static int NewCopy()
    {
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Policy book", Author = "Test", PublishYear = 2026, Quantity = 1
        });
        return new BookCopyService().GetCopies(bookId).Single().CopyId;
    }

    private static void SetPolicy(string readerType, int days, bool active)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(
            "UPDATE dbo.LoanPolicies SET LoanPeriodDays = @Days, IsActive = @Active WHERE ReaderType = @ReaderType", connection);
        command.Parameters.AddWithValue("@Days", days);
        command.Parameters.AddWithValue("@Active", active);
        command.Parameters.AddWithValue("@ReaderType", readerType);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static string CopyStatus(int copyId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand("SELECT Status FROM dbo.BookCopies WHERE CopyId = @CopyId", connection);
        command.Parameters.AddWithValue("@CopyId", copyId);
        return (string)command.ExecuteScalar()!;
    }
}
