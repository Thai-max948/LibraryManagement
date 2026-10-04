using System;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services;

public sealed class LoanPolicyService
{
    private readonly LoanPolicyRepository _repository;

    public LoanPolicyService() : this(new LoanPolicyRepository()) { }
    public LoanPolicyService(LoanPolicyRepository repository) => _repository = repository;

    public LoanPolicy GetPolicyFor(Reader reader) => Validate(_repository.GetActiveByReaderType(reader.ReaderType), reader.ReaderType);

    public LoanPolicy GetPolicyFor(SqlConnection connection, SqlTransaction transaction, string readerType) =>
        Validate(_repository.GetActiveByReaderType(connection, transaction, readerType), readerType);

    public static DateTime CalculateDueDate(LoanPolicy policy, DateTime borrowDate)
    {
        if (policy.LoanPeriodDays <= 0)
            throw new BusinessRuleException("Số ngày mượn trong chính sách phải lớn hơn 0.");
        return borrowDate.Date.AddDays(policy.LoanPeriodDays);
    }

    private static LoanPolicy Validate(LoanPolicy? policy, string readerType)
    {
        if (policy == null)
            throw new BusinessRuleException($"Chưa cấu hình chính sách mượn cho loại độc giả {readerType}.");
        if (policy.LoanPeriodDays <= 0)
            throw new BusinessRuleException("Số ngày mượn trong chính sách phải lớn hơn 0.");
        return policy;
    }
}
