using System;

namespace LibraryManagement.Models
{
    public class Reader
    {
        public int ReaderId { get; set; }

        public string FormattedId => ReaderId > 0 ? $"R{ReaderId:D6}" : "R------";

        public string FullName { get; set; } = string.Empty;

        public string ReaderType { get; set; } = "Student";

        public string? StudentId { get; set; }

        public string? IdentityNumber { get; set; }

        public string DisplayIdentification
        {
            get
            {
                if (string.Equals(ReaderType, "External", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(IdentityNumber))
                    {
                        return "-";
                    }
                    string trimmed = IdentityNumber.Trim();
                    if (trimmed.Length <= 3 || trimmed.StartsWith('*'))
                    {
                        return trimmed;
                    }
                    return new string('*', trimmed.Length - 3) + trimmed[^3..];
                }

                return string.IsNullOrWhiteSpace(StudentId) ? "-" : StudentId.Trim();
            }
        }

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        public string Status { get; set; } = "Active";

        public bool IsDeleted { get; set; }

        public bool IsSuspended => string.Equals(Status, "Suspended", StringComparison.OrdinalIgnoreCase);

        public bool IsStudent => string.Equals(ReaderType, "Student", StringComparison.OrdinalIgnoreCase);

        public bool IsExternal => string.Equals(ReaderType, "External", StringComparison.OrdinalIgnoreCase);
    }
}
