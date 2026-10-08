namespace LibraryManagement.Models;

public static class BookCopyStatuses
{
    public const string Available = "Available";
    public const string Borrowed = "Borrowed";
    public const string Lost = "Lost";
    public const string Damaged = "Damaged";
    public const string UnderRepair = "UnderRepair";
    public const string Retired = "Retired";
}

public static class BookCopyStatusDisplay
{
    public const string DamagedUnderRepair = @"Damaged\UnderRepair";

    public static string GetLabel(string status) => status is BookCopyStatuses.Damaged or BookCopyStatuses.UnderRepair
        ? DamagedUnderRepair
        : status;

    // Selecting the merged picker category stores the repair-needed state.
    public static string GetStoredStatus(string label) => label == DamagedUnderRepair
        ? BookCopyStatuses.UnderRepair
        : label;
}

public class BookCopy
{
    public int CopyId { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Status { get; set; } = BookCopyStatuses.Available;
    public string StatusDisplay => BookCopyStatusDisplay.GetLabel(Status);
    public string Condition { get; set; } = "Good";
    public DateTime CreatedAt { get; set; }
}
