namespace LibraryManagement.Models
{
    public class Book
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public decimal? ReplacementValue { get; set; }
        public decimal? RentalPrice { get; set; }
        public string? Language { get; set; }
        public string LanguageDisplay => LanguageCatalog.GetDisplayName(Language);
        public string? Isbn { get; set; }
        public string Status { get; set; } = BookStatuses.Active;
        public DateTime? ArchivedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int PublishYear { get; set; }
        public int Quantity { get; set; }
        public int AvailableQuantity { get; set; }
        public int? BorrowedCopies { get; set; }
    }
}
