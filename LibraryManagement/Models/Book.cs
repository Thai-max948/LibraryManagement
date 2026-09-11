using System;
namespace LibraryManagement.Models
{
    public class Book
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Category { get; set; }
        public int PublishYear { get; set; }
        public int Quantity { get; set; }
        public int AvailableQuantity { get; set; }
    }
}
