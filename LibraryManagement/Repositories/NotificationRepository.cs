using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private const string Columns = "Id, Title, Message, Type, SourceModule, SourceEntityType, SourceEntityId, CreatedAt, IsRead";

    public async Task<Notification?> AddAsync(Notification notification, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"INSERT INTO dbo.Notifications
            (Title, Message, Type, SourceModule, SourceEntityType, SourceEntityId, IdempotencyKey, CreatedAt, IsRead)
            OUTPUT INSERTED.Id
            VALUES (@Title, @Message, @Type, @SourceModule, @SourceEntityType, @SourceEntityId, @IdempotencyKey, @CreatedAt, 0)", connection);
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = notification.Title;
        command.Parameters.Add("@Message", SqlDbType.NVarChar, 1000).Value = notification.Message;
        command.Parameters.Add("@Type", SqlDbType.Int).Value = (int)notification.Type;
        command.Parameters.Add("@SourceModule", SqlDbType.NVarChar, 40).Value = notification.SourceModule;
        command.Parameters.Add("@SourceEntityType", SqlDbType.NVarChar, 80).Value = (object?)notification.SourceEntityType ?? DBNull.Value;
        command.Parameters.Add("@SourceEntityId", SqlDbType.NVarChar, 100).Value = (object?)notification.SourceEntityId ?? DBNull.Value;
        command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 200).Value = (object?)idempotencyKey ?? DBNull.Value;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = notification.CreatedAt;
        int id;
        try { id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)); }
        catch (SqlException exception) when (idempotencyKey != null && exception.Number is 2601 or 2627) { return null; }
        return new Notification
        {
            Id = id, Title = notification.Title, Message = notification.Message, Type = notification.Type,
            SourceModule = notification.SourceModule, SourceEntityType = notification.SourceEntityType,
            SourceEntityId = notification.SourceEntityId, IdempotencyKey = idempotencyKey, CreatedAt = notification.CreatedAt, IsRead = false
        };
    }

    public Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default) => ListAsync(false, cancellationToken);
    public Task<IReadOnlyList<Notification>> GetUnreadAsync(CancellationToken cancellationToken = default) => ListAsync(true, cancellationToken);

    public async Task<NotificationPage> GetPageAsync(NotificationPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string whereClause = query.UnreadOnly ? " WHERE IsRead = 0" : string.Empty;
        var items = new List<Notification>(query.PageSize);
        int totalCount;

        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var countCommand = new SqlCommand(
            $"SELECT COUNT_BIG(*) FROM dbo.Notifications{whereClause}", connection))
        {
            totalCount = checked(Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken)));
        }

        int pageNumber = query.GetEffectivePageNumber(totalCount);
        int offset = checked((pageNumber - 1) * query.PageSize);
        string pageSql = $@"SELECT {Columns}
            FROM dbo.Notifications AS n{whereClause}
            ORDER BY {NotificationPageQuery.StableOrderBySql}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        await using (var pageCommand = new SqlCommand(pageSql, connection))
        {
            pageCommand.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            pageCommand.Parameters.Add("@PageSize", SqlDbType.Int).Value = query.PageSize;

            await using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(MapNotification(reader));
        }

        return new NotificationPage
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = query.PageSize
        };
    }

    private static async Task<IReadOnlyList<Notification>> ListAsync(bool unreadOnly, CancellationToken cancellationToken)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        string sql = $"SELECT {Columns} FROM dbo.Notifications" +
                     (unreadOnly ? " WHERE IsRead = 0" : "") + " ORDER BY CreatedAt DESC, Id DESC";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<Notification>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapNotification(reader));
        return results;
    }

    private static Notification MapNotification(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0), Title = reader.GetString(1), Message = reader.GetString(2),
        Type = (NotificationType)reader.GetInt32(3), SourceModule = reader.GetString(4),
        SourceEntityType = reader.IsDBNull(5) ? null : reader.GetString(5),
        SourceEntityId = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = reader.GetDateTime(7), IsRead = reader.GetBoolean(8)
    };

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Notifications WHERE IsRead = 0", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.Notifications SET IsRead = 1 WHERE Id = @Id AND IsRead = 0", connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.Notifications SET IsRead = 1 WHERE IsRead = 0", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
