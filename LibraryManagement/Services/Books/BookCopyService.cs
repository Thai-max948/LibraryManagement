using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services;

public class BookCopyService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        BookCopyStatuses.Available, BookCopyStatuses.Borrowed, BookCopyStatuses.Lost,
        BookCopyStatuses.Damaged, BookCopyStatuses.UnderRepair, BookCopyStatuses.Retired
    };
    private readonly BookCopyRepository _repository;
    private readonly BookRepository _books;

    public BookCopyService() : this(new BookCopyRepository(), new BookRepository()) { }
    public BookCopyService(BookCopyRepository repository) : this(repository, new BookRepository()) { }
    public BookCopyService(BookCopyRepository repository, BookRepository books)
    {
        _repository = repository;
        _books = books;
    }

    public static int ParseQuantity(string? text)
    {
        if (!int.TryParse(text, out int quantity) || quantity is < 1 or > 100)
            throw new BusinessRuleException("Số lượng bản sách phải là số nguyên từ 1 đến 100.");
        return quantity;
    }

    public IReadOnlyList<int> AddCopies(int bookId, int quantity)
    {
        if (quantity is < 1 or > 100)
            throw new BusinessRuleException("Số lượng bản sách phải là số nguyên từ 1 đến 100.");
        var book = _books.GetById(bookId) ?? throw new BusinessRuleException("Sách không tồn tại.");
        if (book.Status != BookStatuses.Active)
            throw new BusinessRuleException("Không thể thêm bản sách cho đầu sách đã được archive.");
        try { return _repository.AddGeneratedCopies(bookId, quantity, BookCopyStatuses.Available, "Good"); }
        catch (InvalidOperationException exception)
        {
            if (exception.Message == "Book does not exist.")
                throw new BusinessRuleException("Sách không tồn tại.");
            if (exception.Message.Contains("đã được lưu trữ", StringComparison.Ordinal))
                throw new BusinessRuleException("Không thể thêm bản sách cho đầu sách đã được archive.");
            throw new BusinessRuleException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new BusinessRuleException("Barcode đã tồn tại; không có bản sách nào được thêm. Vui lòng thử lại.");
        }
    }

    public List<BookCopy> GetCopies(int bookId) => _repository.GetByBookId(bookId);
    public List<BookCopy> GetAvailableCopies(int bookId) => _repository.GetAvailableByBookId(bookId);
    public BookCopy? GetByBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) throw new BusinessRuleException("Vui lòng nhập barcode.");
        if (barcode.Trim().Length > 100) throw new BusinessRuleException("Barcode không được dài quá 100 ký tự.");
        return _repository.GetByBarcode(barcode.Trim());
    }

    public static string? GetBorrowBlockReason(BookCopy copy) => copy.Condition == "LegacyUnverified"
        ? "Bản sách cũ chưa được đối chiếu (LegacyUnverified)."
        : copy.Status == BookCopyStatuses.Available ? null : $"Bản sách đang ở trạng thái {copy.Status}, không thể mượn.";
    public BookCopyInventory GetInventory(int bookId) => BookCopyInventory.FromCopies(GetCopies(bookId));
    public static IReadOnlyList<string> GetManualTransitions(string currentStatus) => AllowedStatuses
        .Where(status => status != currentStatus && BookCopyStatusRules.CanChangeManually(currentStatus, status))
        .OrderBy(status => status, StringComparer.Ordinal)
        .ToArray();
    public static bool IsValidStatus(string status) => AllowedStatuses.Contains(status);

    public int AddCopy(int bookId) => AddCopies(bookId, 1).Single();

    public void ChangeStatus(int copyId, string status)
    {
        if (copyId <= 0) throw new BusinessRuleException("Mã bản sách không hợp lệ.");
        if (!IsValidStatus(status)) throw new BusinessRuleException("Trạng thái bản sách không hợp lệ.");
        if (status == BookCopyStatuses.Borrowed) throw new BusinessRuleException("Trạng thái Đang mượn được cập nhật qua nghiệp vụ mượn sách.");
        try
        {
            if (!_repository.SetStatus(copyId, status)) throw new BusinessRuleException("Bản sách không tồn tại.");
        }
        catch (InvalidOperationException exception)
        {
            throw new BusinessRuleException(exception.Message);
        }
    }

    public void MarkAvailable(int copyId)
    {
        var copy = GetCopyForManualAction(copyId);
        if (copy.Status == BookCopyStatuses.Borrowed)
            throw new BusinessRuleException("This copy is currently borrowed and must be returned through the Return workflow.");
        if (copy.Status == BookCopyStatuses.Retired)
            throw new BusinessRuleException("Retired copies cannot be returned to circulation.");
        if (copy.Status is not (BookCopyStatuses.Damaged or BookCopyStatuses.UnderRepair or BookCopyStatuses.Lost))
            throw new BusinessRuleException("Only lost or repair-needed copies can be marked available.");

        ChangeStatus(copyId, BookCopyStatuses.Available);
    }

    public void RetireCopy(int copyId)
    {
        var copy = GetCopyForManualAction(copyId);
        if (copy.Status == BookCopyStatuses.Retired
            || !BookCopyStatusRules.CanChangeManually(copy.Status, BookCopyStatuses.Retired))
            throw new BusinessRuleException("Bản sách này không thể được retire thủ công.");

        ChangeStatus(copyId, BookCopyStatuses.Retired);
    }

    private BookCopy GetCopyForManualAction(int copyId)
    {
        if (copyId <= 0) throw new BusinessRuleException("Mã bản sách không hợp lệ.");
        return _repository.GetById(copyId) ?? throw new BusinessRuleException("Bản sách không tồn tại.");
    }
}
