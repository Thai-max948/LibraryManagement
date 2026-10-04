namespace LibraryManagement.Models;

/// <summary>Canonical barcode formatting derived only from a persisted BookCopy identity.</summary>
public static class BookCopyBarcode
{
    public static string Format(int copyId)
    {
        if (copyId <= 0) throw new ArgumentOutOfRangeException(nameof(copyId), "CopyId must be positive.");
        return $"BK-{copyId:D6}";
    }
}
