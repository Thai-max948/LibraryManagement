using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Commands;
using LibraryManagement.Views.Readers;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace LibraryManagement.ViewModels
{
    public class ReadersViewModel : BaseViewModel
    {
        private readonly ReaderService _readerService = new ReaderService();

        public ObservableCollection<Reader> Readers { get; set; } = new ObservableCollection<Reader>();

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); Search(); }
        }

        private Reader _selectedReader;
        public Reader SelectedReader
        {
            get => _selectedReader;
            set => SetProperty(ref _selectedReader, value);
        }

        public int TotalReaders => Readers.Count;

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public ReadersViewModel()
        {
            AddCommand = new RelayCommand(AddReader);
            EditCommand = new RelayCommand(EditReader, () => SelectedReader != null);
            DeleteCommand = new RelayCommand(DeleteReader, () => SelectedReader != null);
            Load();
        }

        public void Load()
        {
            try
            {
                Readers.Clear();
                foreach (var r in _readerService.GetAllReaders())
                    Readers.Add(r);
                OnPropertyChanged(nameof(TotalReaders));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải danh sách độc giả: " + ex.Message, "Lỗi");
            }
        }

        private void Search()
        {
            try
            {
                Readers.Clear();
                foreach (var r in _readerService.SearchReader(SearchText))
                    Readers.Add(r);
                OnPropertyChanged(nameof(TotalReaders));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tìm kiếm độc giả: " + ex.Message, "Lỗi");
            }
        }

        private void AddReader()
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

        private void EditReader()
        {
            var dialog = new EditReaderDialog(SelectedReader);
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

        private void DeleteReader()
        {
            var confirm = MessageBox.Show($"Xóa độc giả \"{SelectedReader.FullName}\"?", "Xác nhận", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _readerService.DeleteReader(SelectedReader.ReaderId);
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