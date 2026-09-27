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
        public ObservableCollection<string> StatusOptions { get; } = new ObservableCollection<string> { "All", "Active", "Suspended" };

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    Search();
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
                    Search();
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
                    Search();
                }
            }
        }

        private Reader? _selectedReader;
        public Reader? SelectedReader
        {
            get => _selectedReader;
            set => SetProperty(ref _selectedReader, value);
        }

        public int TotalReaders => Readers.Count;

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ViewCardCommand { get; }
        public ICommand DetailCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ToggleStatusCommand { get; }

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
            ToggleStatusCommand = new RelayCommand(param => ToggleStatus(param as Reader ?? SelectedReader), param => param != null || SelectedReader != null);
            Load();
        }

        public void Load()
        {
            try
            {
                Readers.Clear();
                foreach (var r in _readerService.GetAllReaders())
                {
                    Readers.Add(r);
                }
                OnPropertyChanged(nameof(TotalReaders));
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
                Readers.Clear();
                var results = _readerService.SearchReader(SearchText, SelectedTypeFilter, SelectedStatusFilter);
                foreach (var r in results)
                {
                    Readers.Add(r);
                }
                OnPropertyChanged(nameof(TotalReaders));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tìm kiếm độc giả: " + ex.Message, "Lỗi");
            }
        }

        public void AddReader()
        {
            var dialog = new AddReaderDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _readerService.AddReader(dialog.ResultReader);
                    Load();
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
                    Load();
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

            var cardDialog = new LibraryCardDialog(target);
            cardDialog.ShowDialog();
        }

        public void ViewDetail(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            var detailDialog = new ReaderDetailDialog(target);
            detailDialog.ShowDialog();
        }

        public void ToggleStatus(Reader? reader)
        {
            var target = reader ?? SelectedReader;
            if (target == null)
            {
                return;
            }

            try
            {
                _readerService.ToggleStatus(target.ReaderId);
                Load();
            }
            catch (BusinessRuleException ex)
            {
                MessageBox.Show(ex.Message, "Lỗi cập nhật trạng thái");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã có lỗi hệ thống: " + ex.Message, "Lỗi");
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