using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace LibraryManagement.Views.Books
{
    public partial class BooksView : UserControl
    {
        public BooksView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new BooksViewModel();
            }
        }

        private void ViewDetail_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BooksViewModel viewModel || viewModel.SelectedBook == null)
            {
                MessageBox.Show("Chọn một đầu sách trước.", "Book detail");
                return;
            }

            int bookId = viewModel.SelectedBook.BookId;
            try
            {
                ShowStartupMigrationNotice();
                new BookDetailDialog(bookId) { Owner = Window.GetWindow(this) }.ShowDialog();
                viewModel.Load();
            }
            catch (LibraryManagement.Services.BusinessRuleException exception)
            {
                MessageBox.Show(exception.Message, "Book detail");
                viewModel.Load();
            }
            catch (System.Exception)
            {
                MessageBox.Show("Không thể mở thông tin sách. Vui lòng thử lại.", "Lỗi");
            }
        }

        private void ToggleFilters_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is BooksViewModel viewModel)
                viewModel.IsFilterPanelOpen = !viewModel.IsFilterPanelOpen;
        }

        private void FilterPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || DataContext is not BooksViewModel viewModel) return;
            viewModel.IsFilterPanelOpen = false;
            e.Handled = true;
        }

        private void InventoryCheck_Click(object sender, RoutedEventArgs e)
        {
            try { new InventoryCheckDialog(new InventoryCheckService()) { Owner = Window.GetWindow(this) }.ShowDialog(); }
            catch (System.Exception exception) { MessageBox.Show("Không thể đối soát tồn kho: " + exception.Message, "Lỗi"); }
        }

        private void ManageCopies_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BooksViewModel viewModel || viewModel.SelectedBook == null)
            {
                MessageBox.Show("Chọn một đầu sách trước.", "Quản lý bản sách");
                return;
            }

            if (viewModel.SelectedBook.Status == LibraryManagement.Models.BookStatuses.Archived)
            {
                MessageBox.Show("Khôi phục đầu sách trước khi quản lý các bản sách.", "Quản lý bản sách");
                return;
            }

            try
            {
                ShowStartupMigrationNotice();

                var dialog = new BookCopiesDialog(viewModel.SelectedBook) { Owner = Window.GetWindow(this) };
                dialog.ShowDialog();
                viewModel.Load();
            }
            catch (System.Exception exception)
            {
                MessageBox.Show("Không thể mở danh sách bản sách: " + exception.Message, "Lỗi");
            }
        }

        private static void ShowStartupMigrationNotice()
        {
            var migration = (Application.Current as LibraryManagement.App)?.GetStartupBookCopyMigrationNotice();
            if (migration == null) return;
            var notices = new System.Collections.Generic.List<string>();
            if (migration.CopiesNeedingReview > 0)
                notices.Add($"{migration.CopiesNeedingReview} bản sách được đánh dấu UnderRepair / LegacyUnverified để đối chiếu tình trạng.");
            if (migration.UnresolvedLoans > 0)
                notices.Add($"{migration.UnresolvedLoans} phiếu mượn cũ chưa gắn với bản vật lý. Hãy đối chiếu barcode thực tế rồi cập nhật CopyId cho từng phiếu trước khi ghi nhận trả.");
            if (notices.Count > 0)
                MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, notices), "Cần đối chiếu dữ liệu cũ");
        }
    }
}
