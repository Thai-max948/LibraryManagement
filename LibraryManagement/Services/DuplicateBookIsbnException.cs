namespace LibraryManagement.Services;

public sealed class DuplicateBookIsbnException : BusinessRuleException
{
    public int ExistingBookId { get; }

    public DuplicateBookIsbnException(int existingBookId)
        : base("ISBN này đã thuộc một đầu sách. Hãy thêm bản sách vào đầu sách hiện có.")
    {
        ExistingBookId = existingBookId;
    }
}
