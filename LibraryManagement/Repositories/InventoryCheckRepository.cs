using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class InventoryCheckRepository
{
    private const string AggregatedReportSql = @"
;WITH ActiveLoans AS
(
    SELECT br.BookId, br.CopyId
    FROM dbo.BorrowRecords AS br
    WHERE br.Status COLLATE Latin1_General_100_BIN2 = N'Borrowing'
      AND DATALENGTH(br.Status) = DATALENGTH(N'Borrowing')
),
ActiveLoanByCopy AS
(
    SELECT CopyId, COUNT_BIG(*) AS ActiveLoanCount
    FROM ActiveLoans
    WHERE CopyId IS NOT NULL
    GROUP BY CopyId
),
CopyFacts AS
(
    SELECT c.BookId, c.CopyId,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'Available'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'Available') THEN 1 ELSE 0 END AS IsAvailable,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'Borrowed'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'Borrowed') THEN 1 ELSE 0 END AS IsBorrowed,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'Damaged'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'Damaged') THEN 1 ELSE 0 END AS IsDamaged,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'UnderRepair'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'UnderRepair') THEN 1 ELSE 0 END AS IsUnderRepair,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'Lost'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'Lost') THEN 1 ELSE 0 END AS IsLost,
        CASE WHEN c.Status COLLATE Latin1_General_100_BIN2 = N'Retired'
                   AND DATALENGTH(c.Status) = DATALENGTH(N'Retired') THEN 1 ELSE 0 END AS IsRetired
    FROM dbo.BookCopies AS c
),
CopyAggregate AS
(
    SELECT BookId,
        COUNT_BIG(*) AS TotalCurrent,
        SUM(CONVERT(bigint, IsAvailable)) AS AvailableCurrent,
        SUM(CONVERT(bigint, IsBorrowed)) AS Borrowed,
        SUM(CONVERT(bigint, IsDamaged)) + SUM(CONVERT(bigint, IsUnderRepair)) AS DamagedUnderRepair,
        SUM(CONVERT(bigint, IsLost)) AS Lost,
        SUM(CONVERT(bigint, IsRetired)) AS Retired
    FROM CopyFacts
    GROUP BY BookId
),
CopyLoanState AS
(
    SELECT cf.BookId, cf.CopyId, cf.IsBorrowed,
        COALESCE(alc.ActiveLoanCount, CONVERT(bigint, 0)) AS ActiveLoanCount
    FROM CopyFacts AS cf
    LEFT JOIN ActiveLoanByCopy AS alc ON alc.CopyId = cf.CopyId
),
CopyMismatchAggregate AS
(
    SELECT BookId,
        COUNT_BIG(CASE WHEN (IsBorrowed = 1 AND ActiveLoanCount <> 1)
                            OR (IsBorrowed = 0 AND ActiveLoanCount <> 0) THEN 1 END) AS InconsistentCopyCount
    FROM CopyLoanState
    GROUP BY BookId
),
LoanAggregate AS
(
    SELECT BookId,
        COUNT_BIG(CASE WHEN CopyId IS NULL THEN 1 END) AS UnresolvedLegacyLoans
    FROM ActiveLoans
    GROUP BY BookId
),
OrphanLoanAggregate AS
(
    SELECT al.BookId, COUNT_BIG(*) AS OrphanLoanCount
    FROM ActiveLoans AS al
    WHERE al.CopyId IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.BookCopies AS c
          WHERE c.BookId = al.BookId AND c.CopyId = al.CopyId
      )
    GROUP BY al.BookId
)
SELECT b.BookId, b.Title, b.Quantity AS StoredQuantity, b.AvailableQuantity AS StoredAvailable,
    COALESCE(ca.TotalCurrent, CONVERT(bigint, 0)) AS TotalCurrent,
    COALESCE(ca.AvailableCurrent, CONVERT(bigint, 0)) AS AvailableCurrent,
    COALESCE(ca.Borrowed, CONVERT(bigint, 0)) AS Borrowed,
    COALESCE(ca.DamagedUnderRepair, CONVERT(bigint, 0)) AS DamagedUnderRepair,
    COALESCE(ca.Lost, CONVERT(bigint, 0)) AS Lost,
    COALESCE(ca.Retired, CONVERT(bigint, 0)) AS Retired,
    COALESCE(ca.TotalCurrent, CONVERT(bigint, 0)) - COALESCE(ca.Retired, CONVERT(bigint, 0)) AS DerivedQuantity,
    COALESCE(ca.AvailableCurrent, CONVERT(bigint, 0)) AS DerivedAvailable,
    COALESCE(la.UnresolvedLegacyLoans, CONVERT(bigint, 0)) AS UnresolvedLegacyLoans,
    COALESCE(cm.InconsistentCopyCount, CONVERT(bigint, 0)) + COALESCE(ola.OrphanLoanCount, CONVERT(bigint, 0)) AS InconsistentCopies
FROM dbo.Books AS b
LEFT JOIN CopyAggregate AS ca ON ca.BookId = b.BookId
LEFT JOIN LoanAggregate AS la ON la.BookId = b.BookId
LEFT JOIN CopyMismatchAggregate AS cm ON cm.BookId = b.BookId
LEFT JOIN OrphanLoanAggregate AS ola ON ola.BookId = b.BookId
ORDER BY b.BookId;";

    public virtual List<InventoryCheckRow> GetReport()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!new BookCopyRepository().HasSchema(connection, transaction))
            throw new InvalidOperationException("Cần cập nhật BookCopy trước khi kiểm tra tồn kho.");

        // Read in circulation lock order; no UPDATE, reconciliation never repairs data.
        var books = new List<(int Id, string Title, int Quantity, int Available)>();
        using (var command = new SqlCommand("SELECT BookId, Title, Quantity, AvailableQuantity FROM dbo.Books ORDER BY BookId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) books.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        var copies = new List<BookCopy>();
        using (var command = new SqlCommand("SELECT CopyId, BookId, Status FROM dbo.BookCopies ORDER BY BookId, CopyId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) copies.Add(new BookCopy { CopyId = reader.GetInt32(0), BookId = reader.GetInt32(1), Status = reader.GetString(2) });
        var loans = new List<BorrowRecord>();
        using (var command = new SqlCommand("SELECT BookId, CopyId, Status FROM dbo.BorrowRecords WHERE Status = 'Borrowing' ORDER BY BookId, CopyId", connection, transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) loans.Add(new BorrowRecord { BookId = reader.GetInt32(0), BookCopyId = reader.IsDBNull(1) ? null : reader.GetInt32(1), Status = reader.GetString(2) });
        transaction.Commit();
        return books.Select(book => Evaluate(book.Id, book.Title, book.Quantity, book.Available, copies, loans)).ToList();
    }

    public virtual List<InventoryCheckRow> GetAggregatedReport()
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!new BookCopyRepository().HasSchema(connection, transaction))
            throw new InvalidOperationException("Cần cập nhật BookCopy trước khi kiểm tra tồn kho.");

        var rows = new List<InventoryCheckRow>();
        using (var command = new SqlCommand(AggregatedReportSql, connection, transaction))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add(new InventoryCheckRow
                {
                    BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    StoredQuantity = reader.GetInt32(reader.GetOrdinal("StoredQuantity")),
                    StoredAvailable = reader.GetInt32(reader.GetOrdinal("StoredAvailable")),
                    TotalCurrent = ReadAggregateInt32(reader, "TotalCurrent"),
                    AvailableCurrent = ReadAggregateInt32(reader, "AvailableCurrent"),
                    Borrowed = ReadAggregateInt32(reader, "Borrowed"),
                    DamagedUnderRepair = ReadAggregateInt32(reader, "DamagedUnderRepair"),
                    Lost = ReadAggregateInt32(reader, "Lost"),
                    Retired = ReadAggregateInt32(reader, "Retired"),
                    DerivedQuantity = ReadAggregateInt32(reader, "DerivedQuantity"),
                    DerivedAvailable = ReadAggregateInt32(reader, "DerivedAvailable"),
                    UnresolvedLegacyLoans = ReadAggregateInt32(reader, "UnresolvedLegacyLoans"),
                    InconsistentCopies = ReadAggregateInt32(reader, "InconsistentCopies")
                });
            }
        }

        transaction.Commit();
        return rows;
    }

    private static int ReadAggregateInt32(SqlDataReader reader, string columnName) =>
        checked((int)reader.GetInt64(reader.GetOrdinal(columnName)));

    public static InventoryCheckRow Evaluate(int bookId, string title, int storedQuantity, int storedAvailable,
        IEnumerable<BookCopy> copies, IEnumerable<BorrowRecord> loans)
    {
        var bookCopies = copies.Where(copy => copy.BookId == bookId).ToList();
        var bookLoans = loans.Where(loan => loan.BookId == bookId && loan.Status == "Borrowing").ToList();
        var inventory = BookCopyInventory.FromCopies(bookCopies);
        int inconsistent = bookCopies.Count(copy =>
        {
            int active = loans.Count(loan => loan.Status == "Borrowing" && loan.BookCopyId == copy.CopyId);
            return copy.Status == BookCopyStatuses.Borrowed ? active != 1 : active != 0;
        });
        inconsistent += bookLoans.Count(loan => loan.BookCopyId.HasValue &&
            !bookCopies.Any(copy => copy.CopyId == loan.BookCopyId));
        return new InventoryCheckRow
        {
            BookId = bookId,
            Title = title,
            StoredQuantity = storedQuantity,
            StoredAvailable = storedAvailable,
            TotalCurrent = inventory.TotalCopies,
            AvailableCurrent = inventory.Available,
            Borrowed = inventory.Borrowed,
            DamagedUnderRepair = inventory.DamagedUnderRepair,
            Lost = inventory.Lost,
            Retired = inventory.Retired,
            DerivedQuantity = inventory.ActiveCopies,
            DerivedAvailable = inventory.Available,
            UnresolvedLegacyLoans = bookLoans.Count(loan => !loan.BookCopyId.HasValue),
            InconsistentCopies = inconsistent
        };
    }
}
