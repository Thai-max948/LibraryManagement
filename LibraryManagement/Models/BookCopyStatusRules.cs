namespace LibraryManagement.Models;

public static class BookCopyStatusRules
{
    public static bool CanChangeManually(string current, string next) => (current, next) switch
    {
        (BookCopyStatuses.Available, BookCopyStatuses.Available or BookCopyStatuses.Damaged or BookCopyStatuses.Lost or BookCopyStatuses.UnderRepair or BookCopyStatuses.Retired) => true,
        (BookCopyStatuses.Damaged, BookCopyStatuses.Damaged or BookCopyStatuses.UnderRepair or BookCopyStatuses.Retired) => true,
        (BookCopyStatuses.UnderRepair, BookCopyStatuses.UnderRepair or BookCopyStatuses.Available or BookCopyStatuses.Damaged or BookCopyStatuses.Retired) => true,
        (BookCopyStatuses.Lost, BookCopyStatuses.Lost or BookCopyStatuses.Available or BookCopyStatuses.Retired) => true,
        (BookCopyStatuses.Retired, BookCopyStatuses.Retired) => true,
        _ => false
    };

}
