namespace LibraryManagement.Models;

public sealed class LoanPolicy
{
    public int LoanPolicyId { get; set; }
    public string ReaderType { get; set; } = string.Empty;
    public int LoanPeriodDays { get; set; }
    public bool IsActive { get; set; }
}
