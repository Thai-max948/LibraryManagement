using System.Globalization;

namespace LibraryManagement.ViewModels;

// Keeps one operation id across retries of unchanged payment input, then releases it
// after success so a later payment with the same amount becomes a new operation.
public sealed class FeePaymentAttemptKey
{
    private string? _signature;
    private Guid _id;

    public Guid GetOrCreate(int feeId, decimal amount, string? note)
    {
        string signature = $"{feeId}|{amount.ToString(CultureInfo.InvariantCulture)}|{note?.Trim() ?? string.Empty}";
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            _id = Guid.NewGuid();
        }
        return _id;
    }

    public void Complete()
    {
        _signature = null;
        _id = Guid.Empty;
    }
}
