using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class HistoryRepository
{
    public virtual HistoryPage GetPage(HistoryQuery query, DateTime today)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        bool hasBookCopies = HasBookCopiesSchema(connection);
        bool hasReaderDeletedFlag = HasColumn(connection, "Readers", "IsDeleted");
        string copyJoin = hasBookCopies ? "LEFT JOIN dbo.BookCopies bc ON bc.CopyId = br.CopyId" : string.Empty;
        string barcodeExpression = hasBookCopies ? "COALESCE(br.BarcodeSnapshot, bc.Barcode)" : "br.BarcodeSnapshot";
        string deletedExpression = hasReaderDeletedFlag
            ? "CASE WHEN r.IsDeleted = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END"
            : "CAST(0 AS bit)";
        string where = BuildWhere(query, hasBookCopies);
        int totalCount;
        using (var count = new SqlCommand($@"
            SELECT COUNT(*)
            FROM dbo.BorrowRecords br
            LEFT JOIN dbo.Readers r ON r.ReaderId = br.ReaderId
            LEFT JOIN dbo.Books b ON b.BookId = br.BookId
            {copyJoin}
            WHERE {where};", connection))
        {
            AddParameters(count, query, today);
            totalCount = Convert.ToInt32(count.ExecuteScalar());
        }

        int overdueCount;
        using (var overdue = new SqlCommand(@"
            SELECT COUNT(*) FROM dbo.BorrowRecords
            WHERE Status = 'Borrowing' AND DueDate < @TodayStart;", connection))
        {
            overdue.Parameters.Add("@TodayStart", SqlDbType.DateTime).Value = today.Date;
            overdueCount = Convert.ToInt32(overdue.ExecuteScalar());
        }

        var records = new List<HistoryRecordDto>();
        using (var command = new SqlCommand($@"
            SELECT br.BorrowId, br.ReaderId,
                COALESCE(br.ReaderNameSnapshot, r.FullName, CONCAT('Reader #', br.ReaderId)) AS ReaderName,
                {deletedExpression} AS IsReaderDeleted,
                br.BookId, COALESCE(br.BookTitleSnapshot, b.Title, CONCAT('Book #', br.BookId)) AS BookTitle,
                br.CopyId, {barcodeExpression} AS Barcode,
                br.BorrowDate, br.DueDate, br.ReturnDate, br.LostDate,
                CASE WHEN br.Status = 'Borrowing' AND br.DueDate < @TodayStart THEN 'Overdue' ELSE br.Status END AS DisplayStatus,
                CASE WHEN br.Status = 'Borrowing' AND br.DueDate < @TodayStart THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsOverdue,
                CASE WHEN br.Status = 'Borrowing' AND br.DueDate < @TodayStart
                    THEN DATEDIFF(day, CONVERT(date, br.DueDate), @TodayStart) ELSE 0 END AS OverdueDays,
                COALESCE(lastEvent.OccurredAt, br.LostDate, br.ReturnDate, br.BorrowDate) AS ActionDate,
                CASE WHEN br.ReaderNameSnapshot IS NULL OR br.BookTitleSnapshot IS NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsLegacy
            FROM dbo.BorrowRecords br
            LEFT JOIN dbo.Readers r ON r.ReaderId = br.ReaderId
            LEFT JOIN dbo.Books b ON b.BookId = br.BookId
            {copyJoin}
            OUTER APPLY (
                SELECT TOP (1) e.OccurredAt FROM dbo.CirculationAuditEvents e
                WHERE e.BorrowId = br.BorrowId ORDER BY e.OccurredAt DESC, e.AuditEventId DESC
            ) lastEvent
            WHERE {where}
            ORDER BY ActionDate DESC, br.BorrowId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
        {
            AddParameters(command, query, today);
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = (query.PageNumber - 1) * query.PageSize;
            command.Parameters.Add("@PageSize", SqlDbType.Int).Value = query.PageSize;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new HistoryRecordDto
                {
                    BorrowId = reader.GetInt32(0),
                    ReaderId = reader.GetInt32(1),
                    ReaderName = reader.GetString(2),
                    IsReaderDeleted = reader.GetBoolean(3),
                    BookId = reader.GetInt32(4),
                    BookTitle = reader.GetString(5),
                    BookCopyId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Barcode = reader.IsDBNull(7) ? null : reader.GetString(7),
                    BorrowDate = reader.GetDateTime(8),
                    DueDate = reader.GetDateTime(9),
                    ReturnDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    LostDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    Status = reader.GetString(12),
                    IsOverdue = reader.GetBoolean(13),
                    OverdueDays = reader.GetInt32(14),
                    ActionDate = reader.GetDateTime(15),
                    IsLegacyRecord = reader.GetBoolean(16)
                });
            }
        }

        return new HistoryPage
        {
            Records = records,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            OverdueCount = overdueCount
        };
    }

    private static string BuildWhere(HistoryQuery query, bool hasBookCopies)
    {
        var conditions = new List<string>();
        switch (query.Status)
        {
            case "Borrowing": conditions.Add("br.Status = 'Borrowing'"); break;
            case "Returned": conditions.Add("br.Status = 'Returned'"); break;
            case "Lost": conditions.Add("br.Status = 'Lost'"); break;
            case "Overdue": conditions.Add("br.Status = 'Borrowing' AND br.DueDate < @TodayStart"); break;
        }

        string dateColumn = query.DateFilter == HistoryDateFilter.ReturnDate ? "br.ReturnDate" : "br.BorrowDate";
        if (query.FromDate.HasValue) conditions.Add($"{dateColumn} >= @FromDate");
        if (query.ToDate.HasValue) conditions.Add($"{dateColumn} < @ToDateExclusive");
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            string barcodeExpression = hasBookCopies ? "COALESCE(br.BarcodeSnapshot, bc.Barcode, '')" : "COALESCE(br.BarcodeSnapshot, '')";
            conditions.Add($@"(COALESCE(br.ReaderNameSnapshot, r.FullName, CONCAT('Reader #', br.ReaderId)) LIKE @Search ESCAPE '\'
                OR COALESCE(br.BookTitleSnapshot, b.Title, CONCAT('Book #', br.BookId)) LIKE @Search ESCAPE '\'
                OR {barcodeExpression} LIKE @Search ESCAPE '\')");
        }
        return conditions.Count == 0 ? "1 = 1" : string.Join(" AND ", conditions);
    }

    private static void AddParameters(SqlCommand command, HistoryQuery query, DateTime today)
    {
        command.Parameters.Add("@TodayStart", SqlDbType.DateTime).Value = today.Date;
        if (query.FromDate.HasValue)
            command.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = query.FromDate.Value.Date;
        if (query.ToDate.HasValue)
            command.Parameters.Add("@ToDateExclusive", SqlDbType.DateTime).Value = query.ToDate.Value.Date.AddDays(1);
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            string escaped = query.SearchText.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal);
            command.Parameters.Add("@Search", SqlDbType.NVarChar, 512).Value = $"%{escaped}%";
        }
    }

    private static bool HasBookCopiesSchema(SqlConnection connection)
    {
        using var command = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID('dbo.BookCopies', 'U') IS NOT NULL
            AND COL_LENGTH('dbo.BookCopies', 'CopyId') IS NOT NULL THEN 1 ELSE 0 END", connection);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static bool HasColumn(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH(@TableName, @ColumnName) IS NOT NULL THEN 1 ELSE 0 END", connection);
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 257).Value = "dbo." + table;
        command.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 128).Value = column;
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }
}
