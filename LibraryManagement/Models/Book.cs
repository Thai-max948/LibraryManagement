namespace LibraryManagement.Models
{
    public class Book
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int PublishYear { get; set; }
        public int Quantity { get; set; }
        public int AvailableQuantity { get; set; }
    }
}
