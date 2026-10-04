using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services;

public interface IReaderFinancialStandingProvider
{
    ReaderFinancialStanding GetStanding(int readerId);
    ReaderFinancialStanding GetStanding(SqlConnection connection, SqlTransaction transaction, int readerId);
}

// The Fee module is not present yet. Replace this adapter when it is added.
public sealed class NoFinancialStandingProvider : IReaderFinancialStandingProvider
{
    public ReaderFinancialStanding GetStanding(int readerId) => new();
    public ReaderFinancialStanding GetStanding(SqlConnection connection, SqlTransaction transaction, int readerId) => new();
}
