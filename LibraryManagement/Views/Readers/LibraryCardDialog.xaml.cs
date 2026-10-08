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

            DepartmentInfoPanel.Visibility = reader.IsLecturer ? Visibility.Visible : Visibility.Collapsed;
            DepartmentValueText.Text = reader.DisplayDepartment;

            if (reader.IsExternal)
            {
                TypeText.Text = "EXTERNAL READER";
                TypeText.Foreground = new SolidColorBrush(Color.FromRgb(194, 65, 12)); // Orange-700
                TypeBadge.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)); // Amber-100

                IdLabelText.Text = "IDENTITY NUMBER";
                IdValueText.Text = reader.DisplayIdentification;
            }
            else if (reader.IsLecturer)
            {
                TypeText.Text = "LECTURER";
                TypeText.Foreground = new SolidColorBrush(Color.FromRgb(109, 40, 217)); // Violet-700
                TypeBadge.Background = new SolidColorBrush(Color.FromRgb(237, 233, 254)); // Violet-100

                IdLabelText.Text = "LECTURER CODE";
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
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
