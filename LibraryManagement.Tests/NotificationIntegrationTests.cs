using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using System.Data;

namespace LibraryManagement.Tests;

public sealed class NotificationIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task MigrationAndRepositoryPersistReadAndUpdateNotifications()
    {
        NotificationMigration.Apply();
        NotificationMigration.Apply();
        var service = new NotificationService(new NotificationRepository());
        var first = await service.NotifyAsync("First", "Older", NotificationType.Info, "Book", "Book", "1");
        var second = await service.NotifyAsync("Second", "Newer", NotificationType.Success, "Reader", "Reader", "2");
        Assert.NotNull(first);
        Assert.NotNull(second);

        var all = await service.GetAllAsync();
        Assert.Equal(new[] { second.Id, first.Id }, all.Select(item => item.Id));
        Assert.Equal(2, await service.GetUnreadCountAsync());
        Assert.Equal(2, (await service.GetUnreadAsync()).Count);

        await service.MarkAsReadAsync(first.Id);
        Assert.Equal(second.Id, Assert.Single(await service.GetUnreadAsync()).Id);
        await service.MarkAllAsReadAsync();
        Assert.Equal(0, await service.GetUnreadCountAsync());
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task DueSoonIdempotencyIsProtectedByFilteredUniqueIndexAndAllowsNewDueDate()
    {
        NotificationMigration.Apply();
        var service = new NotificationService(new NotificationRepository());
        var first = await service.NotifyAsync("Due soon", "Old due date", NotificationType.DueSoon,
            "Borrow", "Borrow", "876543", idempotencyKey: "DueSoon:876543:2026-10-06");
        var duplicate = await service.NotifyAsync("Due soon", "Duplicate", NotificationType.DueSoon,
            "Borrow", "Borrow", "876543", idempotencyKey: "DueSoon:876543:2026-10-06");
        var renewed = await service.NotifyAsync("Due soon", "New due date", NotificationType.DueSoon,
            "Borrow", "Borrow", "876543", idempotencyKey: "DueSoon:876543:2026-10-13");

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(renewed);
        Assert.NotEqual(first.Id, renewed.Id);
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task BorrowRepositoryReturnsOnlyActiveLoansForCalendarDayIncludingTimeComponents()
    {
        var target = new DateTime(2026, 10, 6);
        var ids = new List<int>();
        await using (var connection = Database.GetConnection())
        {
            await connection.OpenAsync();
            foreach (var dueDate in new[] { target.AddHours(8), target.AddHours(23).AddMinutes(30), target.AddHours(18) })
            {
                await using var insert = new SqlCommand(@"INSERT INTO dbo.BorrowRecords
                    (BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status)
                    OUTPUT INSERTED.BorrowId
                    SELECT TOP (1) b.BookId, r.ReaderId, @BorrowDate, @DueDate, @ReturnDate, @Status
                    FROM dbo.Books b CROSS JOIN dbo.Readers r ORDER BY b.BookId, r.ReaderId", connection);
                insert.Parameters.Add("@BorrowDate", SqlDbType.DateTime).Value = target.AddDays(-7);
                insert.Parameters.Add("@DueDate", SqlDbType.DateTime).Value = dueDate;
                insert.Parameters.Add("@ReturnDate", SqlDbType.DateTime).Value = dueDate == target.AddHours(18) ? target : DBNull.Value;
                insert.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = dueDate == target.AddHours(18) ? "Returned" : "Borrowing";
                ids.Add(Convert.ToInt32(await insert.ExecuteScalarAsync()));
            }
        }

        try
        {
            var loans = await new BorrowRepository().GetActiveLoansDueOnAsync(target);
            Assert.Equal(2, loans.Count(item => ids.Take(2).Contains(item.BorrowId)));
            Assert.DoesNotContain(loans, item => item.BorrowId == ids[2]);
        }
        finally
        {
            await using var connection = Database.GetConnection();
            await connection.OpenAsync();
            await using var delete = new SqlCommand("DELETE FROM dbo.BorrowRecords WHERE BorrowId IN (@First, @Second, @Third)", connection);
            delete.Parameters.Add("@First", SqlDbType.Int).Value = ids[0];
            delete.Parameters.Add("@Second", SqlDbType.Int).Value = ids[1];
            delete.Parameters.Add("@Third", SqlDbType.Int).Value = ids[2];
            await delete.ExecuteNonQueryAsync();
        }
    }

    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentDuplicateDueSoonWritesCreateOneRow()
    {
        NotificationMigration.Apply();
        var service = new NotificationService(new NotificationRepository());
        string key = "DueSoon:765432:2026-10-06";
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            service.NotifyAsync("Due soon", $"Attempt {index}", NotificationType.DueSoon,
                "Borrow", "Borrow", "765432", idempotencyKey: key)));

        Assert.Single(writes, item => item != null);
        Assert.Equal(1, await CountByIdempotencyKeyAsync(key));
    }

    private static async Task<int> CountByIdempotencyKeyAsync(string key)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Notifications WHERE IdempotencyKey = @Key", connection);
        command.Parameters.Add("@Key", SqlDbType.NVarChar, 200).Value = key;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
