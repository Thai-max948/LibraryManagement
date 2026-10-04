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
    private BookCopy? _selectedCopy;
    private BookCopyInventory _inventory = BookCopyInventory.FromCopies([]);
    private bool _isAddingCopies;

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

    public BookCopy? SelectedCopy
    {
        get => _selectedCopy;
        set
        {
            SetProperty(ref _selectedCopy, value);
            OnPropertyChanged(nameof(ManualTransitions));
        }
    }

    public IReadOnlyList<string> ManualTransitions => SelectedCopy == null || IsArchived
        ? [] : BookCopyService.GetManualTransitions(SelectedCopy.Status)
            .Select(BookCopyStatusDisplay.GetLabel).Distinct().ToArray();

    public BookCopyInventory Inventory
    {
        get => _inventory;
        private set => SetProperty(ref _inventory, value);
    }

    public bool IsArchived => CurrentBook?.Status == BookStatuses.Archived;
    public bool CanManage => CurrentBook?.Status == BookStatuses.Active && !_isAddingCopies;
    public bool HasNoCopies => Copies.Count == 0;

    public void Refresh()
    {
        CurrentBook = _books.GetBookById(_bookId)
            ?? throw new BusinessRuleException("Sách không tồn tại.");
        var copies = _copies.GetCopies(_bookId).Where(copy => copy.BookId == _bookId).ToList();
        Copies.Clear();
        foreach (var copy in copies) Copies.Add(copy);
        SelectedCopy = null;
        Inventory = BookCopyInventory.FromCopies(copies);
        OnPropertyChanged(nameof(HasNoCopies));
        OnPropertyChanged(nameof(ManualTransitions));
    }

    public void AddCopies(int quantity)
    {
        RequireActive();
        _copies.AddCopies(_bookId, quantity);
        Refresh();
    }

    public async Task AddCopiesAsync(int quantity)
    {
        RequireActive();
        _isAddingCopies = true;
        OnPropertyChanged(nameof(CanManage));
        try
        {
            await Task.Run(() => _copies.AddCopies(_bookId, quantity));
            Refresh();
        }
        finally
        {
            _isAddingCopies = false;
            OnPropertyChanged(nameof(CanManage));
        }
    }

    public void ChangeSelectedCopyStatus(string status)
    {
        RequireActive();
        if (SelectedCopy == null) throw new BusinessRuleException("Chọn bản sách trước.");
        string targetStatus = BookCopyStatusDisplay.GetStoredStatus(status);
        if (!BookCopyService.GetManualTransitions(SelectedCopy.Status).Contains(targetStatus))
            throw new BusinessRuleException("Bước chuyển trạng thái này không hợp lệ.");
        _copies.ChangeStatus(SelectedCopy.CopyId, targetStatus);
        Refresh();
    }

    public void RetireSelectedCopy() => ChangeSelectedCopyStatus(BookCopyStatuses.Retired);

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
