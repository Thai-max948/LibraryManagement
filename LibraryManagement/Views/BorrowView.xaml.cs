using System;
using System.ComponentModel;
using System.Windows.Controls;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class BorrowView : UserControl
    {
        public BorrowView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = new BorrowViewModel(new MessageBoxDialogService());
                Loaded += (_, _) => BookSearchInputBox.Focus();
            }
        }

        private void ReaderRecommendationsScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) || e.VerticalChange <= 0 ||
                sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight <= 0)
                return;

            double threshold = Math.Min(48, Math.Max(12, scrollViewer.ScrollableHeight * 0.2));
            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - threshold &&
                DataContext is BorrowViewModel viewModel)
                _ = viewModel.LoadMoreReadersAsync();
        }

        private void BookRecommendationsScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) || e.VerticalChange <= 0 ||
                sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight <= 0)
                return;

            double threshold = Math.Min(48, Math.Max(12, scrollViewer.ScrollableHeight * 0.2));
            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - threshold &&
                DataContext is BorrowViewModel viewModel)
                _ = viewModel.LoadMoreBooksAsync();
        }
    }
}
