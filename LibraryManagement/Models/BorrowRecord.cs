using System;

namespace LibraryManagement.Models
{
    public class BorrowRecord
    {
        public int BorrowId { get; set; }
        public int BookId { get; set; }
        public int ReaderId { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}