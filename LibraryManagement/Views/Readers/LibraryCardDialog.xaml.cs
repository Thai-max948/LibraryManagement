using System;
using System.Windows;
using System.Windows.Media;
using LibraryManagement.Models;

namespace LibraryManagement.Views.Readers
{
    public partial class LibraryCardDialog : Window
    {
        public LibraryCardDialog(Reader reader)
        {
            InitializeComponent();
            PopulateCard(reader);
        }

        private void PopulateCard(Reader reader)
        {
            FullNameText.Text = reader.FullName;
            ReaderIdText.Text = reader.FormattedId;

            bool isExternal = string.Equals(reader.ReaderType, "External", StringComparison.OrdinalIgnoreCase);

            if (isExternal)
            {
                TypeText.Text = "EXTERNAL READER";
                TypeText.Foreground = new SolidColorBrush(Color.FromRgb(194, 65, 12)); // Orange-700
                TypeBadge.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)); // Amber-100

                IdLabelText.Text = "IDENTITY NUMBER";
                IdValueText.Text = reader.DisplayIdentification;
            }
            else
            {
                TypeText.Text = "STUDENT";
                TypeText.Foreground = new SolidColorBrush(Color.FromRgb(3, 105, 161)); // Sky-700
                TypeBadge.Background = new SolidColorBrush(Color.FromRgb(224, 242, 254)); // Sky-100

                IdLabelText.Text = "STUDENT ID";
                IdValueText.Text = string.IsNullOrWhiteSpace(reader.StudentId) ? "-" : reader.StudentId;
            }

            bool isSuspended = reader.IsSuspended;
            StatusText.Text = isSuspended ? "SUSPENDED" : "ACTIVE";
            StatusText.Foreground = isSuspended
                ? new SolidColorBrush(Color.FromRgb(225, 29, 72)) // Danger
                : new SolidColorBrush(Color.FromRgb(5, 150, 105)); // Green
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
