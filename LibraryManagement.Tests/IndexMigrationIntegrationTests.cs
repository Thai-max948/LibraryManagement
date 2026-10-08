using LibraryManagement.Data;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class IndexMigrationIntegrationTests
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void FreshSchemaDefinesBookAndReaderIndexes_AndHistoryMigrationIsIdempotent()
    {
        using var fixture = new SqlIntegrationFixture();
        using var connection = OpenConnection();

        AssertIndexDefinition(connection, "dbo.BookCopies", "IX_BookCopies_BookId_Status",
            new[] { "BookId", "Status" });
        AssertIndexDefinition(connection, "dbo.BorrowRecords", "IX_BorrowRecords_ReaderId_Status",
            new[] { "ReaderId", "Status" }, new[] { "DueDate" });

        using (var dropIndex = new SqlCommand(
            "DROP INDEX IX_BorrowRecords_ReaderId_Status ON dbo.BorrowRecords;", connection))
            dropIndex.ExecuteNonQuery();

        HistorySchemaMigration.Apply();
        HistorySchemaMigration.Apply();

        AssertIndexDefinition(connection, "dbo.BorrowRecords", "IX_BorrowRecords_ReaderId_Status",
            new[] { "ReaderId", "Status" }, new[] { "DueDate" });
        Assert.Equal(1, CountIndex(connection, "dbo.BorrowRecords", "IX_BorrowRecords_ReaderId_Status"));

        foreach (string existingIndex in new[]
        {
            "IX_BorrowRecords_HistoryBorrowDate",
            "IX_BorrowRecords_HistoryReturnDate",
            "IX_BorrowRecords_ActiveDueDate",
            "UX_BorrowRecords_ActiveCopy"
        })
        {
            Assert.Equal(1, CountIndex(connection, "dbo.BorrowRecords", existingIndex));
        }
    }

    private static SqlConnection OpenConnection()
    {
        var connection = Database.GetConnection();
        connection.Open();
        return connection;
    }

    private static void AssertIndexDefinition(SqlConnection connection, string tableName, string indexName,
        IReadOnlyList<string> expectedKeys, IReadOnlyList<string>? expectedIncludes = null)
    {
        using var command = new SqlCommand(@"
            SELECT ix.is_unique, ix.has_filter, ix.filter_definition,
                   c.name, ic.key_ordinal, ic.is_included_column, ic.is_descending_key
            FROM sys.indexes AS ix
            INNER JOIN sys.index_columns AS ic
                ON ic.object_id = ix.object_id AND ic.index_id = ix.index_id
            INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ix.object_id = OBJECT_ID(@TableName) AND ix.name = @IndexName
            ORDER BY ic.is_included_column, ic.key_ordinal, c.column_id;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@IndexName", indexName);

        var columns = new List<(string Name, int KeyOrdinal, bool IsIncluded, bool IsDescending,
            bool IsUnique, bool HasFilter, string? FilterDefinition)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add((
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(0),
                    reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        Assert.NotEmpty(columns);
        Assert.All(columns, column =>
        {
            Assert.False(column.IsUnique);
            Assert.False(column.HasFilter);
            Assert.Null(column.FilterDefinition);
        });

        var keyColumns = columns.Where(column => !column.IsIncluded)
            .OrderBy(column => column.KeyOrdinal).ToList();
        Assert.Equal(expectedKeys, keyColumns.Select(column => column.Name));
        Assert.All(keyColumns, column => Assert.False(column.IsDescending));

        var includeColumns = columns.Where(column => column.IsIncluded)
            .Select(column => column.Name).ToList();
        Assert.Equal(expectedIncludes ?? Array.Empty<string>(), includeColumns);
    }

    private static int CountIndex(SqlConnection connection, string tableName, string indexName)
    {
        using var command = new SqlCommand(@"
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID(@TableName) AND name = @IndexName;", connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@IndexName", indexName);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
