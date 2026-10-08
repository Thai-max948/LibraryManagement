using System.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Tests;

public sealed class RepositoryMapperTests
{
    [Fact]
    public void BookDataMapperPreservesNullableFieldsAndBorrowedCopyCount()
    {
        var table = CreateBookTable(includeBorrowedCopies: true);
        table.Rows.Add(42, "Title", "Author", DBNull.Value, DBNull.Value, "vi", "9781234567890",
            125.50m, DBNull.Value, "Archived", DBNull.Value, DBNull.Value, DBNull.Value,
            2024, 3, 1, 2);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Book book = BookDataMapper.Map(reader);

        Assert.Equal(42, book.BookId);
        Assert.Equal(string.Empty, book.Category);
        Assert.Null(book.Publisher);
        Assert.Equal("vi", book.Language);
        Assert.Null(book.RentalPrice);
        Assert.Equal(125.50m, book.ReplacementValue);
        Assert.Null(book.ArchivedAt);
        Assert.Equal(3, book.Quantity);
        Assert.Equal(1, book.AvailableQuantity);
        Assert.Equal(2, book.BorrowedCopies);
    }

    [Fact]
    public void BookDataMapperSupportsProjectionWithoutBorrowedCopiesColumn()
    {
        var table = CreateBookTable(includeBorrowedCopies: false);
        table.Rows.Add(7, "Legacy", "Author", "History", DBNull.Value, DBNull.Value, DBNull.Value,
            DBNull.Value, DBNull.Value, "Active", DBNull.Value, DBNull.Value, DBNull.Value,
            1999, 2, 1);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Book book = BookDataMapper.Map(reader);

        Assert.Equal(7, book.BookId);
        Assert.Null(book.BorrowedCopies);
    }

    [Fact]
    public void BorrowRecordMapperPreservesLegacyNullCopyAndOutcomeFields()
    {
        var table = new DataTable();
        table.Columns.Add("BorrowId", typeof(int));
        table.Columns.Add("BookId", typeof(int));
        table.Columns.Add("BookCopyId", typeof(int));
        table.Columns.Add("ReaderId", typeof(int));
        table.Columns.Add("BorrowDate", typeof(DateTime));
        table.Columns.Add("DueDate", typeof(DateTime));
        table.Columns.Add("LoanPeriodDaysApplied", typeof(int));
        table.Columns.Add("ReturnCondition", typeof(string));
        table.Columns.Add("ConditionNote", typeof(string));
        table.Columns.Add("LostDate", typeof(DateTime));
        table.Columns.Add("LostNote", typeof(string));
        table.Columns.Add("ReturnDate", typeof(DateTime));
        table.Columns.Add("Status", typeof(string));
        table.Rows.Add(10, 20, DBNull.Value, 30, new DateTime(2026, 1, 2), new DateTime(2026, 1, 9),
            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, "Borrowing");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        BorrowRecord record = BorrowRecordMapper.Map(reader);

        Assert.Equal(10, record.BorrowId);
        Assert.Equal(20, record.BookId);
        Assert.Null(record.BookCopyId);
        Assert.Null(record.LoanPeriodDaysApplied);
        Assert.Null(record.ReturnCondition);
        Assert.Null(record.LostDate);
        Assert.Null(record.ReturnDate);
        Assert.Equal("Borrowing", record.Status);
    }

    [Fact]
    public void BorrowRecordMapperMapsCopyAndReturnOutcome()
    {
        var table = new DataTable();
        table.Columns.Add("BorrowId", typeof(int));
        table.Columns.Add("BookId", typeof(int));
        table.Columns.Add("BookCopyId", typeof(int));
        table.Columns.Add("ReaderId", typeof(int));
        table.Columns.Add("BorrowDate", typeof(DateTime));
        table.Columns.Add("DueDate", typeof(DateTime));
        table.Columns.Add("LoanPeriodDaysApplied", typeof(int));
        table.Columns.Add("ReturnCondition", typeof(string));
        table.Columns.Add("ConditionNote", typeof(string));
        table.Columns.Add("LostDate", typeof(DateTime));
        table.Columns.Add("LostNote", typeof(string));
        table.Columns.Add("ReturnDate", typeof(DateTime));
        table.Columns.Add("Status", typeof(string));
        table.Rows.Add(11, 21, 31, 41, new DateTime(2026, 2, 1), new DateTime(2026, 2, 8),
            7, "Damaged", "Torn cover", DBNull.Value, DBNull.Value, new DateTime(2026, 2, 9), "Returned");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        BorrowRecord record = BorrowRecordMapper.Map(reader);

        Assert.Equal(31, record.BookCopyId);
        Assert.Equal(7, record.LoanPeriodDaysApplied);
        Assert.Equal("Damaged", record.ReturnCondition);
        Assert.Equal("Torn cover", record.ConditionNote);
        Assert.Equal(new DateTime(2026, 2, 9), record.ReturnDate);
    }

    private static DataTable CreateBookTable(bool includeBorrowedCopies)
    {
        var table = new DataTable();
        table.Columns.Add("BookId", typeof(int));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("Author", typeof(string));
        table.Columns.Add("Category", typeof(string));
        table.Columns.Add("Publisher", typeof(string));
        table.Columns.Add("Language", typeof(string));
        table.Columns.Add("ISBN", typeof(string));
        table.Columns.Add("ReplacementValue", typeof(decimal));
        table.Columns.Add("RentalPrice", typeof(decimal));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("ArchivedAt", typeof(DateTime));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("UpdatedAt", typeof(DateTime));
        table.Columns.Add("PublishYear", typeof(int));
        table.Columns.Add("Quantity", typeof(int));
        table.Columns.Add("AvailableQuantity", typeof(int));
        if (includeBorrowedCopies)
            table.Columns.Add("BorrowedCopies", typeof(int));
        return table;
    }
}
