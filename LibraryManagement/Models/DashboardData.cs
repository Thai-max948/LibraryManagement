using System.Globalization;

namespace LibraryManagement.Models;

public sealed record DashboardSnapshot(
    int ActiveReaders,
    int TotalCopies,
    int AvailableCopies,
    int BorrowedCopies,
    int DamagedCopies,
    int UnderRepairCopies,
    int LostCopies,
    int RetiredCopies,
    int ActiveLoans,
    int DueSoonLoans,
    int OverdueLoans,
    decimal OutstandingFees)
{
    public int DamagedOrRepairCopies => DamagedCopies + UnderRepairCopies;

    public static DashboardSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m);
}

public sealed record DashboardDateRange(
    DateTime Today,
    DateTime DueSoonDate,
    DateTime CirculationStart,
    DateTime CirculationEndExclusive);

public sealed record DashboardRecentBorrow(
    int BorrowId,
    string ReaderName,
    string BookTitle,
    string? Barcode,
    DateTime BorrowDate)
{
    public string BorrowDateText => BorrowDate.ToString("dd/MM/yyyy");
}

public sealed record DashboardRecentReturn(
    int BorrowId,
    string ReaderName,
    string BookTitle,
    string? Barcode,
    DateTime ReturnDate)
{
    public string ReturnDateText => ReturnDate.ToString("dd/MM/yyyy");
}

public sealed record DashboardCirculationCount(DateTime Date, int BorrowCount, int ReturnCount);

public sealed record DashboardCirculationPoint(DateTime Date, int BorrowCount, int ReturnCount)
{
    public string DayLabel => Date.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
    public string DateLabel => Date.ToString("dd/MM", CultureInfo.InvariantCulture);
}

public sealed record DashboardRepositoryData(
    DashboardSnapshot Snapshot,
    IReadOnlyList<DashboardRecentBorrow> RecentBorrowings,
    IReadOnlyList<DashboardRecentReturn> RecentReturns,
    IReadOnlyList<DashboardCirculationCount> CirculationCounts);

public sealed record DashboardData(
    DashboardSnapshot Snapshot,
    IReadOnlyList<DashboardRecentBorrow> RecentBorrowings,
    IReadOnlyList<DashboardRecentReturn> RecentReturns,
    IReadOnlyList<DashboardCirculationPoint> Circulation);
