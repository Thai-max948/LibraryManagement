using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

/// <summary>Read-only, bounded aggregate and activity queries for the Dashboard.</summary>
public sealed class DashboardRepository : IDashboardRepository
{
    private const string ReadDashboardSql = @"
SELECT
    (SELECT COUNT_BIG(*) FROM dbo.Readers
        WHERE IsDeleted = 0 AND UPPER(LTRIM(RTRIM(Status))) = N'ACTIVE') AS ActiveReaders,
    inventory.TotalCopies,
    inventory.AvailableCopies,
    inventory.BorrowedCopies,
    inventory.DamagedCopies,
    inventory.UnderRepairCopies,
    inventory.LostCopies,
    inventory.RetiredCopies,
    loans.ActiveLoans,
    loans.DueSoonLoans,
    loans.OverdueLoans
FROM
(
    SELECT COUNT_BIG(*) AS TotalCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'Available'
            AND bc.Condition <> N'LegacyUnverified'
            AND book.Status = N'Active' THEN 1 END) AS AvailableCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'Borrowed' THEN 1 END) AS BorrowedCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'Damaged' THEN 1 END) AS DamagedCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'UnderRepair' THEN 1 END) AS UnderRepairCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'Lost' THEN 1 END) AS LostCopies,
        COUNT_BIG(CASE WHEN bc.Status = N'Retired' THEN 1 END) AS RetiredCopies
    FROM dbo.BookCopies AS bc
    LEFT JOIN dbo.Books AS book ON book.BookId = bc.BookId
) AS inventory
CROSS JOIN
(
    SELECT
        COUNT_BIG(CASE WHEN Status = N'Borrowing' AND ReturnDate IS NULL THEN 1 END) AS ActiveLoans,
        COUNT_BIG(CASE WHEN Status = N'Borrowing' AND ReturnDate IS NULL
            AND DueDate >= @DueSoonStart AND DueDate < @DueSoonEnd THEN 1 END) AS DueSoonLoans,
        COUNT_BIG(CASE WHEN Status = N'Borrowing' AND ReturnDate IS NULL
            AND DueDate < @TodayStart THEN 1 END) AS OverdueLoans
    FROM dbo.BorrowRecords
) AS loans;

SELECT TOP (5) br.BorrowId,
    COALESCE(NULLIF(LTRIM(RTRIM(br.ReaderNameSnapshot)), N''), reader.FullName,
        CONCAT(N'Reader #', br.ReaderId)) AS ReaderName,
    COALESCE(NULLIF(LTRIM(RTRIM(br.BookTitleSnapshot)), N''), book.Title,
        CONCAT(N'Book #', br.BookId)) AS BookTitle,
    COALESCE(NULLIF(LTRIM(RTRIM(br.BarcodeSnapshot)), N''), copy.Barcode) AS Barcode,
    br.BorrowDate
FROM dbo.BorrowRecords AS br
LEFT JOIN dbo.Readers AS reader ON reader.ReaderId = br.ReaderId
LEFT JOIN dbo.Books AS book ON book.BookId = br.BookId
LEFT JOIN dbo.BookCopies AS copy ON copy.CopyId = br.CopyId AND copy.BookId = br.BookId
ORDER BY br.BorrowDate DESC, br.BorrowId DESC;

SELECT TOP (5) br.BorrowId,
    COALESCE(NULLIF(LTRIM(RTRIM(br.ReaderNameSnapshot)), N''), reader.FullName,
        CONCAT(N'Reader #', br.ReaderId)) AS ReaderName,
    COALESCE(NULLIF(LTRIM(RTRIM(br.BookTitleSnapshot)), N''), book.Title,
        CONCAT(N'Book #', br.BookId)) AS BookTitle,
    COALESCE(NULLIF(LTRIM(RTRIM(br.BarcodeSnapshot)), N''), copy.Barcode) AS Barcode,
    br.ReturnDate
FROM dbo.BorrowRecords AS br
LEFT JOIN dbo.Readers AS reader ON reader.ReaderId = br.ReaderId
LEFT JOIN dbo.Books AS book ON book.BookId = br.BookId
LEFT JOIN dbo.BookCopies AS copy ON copy.CopyId = br.CopyId AND copy.BookId = br.BookId
WHERE br.ReturnDate IS NOT NULL
ORDER BY br.ReturnDate DESC, br.BorrowId DESC;

;WITH Activity AS
(
    SELECT CONVERT(date, BorrowDate) AS ActivityDate, COUNT_BIG(*) AS BorrowCount, CONVERT(bigint, 0) AS ReturnCount
    FROM dbo.BorrowRecords
    WHERE BorrowDate >= @ActivityStart AND BorrowDate < @ActivityEndExclusive
    GROUP BY CONVERT(date, BorrowDate)
    UNION ALL
    SELECT CONVERT(date, ReturnDate) AS ActivityDate, CONVERT(bigint, 0) AS BorrowCount, COUNT_BIG(*) AS ReturnCount
    FROM dbo.BorrowRecords
    WHERE ReturnDate IS NOT NULL AND ReturnDate >= @ActivityStart AND ReturnDate < @ActivityEndExclusive
    GROUP BY CONVERT(date, ReturnDate)
)
SELECT ActivityDate, SUM(BorrowCount) AS BorrowCount, SUM(ReturnCount) AS ReturnCount
FROM Activity
GROUP BY ActivityDate
ORDER BY ActivityDate;";

    public async Task<DashboardRepositoryData> GetDashboardDataAsync(
        DashboardDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dateRange);

        // Available follows the same circulation guards as Borrow: a verified available copy
        // must belong to an active title. All status totals still count every physical copy.
        DueSoonDayWindow dueSoonWindow = DueSoonDatePolicy.GetDayWindow(dateRange.DueSoonDate);
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(ReadDashboardSql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("@TodayStart", SqlDbType.DateTime).Value = dateRange.Today.Date;
        command.Parameters.Add("@DueSoonStart", SqlDbType.DateTime).Value = dueSoonWindow.StartInclusive;
        command.Parameters.Add("@DueSoonEnd", SqlDbType.DateTime).Value = dueSoonWindow.EndExclusive;
        command.Parameters.Add("@ActivityStart", SqlDbType.DateTime).Value = dateRange.CirculationStart.Date;
        command.Parameters.Add("@ActivityEndExclusive", SqlDbType.DateTime).Value = dateRange.CirculationEndExclusive.Date;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Dashboard aggregate query returned no result row.");

        var snapshot = new DashboardSnapshot(
            ActiveReaders: ReadCount(reader, 0),
            TotalCopies: ReadCount(reader, 1),
            AvailableCopies: ReadCount(reader, 2),
            BorrowedCopies: ReadCount(reader, 3),
            DamagedCopies: ReadCount(reader, 4),
            UnderRepairCopies: ReadCount(reader, 5),
            LostCopies: ReadCount(reader, 6),
            RetiredCopies: ReadCount(reader, 7),
            ActiveLoans: ReadCount(reader, 8),
            DueSoonLoans: ReadCount(reader, 9),
            OverdueLoans: ReadCount(reader, 10),
            OutstandingFees: 0m);

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var recentBorrowings = new List<DashboardRecentBorrow>(5);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            recentBorrowings.Add(new DashboardRecentBorrow(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetDateTime(4)));
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var recentReturns = new List<DashboardRecentReturn>(5);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            recentReturns.Add(new DashboardRecentReturn(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetDateTime(4)));
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var circulationCounts = new List<DashboardCirculationCount>(7);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            circulationCounts.Add(new DashboardCirculationCount(
                reader.GetDateTime(0).Date, ReadCount(reader, 1), ReadCount(reader, 2)));
        }

        return new DashboardRepositoryData(snapshot, recentBorrowings, recentReturns, circulationCounts);
    }

    private static int ReadCount(SqlDataReader reader, int ordinal) => checked(Convert.ToInt32(reader.GetInt64(ordinal)));
}
