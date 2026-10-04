using System;

namespace LibraryManagement.Models
{
    public class BorrowRecord
    {
        public int BorrowId { get; set; }
        // BookId is a read projection retained for legacy records and existing list screens.
        public int BookId { get; set; }
        public int? BookCopyId { get; set; }

        // Compatibility alias for callers that still use the database's CopyId name.
        public int? CopyId
        {
            get => BookCopyId;
            set => BookCopyId = value;
        }
        public int ReaderId { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public int? LoanPeriodDaysApplied { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string? ReturnCondition { get; set; }
        public string? ConditionNote { get; set; }
        public DateTime? LostDate { get; set; }
        public string? LostNote { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
