using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services;

public sealed class FeeFinancialStandingProvider : IReaderFinancialStandingProvider
{
    private readonly FeeRepository _fees;
    public FeeFinancialStandingProvider() : this(new FeeRepository()) { }
    public FeeFinancialStandingProvider(FeeRepository fees) => _fees = fees;

    public ReaderFinancialStanding GetStanding(int readerId)
    {
        decimal balance = _fees.GetOutstandingBalance(readerId);
        return new ReaderFinancialStanding { OutstandingAmount = balance, BlocksBorrowing = balance > 0m };
    }

    public ReaderFinancialStanding GetStanding(SqlConnection connection, SqlTransaction transaction, int readerId)
    {
        decimal balance = _fees.GetOutstandingBalance(connection, transaction, readerId);
        return new ReaderFinancialStanding { OutstandingAmount = balance, BlocksBorrowing = balance > 0m };
    }
}
