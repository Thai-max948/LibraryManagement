using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class LoanPolicyRepository
{
    public virtual LoanPolicy? GetActiveByReaderType(string readerType)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        return GetActiveByReaderType(connection, null, readerType);
    }

    public virtual LoanPolicy? GetActiveByReaderType(SqlConnection connection, SqlTransaction? transaction, string readerType)
    {
        using var command = new SqlCommand(@"
            SELECT LoanPolicyId, ReaderType, LoanPeriodDays, IsActive
            FROM dbo.LoanPolicies WHERE ReaderType = @ReaderType AND IsActive = 1", connection, transaction);
        command.Parameters.AddWithValue("@ReaderType", readerType);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new LoanPolicy
        {
            LoanPolicyId = reader.GetInt32(0),
            ReaderType = reader.GetString(1),
            LoanPeriodDays = reader.GetInt32(2),
            IsActive = reader.GetBoolean(3)
        } : null;
    }
}
