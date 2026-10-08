namespace LibraryManagement.Models;

public sealed record BookCopyInventory(int TotalCopies, int ActiveCopies, int Available, int Borrowed,
    int DamagedUnderRepair, int Lost, int Retired)
{
    public int Unavailable => DamagedUnderRepair + Lost;
    public bool IsBalanced => TotalCopies == Available + Borrowed + DamagedUnderRepair + Lost + Retired
        && ActiveCopies == TotalCopies - Retired;

    public static BookCopyInventory FromCopies(IEnumerable<BookCopy> copies)
    {
        var grouped = copies.GroupBy(copy => copy.Status)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        int Count(string status) => grouped.GetValueOrDefault(status);
        int total = grouped.Values.Sum();
        int retired = Count(BookCopyStatuses.Retired);
        return new BookCopyInventory(total, total - retired, Count(BookCopyStatuses.Available),
            Count(BookCopyStatuses.Borrowed), Count(BookCopyStatuses.Damaged) + Count(BookCopyStatuses.UnderRepair),
            Count(BookCopyStatuses.Lost), retired);
    }
}
