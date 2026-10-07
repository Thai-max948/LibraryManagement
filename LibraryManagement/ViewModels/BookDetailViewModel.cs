using System.Collections.ObjectModel;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.ViewModels;

public sealed class BookDetailViewModel : BaseViewModel
{
    private readonly int _bookId;
    private readonly BookService _books;
    private readonly BookCopyService _copies;
    private Book? _currentBook;
    private BookCopyInventory _inventory = BookCopyInventory.FromCopies([]);

    public BookDetailViewModel(int bookId) : this(bookId, new BookService(), new BookCopyService()) { }

    public BookDetailViewModel(int bookId, BookService books, BookCopyService copies)
    {
        if (bookId <= 0) throw new BusinessRuleException("Mã sách không hợp lệ.");
        _bookId = bookId;
        _books = books;
        _copies = copies;
        Refresh();
    }

    public Book? CurrentBook
    {
        get => _currentBook;
        private set
        {
            SetProperty(ref _currentBook, value);
            OnPropertyChanged(nameof(IsArchived));
            OnPropertyChanged(nameof(CanManage));
        }
    }

    public BookPricingPolicy PricingPolicy => _books.PricingPolicy;

    public ObservableCollection<BookCopy> Copies { get; } = new();

    public BookCopyInventory Inventory
    {
        get => _inventory;
        private set => SetProperty(ref _inventory, value);
    }

    public bool IsArchived => CurrentBook?.Status == BookStatuses.Archived;
    public bool CanManage => CurrentBook?.Status == BookStatuses.Active;
    public bool HasNoCopies => Copies.Count == 0;

    public void Refresh()
    {
        CurrentBook = _books.GetBookById(_bookId)
            ?? throw new BusinessRuleException("Sách không tồn tại.");
        var copies = _copies.GetCopies(_bookId).Where(copy => copy.BookId == _bookId).ToList();
        Copies.Clear();
        foreach (var copy in copies) Copies.Add(copy);
        Inventory = BookCopyInventory.FromCopies(copies);
        OnPropertyChanged(nameof(HasNoCopies));
    }

    public void UpdateBook(Book edited)
    {
        RequireActive();
        if (edited.BookId != _bookId) throw new BusinessRuleException("Không thể chỉnh sửa đầu sách khác.");
        _books.UpdateBook(edited);
        Refresh();
    }

    public void Archive()
    {
        RequireActive();
        _books.ArchiveBook(_bookId);
        Refresh();
    }

    public void Restore()
    {
        if (!IsArchived) throw new BusinessRuleException("Đầu sách đang hoạt động.");
        _books.RestoreBook(_bookId);
        Refresh();
    }

    private void RequireActive()
    {
        if (!CanManage) throw new BusinessRuleException("Đầu sách đã được lưu trữ; hãy khôi phục trước.");
    }
}
