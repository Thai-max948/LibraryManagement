using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data;

public static class FeeMigration
{
    public static void Apply() => ApplyAsync().GetAwaiter().GetResult();

    public static async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Database.GetConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction, @"
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock @Resource = 'LibraryManagement.FeeMigration',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @lockResult < 0 THROW 51000, 'Fee migration lock unavailable', 1;
                IF OBJECT_ID('dbo.BorrowRecords', 'U') IS NULL OR OBJECT_ID('dbo.Readers', 'U') IS NULL
                    OR OBJECT_ID('dbo.Books', 'U') IS NULL OR OBJECT_ID('dbo.BookCopies', 'U') IS NULL
                    OR OBJECT_ID('dbo.Users', 'U') IS NULL
                    THROW 51001, 'Fee migration requires Books, BookCopies, Readers, BorrowRecords, and Users schemas.', 1;
                IF OBJECT_ID('dbo.Fees', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Fees (
                        FeeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fees PRIMARY KEY,
                        BorrowId INT NOT NULL, ReaderId INT NOT NULL, BookCopyId INT NULL,
                        FeeType INT NOT NULL, Amount DECIMAL(18,2) NOT NULL,
                        PaidAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Fees_PaidAmount DEFAULT 0,
                        WaivedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Fees_WaivedAmount DEFAULT 0,
                        Status INT NOT NULL, Reason NVARCHAR(500) NOT NULL,
                        Description NVARCHAR(1000) NULL, SourceType NVARCHAR(100) NOT NULL,
                        SourceId NVARCHAR(200) NOT NULL, ReaderNameSnapshot NVARCHAR(150) NOT NULL,
                        BookTitleSnapshot NVARCHAR(255) NOT NULL, BarcodeSnapshot NVARCHAR(100) NULL,
                        BookPriceSnapshot DECIMAL(18,2) NULL, RentalPriceSnapshot DECIMAL(18,2) NULL,
                        LateDays INT NULL, AppliedRate DECIMAL(9,6) NULL, AppliedCapRate DECIMAL(9,6) NULL, DamageLevel NVARCHAR(30) NULL,
                        BaseAmount DECIMAL(18,2) NULL, Units DECIMAL(18,4) NULL,
                        CapAmount DECIMAL(18,2) NULL, UncappedAmount DECIMAL(18,2) NULL,
                        DueDateSnapshot DATETIME2(7) NULL, ResolvedAtSnapshot DATETIME2(7) NULL,
                        CreatedAt DATETIME2(7) NOT NULL, CreatedBy INT NULL,
                        UpdatedAt DATETIME2(7) NULL, PaidAt DATETIME2(7) NULL,
                        WaivedAt DATETIME2(7) NULL, WaivedBy INT NULL, WaiveReason NVARCHAR(500) NULL,
                        CancelledAt DATETIME2(7) NULL, CancelledBy INT NULL, CancelReason NVARCHAR(500) NULL,
                        CONSTRAINT FK_Fees_Borrow FOREIGN KEY (BorrowId) REFERENCES dbo.BorrowRecords(BorrowId),
                        CONSTRAINT FK_Fees_Reader FOREIGN KEY (ReaderId) REFERENCES dbo.Readers(ReaderId),
                        CONSTRAINT FK_Fees_Copy FOREIGN KEY (BookCopyId) REFERENCES dbo.BookCopies(CopyId),
                        CONSTRAINT FK_Fees_Actor FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id),
                        CONSTRAINT FK_Fees_WaivedBy FOREIGN KEY (WaivedBy) REFERENCES dbo.Users(Id),
                        CONSTRAINT FK_Fees_CancelledBy FOREIGN KEY (CancelledBy) REFERENCES dbo.Users(Id),
                        CONSTRAINT CK_Fees_Type CHECK (FeeType BETWEEN 1 AND 7),
                        CONSTRAINT CK_Fees_Amounts CHECK (Amount >= 0 AND PaidAmount >= 0 AND PaidAmount <= Amount),
                        CONSTRAINT CK_Fees_Status CHECK (
                            (Status = 1 AND Amount > 0 AND PaidAmount = 0) OR
                            (Status = 2 AND PaidAmount > 0 AND PaidAmount < Amount) OR
                            (Status = 3 AND PaidAmount = Amount) OR
                            (Status = 4 AND PaidAmount < Amount) OR
                            (Status = 5 AND Amount > 0 AND PaidAmount = 0)),
                        CONSTRAINT CK_Fees_TerminalAudit CHECK (
                            (Status = 4 AND WaivedAt IS NOT NULL AND NULLIF(LTRIM(RTRIM(WaiveReason)), '') IS NOT NULL) OR
                            (Status = 5 AND CancelledAt IS NOT NULL AND NULLIF(LTRIM(RTRIM(CancelReason)), '') IS NOT NULL) OR
                            Status IN (1,2,3)),
                        CONSTRAINT CK_Fees_Source CHECK (LEN(LTRIM(RTRIM(SourceType))) > 0 AND LEN(LTRIM(RTRIM(SourceId))) > 0),
                        CONSTRAINT CK_Fees_Snapshot CHECK (LEN(LTRIM(RTRIM(ReaderNameSnapshot))) > 0 AND LEN(LTRIM(RTRIM(BookTitleSnapshot))) > 0),
                        CONSTRAINT CK_Fees_Price CHECK ((BookPriceSnapshot IS NULL OR BookPriceSnapshot >= 0) AND (RentalPriceSnapshot IS NULL OR RentalPriceSnapshot >= 0)),
                        CONSTRAINT CK_Fees_WaivedAmount CHECK (
                            (Status = 4 AND WaivedAmount = Amount - PaidAmount) OR
                            (Status <> 4 AND WaivedAmount = 0)),
                        CONSTRAINT CK_Fees_CalculationSnapshot CHECK (
                            (BaseAmount IS NULL OR BaseAmount >= 0) AND
                            (Units IS NULL OR Units >= 0) AND
                            (CapAmount IS NULL OR CapAmount >= 0) AND
                            (UncappedAmount IS NULL OR UncappedAmount >= 0))
                    );
                END", cancellationToken).ConfigureAwait(false);

            // Each add-column operation is a separate batch so older SQL Server schemas compile safely.
            await AddColumnAsync(connection, transaction, "RentalPriceSnapshot", "DECIMAL(18,2) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "LateDays", "INT NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "AppliedRate", "DECIMAL(9,6) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "AppliedCapRate", "DECIMAL(9,6) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "BaseAmount", "DECIMAL(18,2) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "Units", "DECIMAL(18,4) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "CapAmount", "DECIMAL(18,2) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "UncappedAmount", "DECIMAL(18,2) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "DueDateSnapshot", "DATETIME2(7) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "ResolvedAtSnapshot", "DATETIME2(7) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "WaivedAmount", "DECIMAL(18,2) NOT NULL CONSTRAINT DF_Fees_WaivedAmount DEFAULT (0) WITH VALUES", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "DamageLevel", "NVARCHAR(30) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "WaivedAt", "DATETIME2(7) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "WaivedBy", "INT NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "WaiveReason", "NVARCHAR(500) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "CancelledAt", "DATETIME2(7) NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "CancelledBy", "INT NULL", cancellationToken).ConfigureAwait(false);
            await AddColumnAsync(connection, transaction, "CancelReason", "NVARCHAR(500) NULL", cancellationToken).ConfigureAwait(false);

            // Existing installations receive the same integrity protections as fresh installs.
            await ExecuteAsync(connection, transaction, @"
                UPDATE dbo.Fees SET WaivedAmount = Amount - PaidAmount
                    WHERE Status = 4 AND WaivedAmount = 0 AND Amount > PaidAmount;
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE
                    (Status = 4 AND WaivedAmount <> Amount - PaidAmount) OR
                    (Status <> 4 AND WaivedAmount <> 0))
                    THROW 51009, 'Existing Fees rows have an inconsistent waived remainder; review them before migration.', 1;
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE Amount < 0 OR PaidAmount < 0 OR PaidAmount > Amount)
                    THROW 51002, 'Existing Fees rows violate amount invariants; repair them before applying Fee migration.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_Amounts')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_Amounts
                        CHECK (Amount >= 0 AND PaidAmount >= 0 AND PaidAmount <= Amount);
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE FeeType NOT BETWEEN 1 AND 7)
                    THROW 51003, 'Existing Fees rows contain an unknown FeeType.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_Type')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_Type CHECK (FeeType BETWEEN 1 AND 7);
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE NOT (
                    (Status = 1 AND Amount > 0 AND PaidAmount = 0) OR
                    (Status = 2 AND PaidAmount > 0 AND PaidAmount < Amount) OR
                    (Status = 3 AND PaidAmount = Amount) OR
                    (Status = 4 AND PaidAmount < Amount) OR
                    (Status = 5 AND Amount > 0 AND PaidAmount = 0)))
                    THROW 51004, 'Existing Fees rows violate FeeStatus invariants.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_Status')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_Status CHECK (
                        (Status = 1 AND Amount > 0 AND PaidAmount = 0) OR
                        (Status = 2 AND PaidAmount > 0 AND PaidAmount < Amount) OR
                        (Status = 3 AND PaidAmount = Amount) OR
                        (Status = 4 AND PaidAmount < Amount) OR
                        (Status = 5 AND Amount > 0 AND PaidAmount = 0));
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_StateInvariantV2')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_StateInvariantV2 CHECK (
                        (Status = 1 AND Amount > 0 AND PaidAmount = 0) OR
                        (Status = 2 AND PaidAmount > 0 AND PaidAmount < Amount) OR
                        (Status = 3 AND PaidAmount = Amount) OR
                        (Status = 4 AND PaidAmount < Amount) OR
                        (Status = 5 AND Amount > 0 AND PaidAmount = 0));
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE
                    (Status = 4 AND (WaivedAt IS NULL OR NULLIF(LTRIM(RTRIM(WaiveReason)), '') IS NULL)) OR
                    (Status = 5 AND (CancelledAt IS NULL OR NULLIF(LTRIM(RTRIM(CancelReason)), '') IS NULL)))
                    THROW 51005, 'Existing terminal Fees rows are missing their audit reason or timestamp.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_TerminalAudit')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_TerminalAudit CHECK (
                        (Status = 4 AND WaivedAt IS NOT NULL AND NULLIF(LTRIM(RTRIM(WaiveReason)), '') IS NOT NULL) OR
                        (Status = 5 AND CancelledAt IS NOT NULL AND NULLIF(LTRIM(RTRIM(CancelReason)), '') IS NOT NULL) OR
                        Status IN (1,2,3));
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE (AppliedRate IS NOT NULL AND (AppliedRate < 0 OR AppliedRate > 1))
                    OR (AppliedCapRate IS NOT NULL AND (AppliedCapRate < 0 OR AppliedCapRate > 1)))
                    THROW 51008, 'Existing Fees rows contain an invalid applied policy rate.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_AppliedRates')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_AppliedRates CHECK (
                        (AppliedRate IS NULL OR (AppliedRate >= 0 AND AppliedRate <= 1)) AND
                        (AppliedCapRate IS NULL OR (AppliedCapRate >= 0 AND AppliedCapRate <= 1)));
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_WaivedAmount')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_WaivedAmount CHECK (
                        (Status = 4 AND WaivedAmount = Amount - PaidAmount) OR (Status <> 4 AND WaivedAmount = 0));
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE (BaseAmount IS NOT NULL AND BaseAmount < 0)
                    OR (Units IS NOT NULL AND Units < 0) OR (CapAmount IS NOT NULL AND CapAmount < 0)
                    OR (UncappedAmount IS NOT NULL AND UncappedAmount < 0))
                    THROW 51010, 'Existing Fees rows contain invalid calculation snapshot values.', 1;
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Fees') AND name = 'CK_Fees_CalculationSnapshot')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT CK_Fees_CalculationSnapshot CHECK (
                        (BaseAmount IS NULL OR BaseAmount >= 0) AND (Units IS NULL OR Units >= 0) AND
                        (CapAmount IS NULL OR CapAmount >= 0) AND (UncappedAmount IS NULL OR UncappedAmount >= 0));", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, @"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_Borrow')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT FK_Fees_Borrow FOREIGN KEY (BorrowId) REFERENCES dbo.BorrowRecords(BorrowId);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_Reader')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT FK_Fees_Reader FOREIGN KEY (ReaderId) REFERENCES dbo.Readers(ReaderId);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_Copy')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT FK_Fees_Copy FOREIGN KEY (BookCopyId) REFERENCES dbo.BookCopies(CopyId);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_Actor')
                    ALTER TABLE dbo.Fees WITH CHECK ADD CONSTRAINT FK_Fees_Actor FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_WaivedBy')
                    ALTER TABLE dbo.Fees ADD CONSTRAINT FK_Fees_WaivedBy FOREIGN KEY (WaivedBy) REFERENCES dbo.Users(Id);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Fees_CancelledBy')
                    ALTER TABLE dbo.Fees ADD CONSTRAINT FK_Fees_CancelledBy FOREIGN KEY (CancelledBy) REFERENCES dbo.Users(Id);", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, @"
                IF OBJECT_ID('dbo.FeePayments', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.FeePayments (
                        PaymentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeePayments PRIMARY KEY,
                        FeeId INT NOT NULL, Amount DECIMAL(18,2) NOT NULL,
                        PaidAt DATETIME2(7) NOT NULL, RecordedBy INT NULL,
                        Note NVARCHAR(500) NULL, IdempotencyKey UNIQUEIDENTIFIER NOT NULL,
                        CONSTRAINT FK_FeePayments_Fee FOREIGN KEY (FeeId) REFERENCES dbo.Fees(FeeId),
                        CONSTRAINT FK_FeePayments_Actor FOREIGN KEY (RecordedBy) REFERENCES dbo.Users(Id),
                        CONSTRAINT CK_FeePayments_Amount CHECK (Amount > 0)
                    );
                END", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, @"
                IF EXISTS (SELECT 1 FROM dbo.Fees WHERE Status <> 5
                    GROUP BY SourceType, SourceId, FeeType HAVING COUNT_BIG(*) > 1)
                    THROW 51006, 'Existing active Fees rows have duplicate source keys; resolve duplicates before applying the unique index.', 1;
                IF EXISTS (SELECT 1 FROM dbo.FeePayments GROUP BY IdempotencyKey HAVING COUNT_BIG(*) > 1)
                    THROW 51007, 'Existing FeePayments rows have duplicate idempotency keys; resolve duplicates before applying the unique index.', 1;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Fees')
                    AND name = 'UX_Fees_Source' AND (is_unique = 0 OR has_filter = 0 OR
                    ISNULL(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(filter_definition, '[', ''), ']', ''), '(', ''), ')', ''), ' ', ''), N'') <> N'Status<>5'))
                    DROP INDEX UX_Fees_Source ON dbo.Fees;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Fees')
                    AND name = 'UX_Fees_Source' AND is_unique = 1 AND has_filter = 1)
                    EXEC(N'CREATE UNIQUE INDEX UX_Fees_Source ON dbo.Fees(SourceType, SourceId, FeeType) WHERE Status <> 5;');
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Fees') AND name = 'IX_Fees_Reader_Status')
                    CREATE INDEX IX_Fees_Reader_Status ON dbo.Fees(ReaderId, Status) INCLUDE (Amount, PaidAmount);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Fees') AND name = 'IX_Fees_Borrow_Created')
                    CREATE INDEX IX_Fees_Borrow_Created ON dbo.Fees(BorrowId, CreatedAt DESC);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Fees') AND name = 'IX_Fees_Status_Created')
                    CREATE INDEX IX_Fees_Status_Created ON dbo.Fees(Status, CreatedAt DESC) INCLUDE (FeeType, Amount, PaidAmount);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.FeePayments') AND name = 'UX_FeePayments_IdempotencyKey')
                    CREATE UNIQUE INDEX UX_FeePayments_IdempotencyKey ON dbo.FeePayments(IdempotencyKey);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.FeePayments') AND name = 'IX_FeePayments_Fee_PaidAt')
                    CREATE INDEX IX_FeePayments_Fee_PaidAt ON dbo.FeePayments(FeeId, PaidAt, PaymentId);", cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private static async Task AddColumnAsync(SqlConnection connection, SqlTransaction transaction,
        string column, string definition, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction,
            $"IF COL_LENGTH('dbo.Fees', '{column}') IS NULL ALTER TABLE dbo.Fees ADD [{column}] {definition};", token).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction,
        string sql, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
