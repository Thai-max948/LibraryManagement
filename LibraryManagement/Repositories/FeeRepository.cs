using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories;

// Persistence only. All state-changing calls require the caller's transaction and are
// reached through FeeService commands; there is intentionally no delete or generic update.
public class FeeRepository
{
    private const string Columns = "FeeId, BorrowId, ReaderId, BookCopyId, FeeType, Amount, PaidAmount, Status, Reason, Description, SourceType, SourceId, ReaderNameSnapshot, BookTitleSnapshot, BarcodeSnapshot, BookPriceSnapshot, CreatedAt, CreatedBy, UpdatedAt, PaidAt, RentalPriceSnapshot, LateDays, AppliedRate, DamageLevel, WaivedAt, WaivedBy, WaiveReason, CancelledAt, CancelledBy, CancelReason, AppliedCapRate, BaseAmount, Units, CapAmount, UncappedAmount, WaivedAmount, DueDateSnapshot, ResolvedAtSnapshot";

    public virtual async Task<Fee?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.Fees WHERE FeeId = @Id", connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public virtual Task<IReadOnlyList<Fee>> GetByReaderIdAsync(int readerId, CancellationToken cancellationToken = default) =>
        ListByIdAsync("ReaderId", readerId, cancellationToken);

    public virtual Task<IReadOnlyList<Fee>> GetByBorrowIdAsync(int borrowId, CancellationToken cancellationToken = default) =>
        ListByIdAsync("BorrowId", borrowId, cancellationToken);

    private static async Task<IReadOnlyList<Fee>> ListByIdAsync(string column, int id, CancellationToken cancellationToken)
    {
        // column is selected only from the two constant call sites above.
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.Fees WHERE {column} = @Id ORDER BY CreatedAt DESC, FeeId DESC", connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var fees = new List<Fee>();
        while (await reader.ReadAsync(cancellationToken)) fees.Add(Map(reader));
        return fees;
    }

    public virtual async Task<FeePage> GetPageAsync(FeeSearchQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        var filter = BuildFilter(query, out var parameters);
        int total;
        await using (var count = new SqlCommand($"SELECT COUNT_BIG(*) FROM dbo.Fees {filter}", connection))
        {
            AddFilters(count, query, parameters);
            total = checked((int)Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken)));
        }

        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int page = Math.Clamp(Math.Max(1, query.Page), 1, int.MaxValue / pageSize);
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.Fees {filter} ORDER BY CreatedAt DESC, FeeId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", connection);
        AddFilters(command, query, parameters);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var fees = new List<Fee>();
        while (await reader.ReadAsync(cancellationToken)) fees.Add(Map(reader));
        return new FeePage(fees, total, page, pageSize);
    }

    private static string BuildFilter(FeeSearchQuery query, out string? search)
    {
        search = string.IsNullOrWhiteSpace(query.SearchText) ? null : $"%{query.SearchText.Trim()}%";
        return @"WHERE (@Status IS NULL OR Status = @Status)
            AND (@FeeType IS NULL OR FeeType = @FeeType)
            AND (@Search IS NULL OR ReaderNameSnapshot LIKE @Search OR BookTitleSnapshot LIKE @Search
                 OR COALESCE(BarcodeSnapshot, N'') LIKE @Search OR CONVERT(nvarchar(20), FeeId) LIKE @Search
                 OR CONVERT(nvarchar(20), BorrowId) LIKE @Search OR CONVERT(nvarchar(20), ReaderId) LIKE @Search)";
    }

    private static void AddFilters(SqlCommand command, FeeSearchQuery query, string? search)
    {
        command.Parameters.Add("@Status", SqlDbType.Int).Value = query.Status.HasValue ? (int)query.Status.Value : DBNull.Value;
        command.Parameters.Add("@FeeType", SqlDbType.Int).Value = query.FeeType.HasValue ? (int)query.FeeType.Value : DBNull.Value;
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 300).Value = (object?)search ?? DBNull.Value;
    }

    public virtual async Task<decimal> GetOutstandingBalanceAsync(int readerId, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetOutstandingBalanceAsync(connection, null, readerId, cancellationToken);
    }

    public virtual async Task<decimal> GetTotalOutstandingBalanceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"SELECT COALESCE(SUM(Amount - PaidAmount), 0)
            FROM dbo.Fees WHERE Status IN (1,2)", connection);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
    }

    public virtual async Task<decimal> GetOutstandingBalanceAsync(SqlConnection connection, SqlTransaction? transaction,
        int readerId, CancellationToken cancellationToken = default)
    {
        await using var command = new SqlCommand(@"SELECT COALESCE(SUM(Amount - PaidAmount), 0)
            FROM dbo.Fees WHERE ReaderId = @ReaderId AND Status IN (1,2)", connection, transaction);
        command.Parameters.Add("@ReaderId", SqlDbType.Int).Value = readerId;
        return Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
    }

    // Synchronous variant is used by existing synchronous BorrowService transaction checks.
    public virtual decimal GetOutstandingBalance(SqlConnection connection, SqlTransaction transaction, int readerId)
    {
        using var command = new SqlCommand(@"SELECT COALESCE(SUM(Amount - PaidAmount), 0)
            FROM dbo.Fees WHERE ReaderId = @ReaderId AND Status IN (1,2)", connection, transaction);
        command.Parameters.Add("@ReaderId", SqlDbType.Int).Value = readerId;
        return Convert.ToDecimal(command.ExecuteScalar());
    }

    public virtual decimal GetOutstandingBalance(int readerId)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"SELECT COALESCE(SUM(Amount - PaidAmount), 0)
            FROM dbo.Fees WHERE ReaderId = @ReaderId AND Status IN (1,2)", connection);
        command.Parameters.Add("@ReaderId", SqlDbType.Int).Value = readerId;
        return Convert.ToDecimal(command.ExecuteScalar());
    }

    internal virtual FeeSourceSnapshot? GetSourceSnapshot(SqlConnection connection, SqlTransaction transaction, int borrowId)
    {
        using var command = new SqlCommand(@"SELECT br.ReaderId, br.CopyId, rd.FullName, b.Title, bc.Barcode,
                   b.ReplacementValue, b.RentalPrice
            FROM dbo.BorrowRecords br WITH (HOLDLOCK)
            INNER JOIN dbo.Readers rd ON rd.ReaderId = br.ReaderId
            INNER JOIN dbo.Books b ON b.BookId = br.BookId
            LEFT JOIN dbo.BookCopies bc ON bc.CopyId = br.CopyId AND bc.BookId = br.BookId
            WHERE br.BorrowId = @BorrowId", connection, transaction);
        command.Parameters.Add("@BorrowId", SqlDbType.Int).Value = borrowId;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new FeeSourceSnapshot(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6));
    }

    internal virtual Fee Create(SqlConnection connection, SqlTransaction transaction, Fee fee)
    {
        using var command = BuildInsert(connection, transaction, fee);
        int id = Convert.ToInt32(command.ExecuteScalar());
        return fee with { FeeId = id };
    }

    internal virtual async Task<Fee> CreateAsync(SqlConnection connection, SqlTransaction transaction, Fee fee,
        CancellationToken cancellationToken = default)
    {
        await using var command = BuildInsert(connection, transaction, fee);
        int id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return fee with { FeeId = id };
    }

    private static SqlCommand BuildInsert(SqlConnection connection, SqlTransaction transaction, Fee fee)
    {
        var command = new SqlCommand(@"INSERT INTO dbo.Fees
            (BorrowId, ReaderId, BookCopyId, FeeType, Amount, PaidAmount, Status, Reason, Description,
             SourceType, SourceId, ReaderNameSnapshot, BookTitleSnapshot, BarcodeSnapshot, BookPriceSnapshot,
             RentalPriceSnapshot, LateDays, AppliedRate, AppliedCapRate, DamageLevel,
             BaseAmount, Units, CapAmount, UncappedAmount, CreatedAt, CreatedBy, UpdatedAt, PaidAt,
             DueDateSnapshot, ResolvedAtSnapshot)
            OUTPUT INSERTED.FeeId VALUES
            (@BorrowId,@ReaderId,@BookCopyId,@FeeType,@Amount,@PaidAmount,@Status,@Reason,@Description,
             @SourceType,@SourceId,@ReaderName,@BookTitle,@Barcode,@BookPrice,@RentalPrice,@LateDays,
             @AppliedRate,@AppliedCapRate,@DamageLevel,@BaseAmount,@Units,@CapAmount,@UncappedAmount,
             @CreatedAt,@CreatedBy,NULL,NULL,@DueDateSnapshot,@ResolvedAtSnapshot)", connection, transaction);
        command.Parameters.Add("@BorrowId", SqlDbType.Int).Value = fee.BorrowId;
        command.Parameters.Add("@ReaderId", SqlDbType.Int).Value = fee.ReaderId;
        command.Parameters.Add("@BookCopyId", SqlDbType.Int).Value = (object?)fee.BookCopyId ?? DBNull.Value;
        command.Parameters.Add("@FeeType", SqlDbType.Int).Value = (int)fee.FeeType;
        AddMoney(command, "@Amount", fee.Amount);
        AddMoney(command, "@PaidAmount", fee.PaidAmount);
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)fee.Status;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = fee.Reason;
        command.Parameters.Add("@Description", SqlDbType.NVarChar, 1000).Value = (object?)fee.Description ?? DBNull.Value;
        command.Parameters.Add("@SourceType", SqlDbType.NVarChar, 100).Value = fee.SourceType;
        command.Parameters.Add("@SourceId", SqlDbType.NVarChar, 200).Value = fee.SourceId;
        command.Parameters.Add("@ReaderName", SqlDbType.NVarChar, 150).Value = fee.ReaderNameSnapshot;
        command.Parameters.Add("@BookTitle", SqlDbType.NVarChar, 255).Value = fee.BookTitleSnapshot;
        command.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = (object?)fee.BarcodeSnapshot ?? DBNull.Value;
        AddMoney(command, "@BookPrice", fee.BookPriceSnapshot);
        AddMoney(command, "@RentalPrice", fee.RentalPriceSnapshot);
        command.Parameters.Add("@LateDays", SqlDbType.Int).Value = (object?)fee.LateDays ?? DBNull.Value;
        var rate = command.Parameters.Add("@AppliedRate", SqlDbType.Decimal);
        rate.Precision = 9; rate.Scale = 6; rate.Value = (object?)fee.AppliedRate ?? DBNull.Value;
        var capRate = command.Parameters.Add("@AppliedCapRate", SqlDbType.Decimal);
        capRate.Precision = 9; capRate.Scale = 6; capRate.Value = (object?)fee.AppliedCapRate ?? DBNull.Value;
        command.Parameters.Add("@DamageLevel", SqlDbType.NVarChar, 30).Value = (object?)fee.DamageLevel ?? DBNull.Value;
        AddMoney(command, "@BaseAmount", fee.BaseAmount);
        var units = command.Parameters.Add("@Units", SqlDbType.Decimal);
        units.Precision = 18; units.Scale = 4; units.Value = (object?)fee.Units ?? DBNull.Value;
        AddMoney(command, "@CapAmount", fee.CapAmount);
        AddMoney(command, "@UncappedAmount", fee.UncappedAmount);
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = fee.CreatedAt;
        command.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = (object?)fee.CreatedBy ?? DBNull.Value;
        command.Parameters.Add("@DueDateSnapshot", SqlDbType.DateTime2).Value = (object?)fee.DueDateSnapshot ?? DBNull.Value;
        command.Parameters.Add("@ResolvedAtSnapshot", SqlDbType.DateTime2).Value = (object?)fee.ResolvedAtSnapshot ?? DBNull.Value;
        return command;
    }

    internal virtual Fee? GetForUpdate(SqlConnection connection, SqlTransaction transaction, int feeId)
    {
        using var command = new SqlCommand($"SELECT {Columns} FROM dbo.Fees WITH (UPDLOCK, ROWLOCK) WHERE FeeId = @FeeId", connection, transaction);
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = feeId;
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    internal virtual FeePayment? GetPaymentByIdempotencyKey(SqlConnection connection, SqlTransaction transaction, Guid key)
    {
        using var command = new SqlCommand(@"SELECT PaymentId, FeeId, Amount, PaidAt, RecordedBy, Note, IdempotencyKey
            FROM dbo.FeePayments WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = @Key", connection, transaction);
        command.Parameters.Add("@Key", SqlDbType.UniqueIdentifier).Value = key;
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPayment(reader) : null;
    }

    public virtual async Task<IReadOnlyList<FeePayment>> GetPaymentHistoryAsync(int feeId, CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(@"SELECT PaymentId, FeeId, Amount, PaidAt, RecordedBy, Note, IdempotencyKey
            FROM dbo.FeePayments WHERE FeeId = @FeeId ORDER BY PaidAt, PaymentId", connection);
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = feeId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var payments = new List<FeePayment>();
        while (await reader.ReadAsync(cancellationToken)) payments.Add(MapPayment(reader));
        return payments;
    }

    internal virtual FeePayment InsertPayment(SqlConnection connection, SqlTransaction transaction,
        RecordFeePaymentRequest request, int? actorId, DateTime paidAt)
    {
        using var command = new SqlCommand(@"INSERT INTO dbo.FeePayments(FeeId, Amount, PaidAt, RecordedBy, Note, IdempotencyKey)
            OUTPUT INSERTED.PaymentId VALUES(@FeeId, @Amount, @PaidAt, @ActorId, @Note, @Key)", connection, transaction);
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = request.FeeId;
        AddMoney(command, "@Amount", request.Amount);
        command.Parameters.Add("@PaidAt", SqlDbType.DateTime2).Value = paidAt;
        command.Parameters.Add("@ActorId", SqlDbType.Int).Value = (object?)actorId ?? DBNull.Value;
        command.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object?)request.Note ?? DBNull.Value;
        command.Parameters.Add("@Key", SqlDbType.UniqueIdentifier).Value = request.IdempotencyKey;
        int paymentId = Convert.ToInt32(command.ExecuteScalar());
        return new FeePayment(paymentId, request.FeeId, request.Amount, paidAt, actorId, request.Note, request.IdempotencyKey);
    }

    internal virtual bool UpdatePaymentState(SqlConnection connection, SqlTransaction transaction,
        int feeId, decimal paidAmount, FeeStatus status, DateTime updatedAt, DateTime? paidAt)
    {
        using var command = new SqlCommand(@"UPDATE dbo.Fees SET PaidAmount=@PaidAmount, Status=@Status,
                UpdatedAt=@UpdatedAt, PaidAt=@PaidAt
            WHERE FeeId=@FeeId AND Status IN (1,2) AND @PaidAmount <= Amount", connection, transaction);
        AddMoney(command, "@PaidAmount", paidAmount);
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)status;
        command.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = updatedAt;
        command.Parameters.Add("@PaidAt", SqlDbType.DateTime2).Value = (object?)paidAt ?? DBNull.Value;
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = feeId;
        return command.ExecuteNonQuery() == 1;
    }

    internal virtual bool Waive(SqlConnection connection, SqlTransaction transaction, int feeId,
        string reason, int? actorId, DateTime at)
    {
        using var command = new SqlCommand(@"UPDATE dbo.Fees SET Status=4, WaivedAmount=Amount-PaidAmount, WaiveReason=@Reason,
                WaivedBy=@ActorId, WaivedAt=@At, UpdatedAt=@At
            WHERE FeeId=@FeeId AND Status IN (1,2)", connection, transaction);
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason;
        command.Parameters.Add("@ActorId", SqlDbType.Int).Value = (object?)actorId ?? DBNull.Value;
        command.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = feeId;
        return command.ExecuteNonQuery() == 1;
    }

    internal virtual bool Cancel(SqlConnection connection, SqlTransaction transaction, int feeId,
        string reason, int? actorId, DateTime at)
    {
        using var command = new SqlCommand(@"UPDATE dbo.Fees SET Status=5, CancelReason=@Reason,
                CancelledBy=@ActorId, CancelledAt=@At, UpdatedAt=@At
            WHERE FeeId=@FeeId AND Status=1 AND PaidAmount=0", connection, transaction);
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason;
        command.Parameters.Add("@ActorId", SqlDbType.Int).Value = (object?)actorId ?? DBNull.Value;
        command.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
        command.Parameters.Add("@FeeId", SqlDbType.Int).Value = feeId;
        return command.ExecuteNonQuery() == 1;
    }

    internal virtual FeePayment? GetPaymentByIdempotencyKey(Guid key)
    {
        using var connection = Database.GetConnection();
        connection.Open();
        using var command = new SqlCommand(@"SELECT PaymentId, FeeId, Amount, PaidAt, RecordedBy, Note, IdempotencyKey
            FROM dbo.FeePayments WHERE IdempotencyKey=@Key", connection);
        command.Parameters.Add("@Key", SqlDbType.UniqueIdentifier).Value = key;
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPayment(reader) : null;
    }

    private static void AddMoney(SqlCommand command, string name, decimal? value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 18;
        parameter.Scale = 2;
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    private static Fee Map(SqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3),
        (FeeType)r.GetInt32(4), r.GetDecimal(5), r.GetDecimal(6), (FeeStatus)r.GetInt32(7),
        r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetString(10), r.GetString(11),
        r.GetString(12), r.GetString(13), r.IsDBNull(14) ? null : r.GetString(14),
        r.IsDBNull(15) ? null : r.GetDecimal(15), r.GetDateTime(16), r.IsDBNull(17) ? null : r.GetInt32(17),
        r.IsDBNull(18) ? null : r.GetDateTime(18), r.IsDBNull(19) ? null : r.GetDateTime(19),
        r.IsDBNull(20) ? null : r.GetDecimal(20), r.IsDBNull(21) ? null : r.GetInt32(21),
        r.IsDBNull(22) ? null : r.GetDecimal(22), r.IsDBNull(23) ? null : r.GetString(23),
        r.IsDBNull(24) ? null : r.GetDateTime(24), r.IsDBNull(25) ? null : r.GetInt32(25),
        r.IsDBNull(26) ? null : r.GetString(26), r.IsDBNull(27) ? null : r.GetDateTime(27),
        r.IsDBNull(28) ? null : r.GetInt32(28), r.IsDBNull(29) ? null : r.GetString(29),
        r.IsDBNull(30) ? null : r.GetDecimal(30), r.IsDBNull(31) ? null : r.GetDecimal(31),
        r.IsDBNull(32) ? null : r.GetDecimal(32), r.IsDBNull(33) ? null : r.GetDecimal(33),
        r.IsDBNull(34) ? null : r.GetDecimal(34), r.GetDecimal(35),
        r.IsDBNull(36) ? null : r.GetDateTime(36), r.IsDBNull(37) ? null : r.GetDateTime(37));

    private static FeePayment MapPayment(SqlDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2),
        r.GetDateTime(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetGuid(6));
}
