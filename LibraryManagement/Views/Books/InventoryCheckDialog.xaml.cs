using System.Windows;
using LibraryManagement.Models;
using LibraryManagement.Services;

namespace LibraryManagement.Views.Books;

public partial class InventoryCheckDialog : Window
{
    public InventoryCheckDialog(InventoryCheckService inventoryCheckService)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(inventoryCheckService);
        var report = inventoryCheckService.Check();
        ReportGrid.ItemsSource = report;
        int damagedUnderRepairCopies = report.Sum(row => row.DamagedUnderRepair);
        SummaryText.Text = report.Count == 0 ? "Không phát hiện chênh lệch."
            : $"{report.Count} đầu sách cần đối chiếu do số liệu lưu cũ hoặc dữ liệu bản sách/phiếu mượn chưa khớp. Có {damagedUnderRepairCopies} bản trong nhóm {BookCopyStatusDisplay.DamagedUnderRepair} ở các đầu sách hiển thị (đã tính trong Tổng hiện tại). Báo cáo không tự sửa dữ liệu.";
    }
}
