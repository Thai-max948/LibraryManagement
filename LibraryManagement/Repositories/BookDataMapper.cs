using System;
using System.Data.Common;
using LibraryManagement.Models;

namespace LibraryManagement.Repositories;

internal static class BookDataMapper
{
    internal static Book Map(DbDataReader reader)
    {
        int categoryOrdinal = reader.GetOrdinal("Category");
        return new Book
        {
            BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Author = reader.GetString(reader.GetOrdinal("Author")),
            Category = reader.IsDBNull(categoryOrdinal) ? string.Empty : reader.GetString(categoryOrdinal),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            Language = reader.IsDBNull(reader.GetOrdinal("Language")) ? null : reader.GetString(reader.GetOrdinal("Language")),
            Isbn = reader.IsDBNull(reader.GetOrdinal("ISBN")) ? null : reader.GetString(reader.GetOrdinal("ISBN")),
            ReplacementValue = reader.IsDBNull(reader.GetOrdinal("ReplacementValue")) ? null : reader.GetDecimal(reader.GetOrdinal("ReplacementValue")),
            RentalPrice = reader.IsDBNull(reader.GetOrdinal("RentalPrice")) ? null : reader.GetDecimal(reader.GetOrdinal("RentalPrice")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            ArchivedAt = reader.IsDBNull(reader.GetOrdinal("ArchivedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ArchivedAt")),
            CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            PublishYear = reader.GetInt32(reader.GetOrdinal("PublishYear")),
            Quantity = reader.GetInt32(reader.GetOrdinal("Quantity")),
            AvailableQuantity = reader.GetInt32(reader.GetOrdinal("AvailableQuantity")),
            BorrowedCopies = HasColumn(reader, "BorrowedCopies") ? reader.GetInt32(reader.GetOrdinal("BorrowedCopies")) : null
        };
    }

    private static bool HasColumn(DbDataReader reader, string name)
    {
        for (int index = 0; index < reader.FieldCount; index++)
            if (string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
