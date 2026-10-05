using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class DashboardRepositoryIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task DashboardReadModel_UsesPhysicalCopiesCanonicalLoanStateSnapshotsAndBoundedActivity()
    {
        DateTime today = new(2099, 1, 10);
        DashboardDateRange range = CreateRange(today);
        var repository = new DashboardRepository();
        DashboardRepositoryData before = await repository.GetDashboardDataAsync(range);

        int activeReaderId = InsertReader("Dashboard active", "Active", isDeleted: false);
        int suspendedReaderId = InsertReader("Dashboard suspended", "Suspended", isDeleted: false);
        int deletedReaderId = InsertReader("Dashboard deleted", "Active", isDeleted: true);
        string activeBookTitle = "Dashboard active title " + Guid.NewGuid().ToString("N");
        int activeBookId = InsertBook(activeBookTitle, "Active");
        int archivedBookId = InsertBook("Dashboard archived title", "Archived");

        int verifiedAvailableCopyId = InsertCopy(activeBookId, "Available", "Good");
        InsertCopy(activeBookId, "Available", "LegacyUnverified");
        InsertCopy(activeBookId, "Borrowed", "Good");
        InsertCopy(activeBookId, "Damaged", "Good");
        InsertCopy(activeBookId, "UnderRepair", "Good");
        InsertCopy(activeBookId, "Lost", "Good");
        InsertCopy(activeBookId, "Retired", "Good");
        InsertCopy(archivedBookId, "Available", "Good");

        InsertBorrow(activeBookId, activeReaderId, today.AddDays(-6), today.AddDays(-1), null,
            "Borrowing");
        InsertBorrow(activeBookId, activeReaderId, today.AddDays(-5), today, null, "Borrowing");
        InsertBorrow(activeBookId, activeReaderId, today.AddDays(-4), today.AddDays(1), null, "Borrowing");
        InsertBorrow(activeBookId, activeReaderId, today.AddDays(-3), today.AddDays(2), null, "Borrowing");
        int returnedTodayId = InsertBorrow(activeBookId, activeReaderId, today.AddDays(-2), today.AddDays(2), today.AddHours(10),
            "Returned");
        int returnedStateMismatchId = InsertBorrow(activeBookId, activeReaderId, today.AddDays(-1), today.AddDays(1), today.AddHours(12),
            "Borrowing");
        int tiedBorrowId = InsertBorrow(activeBookId, activeReaderId, today.AddHours(9), today.AddDays(7), null, "Borrowing");
        int tiedReturnedId = InsertBorrow(activeBookId, activeReaderId, today.AddHours(9), today.AddDays(10), today.AddDays(1),
            "Returned", copyId: verifiedAvailableCopyId);
        int snapshotBorrowId = InsertBorrow(activeBookId, deletedReaderId, today.AddHours(11), today.AddDays(2), null,
            "Borrowing",
            readerSnapshot: "Historical reader name", bookSnapshot: "Historical book title", barcodeSnapshot: "HIST-BARCODE");
        int outsideRangeBorrowId = InsertBorrow(activeBookId, activeReaderId, today.AddDays(1), today.AddDays(10),
            today.AddDays(1).AddHours(9), "Returned");

        DashboardRepositoryData after = await repository.GetDashboardDataAsync(range);

        Assert.Equal(before.Snapshot.ActiveReaders + 1, after.Snapshot.ActiveReaders);
        Assert.Equal(before.Snapshot.TotalCopies + 8, after.Snapshot.TotalCopies);
        Assert.Equal(before.Snapshot.AvailableCopies + 1, after.Snapshot.AvailableCopies);
        Assert.Equal(before.Snapshot.BorrowedCopies + 1, after.Snapshot.BorrowedCopies);
        Assert.Equal(before.Snapshot.DamagedCopies + 1, after.Snapshot.DamagedCopies);
        Assert.Equal(before.Snapshot.UnderRepairCopies + 1, after.Snapshot.UnderRepairCopies);
        Assert.Equal(before.Snapshot.LostCopies + 1, after.Snapshot.LostCopies);
        Assert.Equal(before.Snapshot.RetiredCopies + 1, after.Snapshot.RetiredCopies);
        Assert.Equal(before.Snapshot.ActiveLoans + 6, after.Snapshot.ActiveLoans);
        Assert.Equal(before.Snapshot.DueSoonLoans + 2, after.Snapshot.DueSoonLoans);
        Assert.Equal(before.Snapshot.OverdueLoans + 1, after.Snapshot.OverdueLoans);

        Assert.Equal(5, after.RecentBorrowings.Count);
        Assert.Equal(new[] { outsideRangeBorrowId, snapshotBorrowId, tiedReturnedId, tiedBorrowId, returnedStateMismatchId },
            after.RecentBorrowings.Select(item => item.BorrowId));
        DashboardRecentBorrow snapshot = Assert.Single(after.RecentBorrowings, item => item.BorrowId == snapshotBorrowId);
        Assert.Equal("Historical reader name", snapshot.ReaderName);
        Assert.Equal("Historical book title", snapshot.BookTitle);
        Assert.Equal("HIST-BARCODE", snapshot.Barcode);
        DashboardRecentBorrow fallback = Assert.Single(after.RecentBorrowings, item => item.BorrowId == tiedReturnedId);
        Assert.Equal("Dashboard active", fallback.ReaderName);
        Assert.Equal(activeBookTitle, fallback.BookTitle);
        Assert.Equal("BK-" + verifiedAvailableCopyId.ToString("D6"), fallback.Barcode);

        Assert.Equal(new[] { outsideRangeBorrowId, tiedReturnedId, returnedStateMismatchId, returnedTodayId },
            after.RecentReturns.Select(item => item.BorrowId));
        Assert.Equal(7, after.CirculationCounts.Count);
        Assert.Equal(today.AddDays(-6), after.CirculationCounts[0].Date);
        Assert.Equal(today, after.CirculationCounts[^1].Date);
        Assert.Equal(9, after.CirculationCounts.Sum(point => point.BorrowCount));
        Assert.Equal(2, after.CirculationCounts[^1].ReturnCount);
        Assert.Equal(0, after.CirculationCounts.Sum(point => point.ReturnCount) - after.CirculationCounts[^1].ReturnCount);
        Assert.Equal(1, after.CirculationCounts[0].BorrowCount);
        Assert.Equal(3, after.CirculationCounts[^1].BorrowCount);

        _ = suspendedReaderId;
    }

    private static DashboardDateRange CreateRange(DateTime today) => new(
        today.Date,
        DueSoonDatePolicy.GetTargetDate(today),
        today.Date.AddDays(-6),
        today.Date.AddDays(1));

    private static int InsertReader(string name, string status, bool isDeleted)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"INSERT INTO dbo.Readers (FullName, StudentId, Status, IsDeleted)
            OUTPUT INSERTED.ReaderId VALUES (@Name, @StudentId, @Status, @IsDeleted)", connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@StudentId", "DASH-" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@IsDeleted", isDeleted);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int InsertBook(string title, string status)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"INSERT INTO dbo.Books (Title, Author, Status, Quantity, AvailableQuantity)
            OUTPUT INSERTED.BookId VALUES (@Title, N'Dashboard integration test', @Status, 0, 0)", connection);
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue("@Status", status);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int InsertCopy(int bookId, string status, string condition)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"INSERT INTO dbo.BookCopies (BookId, Status, Condition)
            OUTPUT INSERTED.CopyId VALUES (@BookId, @Status, @Condition)", connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@Condition", condition);
        int copyId = Convert.ToInt32(command.ExecuteScalar());
        using var barcode = new SqlCommand(@"UPDATE dbo.BookCopies SET Barcode = @Barcode
            WHERE CopyId = @CopyId AND Barcode IS NULL", connection);
        barcode.Parameters.AddWithValue("@CopyId", copyId);
        barcode.Parameters.AddWithValue("@Barcode", BookCopyBarcode.Format(copyId));
        Assert.Equal(1, barcode.ExecuteNonQuery());
        return copyId;
    }

    private static int InsertBorrow(
        int bookId,
        int readerId,
        DateTime borrowDate,
        DateTime dueDate,
        DateTime? returnDate,
        string status,
        int? copyId = null,
        string? readerSnapshot = null,
        string? bookSnapshot = null,
        string? barcodeSnapshot = null)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"INSERT INTO dbo.BorrowRecords
            (BookId, ReaderId, CopyId, BorrowDate, DueDate, ReturnDate, Status,
             ReaderNameSnapshot, BookTitleSnapshot, BarcodeSnapshot)
            OUTPUT INSERTED.BorrowId
            VALUES (@BookId, @ReaderId, @CopyId, @BorrowDate, @DueDate, @ReturnDate, @Status,
                    @ReaderSnapshot, @BookSnapshot, @BarcodeSnapshot)", connection);
        command.Parameters.AddWithValue("@BookId", bookId);
        command.Parameters.AddWithValue("@ReaderId", readerId);
        command.Parameters.AddWithValue("@CopyId", (object?)copyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BorrowDate", borrowDate);
        command.Parameters.AddWithValue("@DueDate", dueDate);
        command.Parameters.AddWithValue("@ReturnDate", (object?)returnDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@ReaderSnapshot", (object?)readerSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("@BookSnapshot", (object?)bookSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("@BarcodeSnapshot", (object?)barcodeSnapshot ?? DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
