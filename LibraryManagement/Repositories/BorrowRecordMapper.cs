using System.Data.Common;
using LibraryManagement.Models;

namespace LibraryManagement.Repositories;

internal static class BorrowRecordMapper
{
    internal static BorrowRecord Map(DbDataReader reader)
    {
        int returnDateOrdinal = reader.GetOrdinal("ReturnDate");
        return new BorrowRecord
        {
            BorrowId = reader.GetInt32(reader.GetOrdinal("BorrowId")),
            BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
            BookCopyId = reader.IsDBNull(reader.GetOrdinal("BookCopyId")) ? null : reader.GetInt32(reader.GetOrdinal("BookCopyId")),
            ReaderId = reader.GetInt32(reader.GetOrdinal("ReaderId")),
            BorrowDate = reader.GetDateTime(reader.GetOrdinal("BorrowDate")),
            DueDate = reader.GetDateTime(reader.GetOrdinal("DueDate")),
            LoanPeriodDaysApplied = reader.IsDBNull(reader.GetOrdinal("LoanPeriodDaysApplied")) ? null : reader.GetInt32(reader.GetOrdinal("LoanPeriodDaysApplied")),
            ReturnCondition = reader.IsDBNull(reader.GetOrdinal("ReturnCondition")) ? null : reader.GetString(reader.GetOrdinal("ReturnCondition")),
            ConditionNote = reader.IsDBNull(reader.GetOrdinal("ConditionNote")) ? null : reader.GetString(reader.GetOrdinal("ConditionNote")),
            LostDate = reader.IsDBNull(reader.GetOrdinal("LostDate")) ? null : reader.GetDateTime(reader.GetOrdinal("LostDate")),
            LostNote = reader.IsDBNull(reader.GetOrdinal("LostNote")) ? null : reader.GetString(reader.GetOrdinal("LostNote")),
            ReturnDate = reader.IsDBNull(returnDateOrdinal) ? (DateTime?)null : reader.GetDateTime(returnDateOrdinal),
            Status = reader.GetString(reader.GetOrdinal("Status"))
        };
    }
}
