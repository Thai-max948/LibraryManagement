using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LibraryManagement.Commands;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Views.Readers;

namespace LibraryManagement.ViewModels
{
    public class ReadersViewModel : BaseViewModel
    {
        private readonly ReaderService _readerService;

        public ObservableCollection<Reader> Readers { get; set; } = new ObservableCollection<Reader>();

        public ObservableCollection<string> TypeOptions { get; } = new ObservableCollection<string> { "All", "Student", "External" };
        public ObservableCollection<string> StatusOptions { get; } = new ObservableCollection<string> { "All", "Active", "Suspended", "Inactive" };
        public ObservableCollection<string> SortOptions { get; } = new ObservableCollection<string> { "Name A-Z", "Name Z-A", "Newest", "Oldest", "Status" };
        public ObservableCollection<int> PageSizeOptions { get; } = new ObservableCollection<int> { 10, 20, 50 };

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ResetAndSearch();
                }
            }
        }

        private string _selectedTypeFilter = "All";
        public string SelectedTypeFilter
        {
            get => _selectedTypeFilter;
            set
            {
                if (SetProperty(ref _selectedTypeFilter, value))
                {
                    ResetAndSearch();
                }
            }
        }

        private string _selectedStatusFilter = "All";
        public string SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set
            {
                if (SetProperty(ref _selectedStatusFilter, value))
                {
                    ResetAndSearch();
                }
            }
        }

        private Reader? _selectedReader;
        public Reader? SelectedReader
        {
            get => _selectedReader;
            set => SetProperty(ref _selectedReader, value);
        }

        private string _selectedSort = "Name A-Z";
        public string SelectedSort
        {
            get => _selectedSort;
            set { if (SetProperty(ref _selectedSort, value)) ResetAndSearch(); }
        }

        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set { if (SetProperty(ref _pageSize, value)) ResetAndSearch(); }
        }

        private int _currentPage = 1;
        public int CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

        private int _totalReaders;
        public int TotalReaders { get => _totalReaders; private set => SetProperty(ref _totalReaders, value); }

        private int _totalPages = 1;
        public int TotalPages { get => _totalPages; private set => SetProperty(ref _totalPages, value); }
        public string PageSummary => $"Trang {CurrentPage}/{TotalPages} • {TotalReaders} độc giả";

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ViewCardCommand { get; }
        public ICommand DetailCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }

        public ReadersViewModel() : this(new ReaderService())
        {
        }

        public ReadersViewModel(ReaderService readerService)
        {
            _readerService = readerService;
            AddCommand = new RelayCommand(AddReader);
            EditCommand = new RelayCommand(param => EditReader(param as Reader ?? SelectedReader), param => param != null || SelectedReader != null);
            ViewCardCommand = new RelayCommand(param => ViewCard(param as Reader ?? SelectedReader), param => param != null || SelectedReader != null);
            DetailCommand = new RelayCommand(param => ViewDetail(param as Reader ?? SelectedReader), param => param != null || SelectedReader != null);
            DeleteCommand = new RelayCommand(param => DeleteReader(param as Reader ?? SelectedReader), param => param != null || SelectedReader != null);
            PreviousPageCommand = new RelayCommand(_ => ChangePage(-1));
            NextPageCommand = new RelayCommand(_ => ChangePage(1));
            Load();
        }

        public void Load()
        {
            try
            {
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách độc giả: " + ex.Message, "Lỗi");
            }
        }

        public void Search()
        {
            try
            {
                var page = _readerService.GetReaderPage(SearchText, SelectedTypeFilter, SelectedStatusFilter,
                    SelectedSort, CurrentPage, PageSize);
                Readers.Clear();
                foreach (var r in page.Items)
                {
                    Readers.Add(r);
                }
                CurrentPage = page.PageNumber;
                TotalReaders = page.TotalCount;
                TotalPages = page.TotalPages;
                OnPropertyChanged(nameof(PageSummary));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tìm kiếm độc giả: " + ex.Message, "Lỗi");
            }
        }

        private void ResetAndSearch()
        {
            CurrentPage = 1;
            Search();
        }

        private void ChangePage(int delta)
        {
            int target = Math.Clamp(CurrentPage + delta, 1, TotalPages);
            if (target == CurrentPage) return;
            CurrentPage = target;
            Search();
        }

        public void AddReader()
        {
            var dialog = new AddReaderDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _readerService.AddReader(dialog.ResultReader);
                    Search();
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể thêm độc giả");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        public void EditReader(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            var dialog = new EditReaderDialog(target);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _readerService.UpdateReader(dialog.ResultReader);
                    Search();
                }
                catch (BusinessRuleException ex)
                {
                    MessageBox.Show(ex.Message, "Không thể cập nhật độc giả");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
                }
            }
        }

        public void ViewCard(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            try
            {
                var current = _readerService.GetReaderById(target.ReaderId);
                if (current == null || current.IsDeleted)
                    throw new BusinessRuleException("Độc giả không còn tồn tại.");
                new LibraryCardDialog(current).ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Không thể xem thẻ");
            }
        }

        public void ViewDetail(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            try
            {
                var profile = _readerService.GetReaderProfile(target.ReaderId);
                new ReaderDetailDialog(profile, _readerService).ShowDialog();
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Không thể xem chi tiết");
            }
        }

        public void DeleteReader(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            var confirm = MessageBox.Show($"Xóa độc giả \"{target.FullName}\"?", "Xác nhận", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _readerService.DeleteReader(target.ReaderId);
                Load();
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Không thể xóa độc giả");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống xảy ra: " + ex.Message, "Lỗi");
            }
        }
    }
}
