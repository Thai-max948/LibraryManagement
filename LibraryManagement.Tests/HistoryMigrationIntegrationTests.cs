using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Tests;

public sealed class HistoryMigrationIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void LegacyReturnWithoutRecordedConditionIsBackfilledAsGenericReturnOnce()
    {
        ReturnOutcomeMigration.Apply();
        CirculationAuditMigration.Apply();
        HistorySchemaMigration.Apply();

        int readerId = new ReaderService().AddReader(new Reader
        {
            FullName = "Legacy event reader",
            StudentId = "HIST-LEGACY-" + Guid.NewGuid().ToString("N"),
            Phone = "0901234567"
        });
        int bookId = new BookService().AddBook(new Book
        {
            Title = "Legacy return event",
            Author = "History test",
            PublishYear = 2026,
            Quantity = 0
        });

        int borrowId;
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using (var resetBackfill = new SqlCommand(@"
                DELETE FROM dbo.HistoryMigrationState
                WHERE MigrationKey = @MigrationKey;", connection))
            {
                resetBackfill.Parameters.AddWithValue("@MigrationKey", "HistoryCirculationFactsV1");
                resetBackfill.ExecuteNonQuery();
            }

            using var command = new SqlCommand(@"
                INSERT INTO dbo.BorrowRecords (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status)
                VALUES (@BookId, @ReaderId, DATEADD(day, -5, GETDATE()), DATEADD(day, -1, GETDATE()), GETDATE(), 'Returned');
                SELECT CAST(SCOPE_IDENTITY() AS int);", connection);
            command.Parameters.AddWithValue("@BookId", bookId);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            borrowId = Convert.ToInt32(command.ExecuteScalar());
        }

        HistorySchemaMigration.Apply();

        var history = new HistoryService();
        var firstRead = Assert.Single(history.GetPage(new HistoryQuery { SearchText = "Legacy return event" }).Records);
        Assert.Equal(borrowId, firstRead.BorrowId);
        Assert.Collection(firstRead.Events,
            item => Assert.Equal(CirculationAuditEventType.BorrowCreated, item.EventType),
            item =>
            {
                Assert.Equal(CirculationAuditEventType.Returned, item.EventType);
                Assert.Equal("Legacy / Unknown", item.ActorNameSnapshot);
                Assert.Null(item.ActorUserId);
            });

        var secondRead = Assert.Single(history.GetPage(new HistoryQuery { SearchText = "Legacy return event" }).Records);
        Assert.Equal(2, secondRead.Events.Count);
        Assert.DoesNotContain(secondRead.Events, item => item.EventType == CirculationAuditEventType.ReturnedNormal);
    }
}
