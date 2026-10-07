using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Views.Books;

namespace LibraryManagement.ViewModels;

public class BooksViewModel : BaseViewModel
{
    private static readonly BookStatusFilterOption[] ViewScopeOptionsSource =
    [
        new(BookStatusFilter.Active, "Active Books"),
        new(BookStatusFilter.Archived, "Archived Books"),
        new(BookStatusFilter.All, "All Books")
    ];

    private static readonly BookSortOption[] SortOptionsSource =
    [
        new("Title A-Z", "Title", "ASC"),
        new("Title Z-A", "Title", "DESC"),
        new("Newest year", "PublishYear", "DESC"),
        new("Oldest year", "PublishYear", "ASC"),
        new("Recently added", "CreatedAt", "DESC")
    ];

    private readonly BookService _bookService;
    private readonly List<string> _allAuthors = new();
    private BookSearchQuery _appliedFilters = new();
    private BookPriceRange _bookPriceRange = new(null, null);
    private Book? _selectedBook;
    private string _searchText = string.Empty;
    private string _categoryFilter = BookFilterCodes.All;
    private string _authorFilterText = string.Empty;
    private string _languageCodeFilter = LanguageCatalog.AllFilterCode;
    private string _publisherFilter = BookFilterCodes.All;
    private string _publishYearFromText = string.Empty;
    private string _publishYearToText = string.Empty;
    private string _minBookPriceText = string.Empty;
    private string _maxBookPriceText = string.Empty;
    private BookStatusFilter _viewScope = BookStatusFilter.Active;
    private bool _isFilterPanelOpen;
    private int _pageNumber = 1;
    private int _pageSize = 50;
    private int _totalCount;
    private int _totalPages = 1;
    private string _errorMessage = string.Empty;
    private string _validationMessage = string.Empty;
    private BookSortOption _selectedSortOption = SortOptionsSource[0];
    private BookCatalogMetrics _metrics = new(0, 0, 0);
    private double _priceSliderLowerValue;
    private double _priceSliderUpperValue;

    public ObservableCollection<Book> Books { get; } = new();
    public ObservableCollection<BookFilterOption> CategoryFilterOptions { get; } = new();
    public ObservableCollection<string> AuthorFilterOptions { get; } = new();
    public ObservableCollection<BookFilterOption> PublisherFilterOptions { get; } = new();
    public ObservableCollection<BookFilterChip> ActiveFilterChips { get; } = new();
    public IReadOnlyList<BookStatusFilterOption> ViewScopeOptions { get; } = ViewScopeOptionsSource;
    public IReadOnlyList<LanguageOption> LanguageFilterOptions { get; } = LanguageCatalog.FilterOptions;
    public IReadOnlyList<BookSortOption> SortOptions { get; } = SortOptionsSource;
    public IReadOnlyList<int> PageSizeOptions { get; } = new[] { 25, 50, 100 };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty)) ResetPageAndSearch();
        }
    }

    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? BookFilterCodes.All : value.Trim();
            if (SetProperty(ref _categoryFilter, normalized)) UpdateFilterValidation();
        }
    }

    public string AuthorFilterText
    {
        get => _authorFilterText;
        set
        {
            if (!SetProperty(ref _authorFilterText, value ?? string.Empty)) return;
            UpdateAuthorSuggestions();
            UpdateFilterValidation();
        }
    }

    public string LanguageCodeFilter
    {
        get => _languageCodeFilter;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? LanguageCatalog.AllFilterCode
                : LanguageFilterOptions.FirstOrDefault(option =>
                    string.Equals(option.Code, value.Trim(), StringComparison.OrdinalIgnoreCase))?.Code ?? value.Trim();
            if (SetProperty(ref _languageCodeFilter, normalized)) UpdateFilterValidation();
        }
    }

    public string PublisherFilter
    {
        get => _publisherFilter;
        set
        {
            if (SetProperty(ref _publisherFilter, string.IsNullOrWhiteSpace(value) ? BookFilterCodes.All : value.Trim()))
                UpdateFilterValidation();
        }
    }

    public string PublishYearFromText
    {
        get => _publishYearFromText;
        set
        {
            if (SetProperty(ref _publishYearFromText, value ?? string.Empty)) UpdateFilterValidation();
        }
    }

    public string PublishYearToText
    {
        get => _publishYearToText;
        set
        {
            if (SetProperty(ref _publishYearToText, value ?? string.Empty)) UpdateFilterValidation();
        }
    }

    public string MinBookPriceText
    {
        get => _minBookPriceText;
        set
        {
            if (!SetProperty(ref _minBookPriceText, value ?? string.Empty)) return;
            SyncLowerSliderFromInput();
            SyncUpperSliderFromInput();
            UpdateFilterValidation();
        }
    }

    public string MaxBookPriceText
    {
        get => _maxBookPriceText;
        set
        {
            if (!SetProperty(ref _maxBookPriceText, value ?? string.Empty)) return;
            SyncUpperSliderFromInput();
            SyncLowerSliderFromInput();
            UpdateFilterValidation();
        }
    }

    public double PriceSliderMinimum => _bookPriceRange.Minimum is decimal minimum ? (double)minimum : 0d;
    public double PriceSliderMaximum => _bookPriceRange.Maximum is decimal maximum ? (double)maximum : 0d;
    public double PriceSliderSmallChange
    {
        get
        {
            double range = PriceSliderMaximum - PriceSliderMinimum;
            return range <= 0 ? 0.01 : Math.Max(0.01, Math.Min(1d, range / 100d));
        }
    }

    public double PriceSliderLowerValue
    {
        get => _priceSliderLowerValue;
        set
        {
            double upper = Math.Clamp(_priceSliderUpperValue, PriceSliderMinimum, PriceSliderMaximum);
            double normalized = Math.Clamp(value, PriceSliderMinimum, upper);
            if (!SetProperty(ref _priceSliderLowerValue, normalized)) return;
            MinBookPriceText = IsAtPriceBound(normalized, _bookPriceRange.Minimum) ? string.Empty : SliderValueToText(normalized);
        }
    }

    public double PriceSliderUpperValue
    {
        get => _priceSliderUpperValue;
        set
        {
            double lower = Math.Clamp(_priceSliderLowerValue, PriceSliderMinimum, PriceSliderMaximum);
            double normalized = Math.Clamp(value, lower, PriceSliderMaximum);
            if (!SetProperty(ref _priceSliderUpperValue, normalized)) return;
            MaxBookPriceText = IsAtPriceBound(normalized, _bookPriceRange.Maximum) ? string.Empty : SliderValueToText(normalized);
        }
    }

    public bool HasConfiguredBookPrices => _bookPriceRange.HasConfiguredPrices;
    public string NoConfiguredPricesText => "No configured prices";

    public BookStatusFilter ViewScope
    {
        get => _viewScope;
        set
        {
            if (Enum.IsDefined(value) && SetProperty(ref _viewScope, value)) ResetPageAndSearch();
        }
    }

    public bool IsFilterPanelOpen
    {
        get => _isFilterPanelOpen;
        set
        {
            if (!SetProperty(ref _isFilterPanelOpen, value)) return;
            CopyAppliedFiltersToDraft();
            UpdateFilterValidation();
        }
    }

    public BookSortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (value != null && SetProperty(ref _selectedSortOption, value)) ResetPageAndSearch();
        }
    }

    public int PageNumber
    {
        get => _pageNumber;
        private set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                OnPropertyChanged(nameof(PageSummary));
                OnPropertyChanged(nameof(RangeSummary));
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(HasNextPage));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (!PageSizeOptions.Contains(value) || !SetProperty(ref _pageSize, value)) return;
            OnPropertyChanged(nameof(RangeSummary));
            ResetPageAndSearch();
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(RangeSummary));
                OnPropertyChanged(nameof(ResultCountText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(PageSummary));
                OnPropertyChanged(nameof(HasNextPage));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
                OnPropertyChanged(nameof(CanApplyFilters));
                OnPropertyChanged(nameof(IsEmpty));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);
    public bool CanApplyFilters => !HasValidationError;
    public bool IsEmpty => !HasError && !HasValidationError && TotalCount == 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public int FilterCount => ActiveFilterChips.Count;
    public string FilterButtonText => FilterCount == 0 ? "Filters" : $"Filters {FilterCount}";
    public int TotalBooks => _metrics.TotalBooks;
    public int TotalAvailable => _metrics.AvailableBooks;
    public int TotalBorrowed => _metrics.BorrowedBooks;
    public string PricingRateBadge => $"AUTO · {_bookService.PricingPolicy.RentalRate.ToString("P0", CultureInfo.InvariantCulture)}";
    public string PageSummary => $"Page {PageNumber} / {TotalPages}";
    public string RangeSummary => TotalCount == 0
        ? "Showing 0 of 0"
        : $"Showing {(PageNumber - 1) * PageSize + 1}-{Math.Min(PageNumber * PageSize, TotalCount)} of {TotalCount}";
    public string ResultCountText => $"{TotalCount:N0} {(TotalCount == 1 ? "result" : "results")}";

    public Book? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (SetProperty(ref _selectedBook, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand RemoveFilterCommand { get; }
    public ICommand RetryCommand { get; }

    public BooksViewModel() : this(new BookService()) { }

    public BooksViewModel(BookService bookService)
    {
        _bookService = bookService;
        AddCommand = new RelayCommand(AddBook);
        EditCommand = new RelayCommand(EditBook, () => SelectedBook?.Status == BookStatuses.Active);
        ArchiveCommand = new RelayCommand(ArchiveBook, () => SelectedBook?.Status == BookStatuses.Active);
        RestoreCommand = new RelayCommand(RestoreBook, () => SelectedBook?.Status == BookStatuses.Archived);
        PreviousPageCommand = new RelayCommand(() => ChangePage(PageNumber - 1), () => HasPreviousPage);
        NextPageCommand = new RelayCommand(() => ChangePage(PageNumber + 1), () => HasNextPage);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        ApplyFiltersCommand = new RelayCommand(ApplyFilters, () => CanApplyFilters);
        RemoveFilterCommand = new RelayCommand(RemoveFilter);
        RetryCommand = new RelayCommand(Load);
        Load();
    }

    public void Load()
    {
        try
        {
            string previousCategory = _appliedFilters.Category ?? BookFilterCodes.All;
            string previousPublisher = _appliedFilters.Publisher ?? BookFilterCodes.All;
            var categories = _bookService.GetCategoryFilterOptions();
            var publishers = _bookService.GetPublisherFilterOptions();
            _allAuthors.Clear();
            _allAuthors.AddRange((_bookService.GetDistinctAuthors() ?? Array.Empty<string>())
                .Where(author => !string.IsNullOrWhiteSpace(author))
                .Select(author => author.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(author => author, StringComparer.OrdinalIgnoreCase));
            _bookPriceRange = _bookService.GetBookPriceRange() ?? new BookPriceRange(null, null);

            CategoryFilterOptions.Clear();
            foreach (var option in categories) CategoryFilterOptions.Add(option);
            PublisherFilterOptions.Clear();
            foreach (var option in publishers) PublisherFilterOptions.Add(option);

            string refreshedCategory = CategoryFilterOptions.Any(option => option.Value == previousCategory)
                ? previousCategory : BookFilterCodes.All;
            string? refreshedPublisher = _appliedFilters.Publisher is null
                || PublisherFilterOptions.Any(option => option.Value == previousPublisher) ? _appliedFilters.Publisher : null;
            string? refreshedAuthor = _appliedFilters.Author is null
                || _allAuthors.Contains(_appliedFilters.Author, StringComparer.OrdinalIgnoreCase) ? _appliedFilters.Author : null;
            _appliedFilters = _appliedFilters with
            {
                Category = refreshedCategory,
                Publisher = refreshedPublisher,
                Author = refreshedAuthor
            };
            OnPropertyChanged(nameof(PriceSliderMinimum));
            OnPropertyChanged(nameof(PriceSliderMaximum));
            OnPropertyChanged(nameof(PriceSliderSmallChange));
            OnPropertyChanged(nameof(HasConfiguredBookPrices));
            OnPropertyChanged(nameof(NoConfiguredPricesText));
            CopyAppliedFiltersToDraft();
            UpdateAuthorSuggestions();

            _metrics = _bookService.GetActiveCatalogMetrics();
            OnPropertyChanged(nameof(TotalBooks));
            OnPropertyChanged(nameof(TotalAvailable));
            OnPropertyChanged(nameof(TotalBorrowed));
            UpdateActiveFilterChips();
            PageNumber = 1;
            LoadPage();
        }
        catch
        {
            ClearResults("Could not load books. Please try again.");
        }
    }

    private void LoadPage()
    {
        SelectedBook = null;
        try
        {
            var page = _bookService.GetPagedBooks(new BookSearchQuery
            {
                SearchText = SearchText,
                Category = _appliedFilters.Category,
                Author = _appliedFilters.Author,
                LanguageCode = _appliedFilters.LanguageCode,
                Publisher = _appliedFilters.Publisher,
                PublishYearFrom = _appliedFilters.PublishYearFrom,
                PublishYearTo = _appliedFilters.PublishYearTo,
                MinBookPrice = _appliedFilters.MinBookPrice,
                MaxBookPrice = _appliedFilters.MaxBookPrice,
                Status = ViewScope,
                PageNumber = PageNumber,
                PageSize = PageSize,
                SortBy = SelectedSortOption.SortBy,
                SortDirection = SelectedSortOption.SortDirection
            });

            Books.Clear();
            foreach (var book in page.Items) Books.Add(book);
            TotalCount = page.TotalCount;
            PageNumber = page.PageNumber;
            TotalPages = page.TotalPages;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(RangeSummary));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch
        {
            ClearResults("Could not search books. Please try again.");
        }
    }

    private void ClearResults(string error)
    {
        Books.Clear();
        TotalCount = 0;
        TotalPages = 1;
        PageNumber = 1;
        ErrorMessage = error;
        OnPropertyChanged(nameof(RangeSummary));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(IsEmpty));
        SelectedBook = null;
    }

    private void ResetPageAndSearch()
    {
        PageNumber = 1;
        LoadPage();
    }

    private void ChangePage(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > TotalPages) return;
        PageNumber = pageNumber;
        LoadPage();
    }

    private void UpdateActiveFilterChips()
    {
        ActiveFilterChips.Clear();
        var category = CategoryFilterOptions.FirstOrDefault(option => option.Value == _appliedFilters.Category);
        if (_appliedFilters.Category != BookFilterCodes.All)
            ActiveFilterChips.Add(new BookFilterChip("Category", category?.Label ?? _appliedFilters.Category ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(_appliedFilters.Author))
            ActiveFilterChips.Add(new BookFilterChip("Author", _appliedFilters.Author));

        var language = LanguageFilterOptions.FirstOrDefault(option => option.Code == _appliedFilters.LanguageCode);
        if (_appliedFilters.LanguageCode != LanguageCatalog.AllFilterCode)
            ActiveFilterChips.Add(new BookFilterChip("Language", language?.DisplayName ?? _appliedFilters.LanguageCode ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(_appliedFilters.Publisher))
        {
            var publisher = PublisherFilterOptions.FirstOrDefault(option => option.Value == _appliedFilters.Publisher);
            ActiveFilterChips.Add(new BookFilterChip("Publisher", publisher?.Label ?? _appliedFilters.Publisher));
        }

        bool hasFromYear = _appliedFilters.PublishYearFrom is int;
        bool hasToYear = _appliedFilters.PublishYearTo is int;
        if (hasFromYear || hasToYear)
        {
            string yearLabel = hasFromYear && hasToYear
                ? $"{_appliedFilters.PublishYearFrom}–{_appliedFilters.PublishYearTo}"
                : _appliedFilters.PublishYearFrom is int onlyFrom ? $"Year ≥ {onlyFrom}" : $"Year ≤ {_appliedFilters.PublishYearTo}";
            ActiveFilterChips.Add(new BookFilterChip("YearRange", yearLabel));
        }

        bool hasMinPrice = _appliedFilters.MinBookPrice is decimal;
        bool hasMaxPrice = _appliedFilters.MaxBookPrice is decimal;
        if (hasMinPrice || hasMaxPrice)
        {
            string priceLabel = hasMinPrice && hasMaxPrice
                ? $"Price {FormatBookPrice(_appliedFilters.MinBookPrice!.Value)}–{FormatBookPrice(_appliedFilters.MaxBookPrice!.Value)}"
                : _appliedFilters.MinBookPrice is decimal onlyMin
                    ? $"Price ≥ {FormatBookPrice(onlyMin)}"
                    : $"Price ≤ {FormatBookPrice(_appliedFilters.MaxBookPrice!.Value)}";
            ActiveFilterChips.Add(new BookFilterChip("PriceRange", priceLabel));
        }
        OnPropertyChanged(nameof(FilterCount));
        OnPropertyChanged(nameof(FilterButtonText));
    }

    private void RemoveFilter(object? parameter)
    {
        _appliedFilters = parameter?.ToString() switch
        {
            "Category" => _appliedFilters with { Category = BookFilterCodes.All },
            "Author" => _appliedFilters with { Author = null },
            "Language" => _appliedFilters with { LanguageCode = LanguageCatalog.AllFilterCode },
            "Publisher" => _appliedFilters with { Publisher = null },
            "YearRange" => _appliedFilters with { PublishYearFrom = null, PublishYearTo = null },
            "PriceRange" => _appliedFilters with { MinBookPrice = null, MaxBookPrice = null },
            _ => _appliedFilters
        };
        if (parameter?.ToString() is not ("Category" or "Author" or "Language" or "Publisher" or "YearRange" or "PriceRange")) return;
        CopyAppliedFiltersToDraft();
        UpdateFilterValidation();
        UpdateActiveFilterChips();
        ResetPageAndSearch();
    }

    private void ClearFilters()
    {
        _appliedFilters = _appliedFilters with
        {
            Category = BookFilterCodes.All,
            Author = null,
            LanguageCode = LanguageCatalog.AllFilterCode,
            Publisher = null,
            PublishYearFrom = null,
            PublishYearTo = null,
            MinBookPrice = null,
            MaxBookPrice = null
        };
        CopyAppliedFiltersToDraft();
        UpdateFilterValidation();
        UpdateActiveFilterChips();
        PageNumber = 1;
        LoadPage();
        IsFilterPanelOpen = false;
    }

    private void ApplyFilters()
    {
        UpdateFilterValidation();
        if (HasValidationError || !TryGetAuthor(out string? author)
            || !TryParseYear(PublishYearFromText, out int? yearFrom)
            || !TryParseYear(PublishYearToText, out int? yearTo)
            || !TryParsePrice(MinBookPriceText, out decimal? minPrice)
            || !TryParsePrice(MaxBookPriceText, out decimal? maxPrice))
            return;

        _appliedFilters = _appliedFilters with
        {
            Category = CategoryFilter,
            Author = author,
            LanguageCode = LanguageCodeFilter,
            Publisher = PublisherFilter == BookFilterCodes.All ? null : PublisherFilter,
            PublishYearFrom = yearFrom,
            PublishYearTo = yearTo,
            MinBookPrice = minPrice,
            MaxBookPrice = maxPrice
        };
        UpdateActiveFilterChips();
        PageNumber = 1;
        LoadPage();
        IsFilterPanelOpen = false;
    }

    private void CopyAppliedFiltersToDraft()
    {
        _categoryFilter = _appliedFilters.Category ?? BookFilterCodes.All;
        _authorFilterText = _appliedFilters.Author ?? string.Empty;
        _languageCodeFilter = _appliedFilters.LanguageCode ?? LanguageCatalog.AllFilterCode;
        _publisherFilter = _appliedFilters.Publisher ?? BookFilterCodes.All;
        _publishYearFromText = _appliedFilters.PublishYearFrom?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _publishYearToText = _appliedFilters.PublishYearTo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _minBookPriceText = _appliedFilters.MinBookPrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        _maxBookPriceText = _appliedFilters.MaxBookPrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        _priceSliderLowerValue = Math.Clamp(
            (double)(_appliedFilters.MinBookPrice ?? _bookPriceRange.Minimum ?? 0m), PriceSliderMinimum, PriceSliderMaximum);
        _priceSliderUpperValue = Math.Clamp(
            (double)(_appliedFilters.MaxBookPrice ?? _bookPriceRange.Maximum ?? 0m), _priceSliderLowerValue, PriceSliderMaximum);
        OnPropertyChanged(nameof(CategoryFilter));
        OnPropertyChanged(nameof(AuthorFilterText));
        OnPropertyChanged(nameof(LanguageCodeFilter));
        OnPropertyChanged(nameof(PublisherFilter));
        OnPropertyChanged(nameof(PublishYearFromText));
        OnPropertyChanged(nameof(PublishYearToText));
        OnPropertyChanged(nameof(MinBookPriceText));
        OnPropertyChanged(nameof(MaxBookPriceText));
        OnPropertyChanged(nameof(PriceSliderLowerValue));
        OnPropertyChanged(nameof(PriceSliderUpperValue));
        OnPropertyChanged(nameof(PriceSliderMinimum));
        OnPropertyChanged(nameof(PriceSliderMaximum));
        OnPropertyChanged(nameof(PriceSliderSmallChange));
        UpdateAuthorSuggestions();
    }

    private void UpdateFilterValidation()
    {
        string? message = null;
        if (!TryParseYear(PublishYearFromText, out int? yearFrom) || !TryParseYear(PublishYearToText, out int? yearTo))
            message = "Enter a valid publish year or leave the field empty.";
        else if (yearFrom is int from && yearTo is int to && from > to)
            message = "Publish Year From must be less than or equal to Publish Year To.";
        else if (!TryParsePrice(MinBookPriceText, out decimal? minimum) || !TryParsePrice(MaxBookPriceText, out decimal? maximum))
            message = "Enter a valid book price or leave the field empty.";
        else if (minimum is decimal min && min < 0m || maximum is decimal max && max < 0m)
            message = "Book price must be zero or greater.";
        else if (minimum is decimal minPrice && maximum is decimal maxPrice && minPrice > maxPrice)
            message = "Minimum book price must be less than or equal to maximum book price.";
        else if (!TryGetAuthor(out _))
            message = "Choose an author from the suggestions or clear the field.";
        ValidationMessage = message ?? string.Empty;
    }

    private bool TryGetAuthor(out string? author)
    {
        author = null;
        if (string.IsNullOrWhiteSpace(AuthorFilterText)) return true;
        string entered = AuthorFilterText.Trim();
        author = _allAuthors.FirstOrDefault(value => string.Equals(value, entered, StringComparison.OrdinalIgnoreCase));
        return author is not null;
    }

    private void UpdateAuthorSuggestions()
    {
        string search = AuthorFilterText.Trim();
        var options = string.IsNullOrEmpty(search)
            ? _allAuthors
            : _allAuthors.Where(author => author.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        AuthorFilterOptions.Clear();
        foreach (string author in options) AuthorFilterOptions.Add(author);
    }

    private static bool TryParseYear(string value, out int? year)
    {
        year = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return false;
        year = parsed;
        return true;
    }

    private static bool TryParsePrice(string value, out decimal? price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal parsed)) return false;
        price = parsed;
        return true;
    }

    private void SyncLowerSliderFromInput()
    {
        if (TryParsePrice(MinBookPriceText, out decimal? price) && price is decimal value)
            _priceSliderLowerValue = Math.Clamp((double)value, PriceSliderMinimum, PriceSliderMaximum);
        else
            _priceSliderLowerValue = PriceSliderMinimum;
        if (_priceSliderLowerValue > _priceSliderUpperValue) _priceSliderLowerValue = _priceSliderUpperValue;
        OnPropertyChanged(nameof(PriceSliderLowerValue));
    }

    private void SyncUpperSliderFromInput()
    {
        if (TryParsePrice(MaxBookPriceText, out decimal? price) && price is decimal value)
            _priceSliderUpperValue = Math.Clamp((double)value, PriceSliderMinimum, PriceSliderMaximum);
        else
            _priceSliderUpperValue = PriceSliderMaximum;
        if (_priceSliderUpperValue < _priceSliderLowerValue) _priceSliderUpperValue = _priceSliderLowerValue;
        OnPropertyChanged(nameof(PriceSliderUpperValue));
    }

    private static bool IsAtPriceBound(double value, decimal? bound)
        => bound is decimal amount && Math.Abs(value - (double)amount) < 0.005d;

    private static string SliderValueToText(double value)
        => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatBookPrice(decimal value)
        => value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private void AddBook()
    {
        var dialog = new AddBookDialog(_bookService.PricingPolicy);
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _bookService.AddBook(dialog.ResultBook);
                Load();
            }
            catch (DuplicateBookIsbnException ex)
            {
                try
                {
                    var existing = _bookService.GetAllBooksIncludingArchived().FirstOrDefault(book => book.BookId == ex.ExistingBookId);
                    if (existing == null) throw new BusinessRuleException("Không tìm thấy đầu sách có ISBN này.");
                    string prompt = existing.Status == BookStatuses.Archived
                        ? "\nĐầu sách đang lưu trữ. Khôi phục và mở quản lý bản sách?"
                        : "\nMở quản lý bản sách để thêm bản mới?";
                    if (MessageBox.Show(ex.Message + prompt, "Đầu sách đã tồn tại", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        if (existing.Status == BookStatuses.Archived)
                        {
                            _bookService.RestoreBook(existing.BookId);
                            existing.Status = BookStatuses.Active;
                        }
                        new BookCopiesDialog(existing).ShowDialog();
                        Load();
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "Không thể quản lý bản sách");
                }
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể thêm sách");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
            }
        }
    }

    private void EditBook()
    {
        if (SelectedBook == null) return;
        var dialog = new EditBookDialog(SelectedBook, _bookService.PricingPolicy);
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _bookService.UpdateBook(dialog.ResultBook);
                Load();
            }
            catch (BusinessRuleException ex) { MessageBox.Show(ex.Message, "Không thể cập nhật sách"); }
            catch (Exception ex) { MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi"); }
        }
    }

    private void ArchiveBook()
    {
        if (SelectedBook == null) return;
        var confirm = MessageBox.Show($"Lưu trữ đầu sách \"{SelectedBook.Title}\"? Sách sẽ rời danh mục hoạt động; lịch sử mượn được giữ lại.", "Xác nhận lưu trữ", MessageBoxButton.YesNo);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            _bookService.ArchiveBook(SelectedBook.BookId);
            Load();
        }
        catch (BusinessRuleException ex) { MessageBox.Show(ex.Message, "Không thể lưu trữ sách"); }
        catch (Exception ex) { MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi"); }
    }

    private void RestoreBook()
    {
        if (SelectedBook == null) return;
        try
        {
            _bookService.RestoreBook(SelectedBook.BookId);
            Load();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Không thể khôi phục sách"); }
    }
}
