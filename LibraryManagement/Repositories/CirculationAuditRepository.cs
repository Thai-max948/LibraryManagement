using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

public class CirculationAuditRepository
{
    public virtual void Add(SqlConnection connection, SqlTransaction transaction, CirculationAuditEvent auditEvent)
    {
        using var command = new SqlCommand(@"
            INSERT INTO dbo.CirculationAuditEvents
                (BorrowId, EventType, ActorUserId, ActorNameSnapshot, OccurredAt, BookCopyId, Note)
            OUTPUT INSERTED.AuditEventId VALUES
                (@BorrowId, @EventType, @ActorUserId, @ActorNameSnapshot, @OccurredAt, @BookCopyId, @Note)",
            connection, transaction);
        command.Parameters.AddWithValue("@BorrowId", auditEvent.BorrowId);
        command.Parameters.AddWithValue("@EventType", auditEvent.EventType.ToString());
        command.Parameters.AddWithValue("@ActorUserId", (object?)auditEvent.ActorUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ActorNameSnapshot", auditEvent.ActorNameSnapshot);
        command.Parameters.AddWithValue("@OccurredAt", auditEvent.OccurredAt);
        command.Parameters.AddWithValue("@BookCopyId", (object?)auditEvent.BookCopyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Note", (object?)auditEvent.Note ?? DBNull.Value);
        auditEvent.AuditEventId = Convert.ToInt64(command.ExecuteScalar());
    }

    public virtual List<CirculationAuditEvent> GetByBorrowIds(IEnumerable<int> borrowIds)
    {
        var ids = borrowIds.Distinct().ToArray();
        if (ids.Length == 0) return new List<CirculationAuditEvent>();
        using var connection = Database.GetConnection();
        connection.Open();
        var parameters = new List<string>();
        using var command = new SqlCommand { Connection = connection };
        for (int index = 0; index < ids.Length; index++)
        {
            string name = "@BorrowId" + index;
            parameters.Add(name);
            command.Parameters.AddWithValue(name, ids[index]);
        }
        command.CommandText = $@"
            SELECT AuditEventId, BorrowId, EventType, ActorUserId, ActorNameSnapshot,
                OccurredAt, BookCopyId, Note
            FROM dbo.CirculationAuditEvents
            WHERE BorrowId IN ({string.Join(",", parameters)})
            ORDER BY BorrowId, OccurredAt, AuditEventId";
        var events = new List<CirculationAuditEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) events.Add(Map(reader));
        return events;
    }

    private static CirculationAuditEvent Map(SqlDataReader reader) => new()
    {
        AuditEventId = reader.GetInt64(0),
        BorrowId = reader.GetInt32(1),
        EventType = Enum.Parse<CirculationAuditEventType>(reader.GetString(2)),
        ActorUserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        ActorNameSnapshot = reader.GetString(4),
        OccurredAt = reader.GetDateTime(5),
        BookCopyId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        Note = reader.IsDBNull(7) ? null : reader.GetString(7)
    };
}
