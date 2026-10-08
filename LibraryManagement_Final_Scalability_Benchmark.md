# Environment

**Status: BENCHMARK BLOCKED — NO REAL SQL CONNECTION.** Performance queries were not executed and no measurements are reported.

- **Application runtime configuration checked:** `LibraryManagement/bin/Debug/net10.0-windows/appsettings.json` (same target as the project `appsettings.json`). It configures `Server=.\SQLEXPRESS; Database=LibraryDB; Trusted_Connection=True; TrustServerCertificate=True`. The app's `LIBRARY_TEST_DB_CONNECTION_STRING` override was not set.
- **Read-only connectivity check:** `sqlcmd -S '.\SQLEXPRESS' -d 'LibraryDB' -E -b -l 5 -t 5 -Q 'SET NOCOUNT ON; SELECT DB_NAME()'` failed with `SQL Server Network Interfaces: Error Locating Server/Instance Specified` and `Login timeout expired` (exit code 1). This is the connection target the app is configured to use.
- `sqllocaldb info` listed `MSSQLLocalDB`, but the app does not target it. I did not switch connections or attempt to repair either instance. The SQL Server service listing could not be read in this environment; that does not change the failed application-target connection result.
- **Database used:** none. The connection check ran only `SELECT DB_NAME()`; no application data was read or changed.
- **Actual row counts:** Books — NOT MEASURED; BookCopies — NOT MEASURED; Readers — NOT MEASURED; BorrowRecords — NOT MEASURED; Fees — NOT MEASURED; Notifications — NOT MEASURED.
- **Target scale from the task (not actual counts):** Books ~10,000; BookCopies ~50,000; Readers ~10,000; BorrowRecords 100,000+; Fees 50,000+; Notifications 50,000+.
- **Working tree:** branch `AIfixes`. Numerous dirty/untracked scalability files were already present before this task. They were preserved; this task adds only this report. No production/test files were changed.

Because the configured SQL Server was unreachable, measurement stopped here as required. The sections below contain the exact query shapes and a repeatable checklist to run when that existing application database is available. The snippets are templates from the current repository query semantics; do not treat them as executed results. For valid comparisons, use a representative QA database and current schema, and capture the actual plan in SSMS (Ctrl+M) alongside STATISTICS IO/TIME output.

## Shared measurement checklist

1. Connect to the same `LibraryDB` instance used by the application. Record the server/database, schema version, and actual counts using `COUNT_BIG(*)` on the six tables above. Compare each count to target scale before interpreting results.
2. For each query shape below, capture the actual execution plan. Enable `SET STATISTICS IO ON; SET STATISTICS TIME ON;` and run one warm-up followed by three measured runs. Record each run's elapsed time, CPU time, and logical reads; report the median of the three measured runs. Record operators and actual rows for scans/seeks, joins, aggregate, sort, and key lookups.
3. Keep diagnostic candidate-count queries separate from the application query timings. Do not add `OPTION`, hints, indexes, or rewritten predicates to the measured application SQL.
4. Run SELECTs only. Do not add synthetic fee, notification, book, copy, reader, or loan rows to a production database. DueSoon notification-write and login timing require a disposable QA database and the existing application behavior.

# P2-06 Borrow Search

**Measured result:** NOT MEASURED — no SQL connection. Returned suggestions, candidate-book count, IO, CPU, elapsed time, and plan/index usage are all unavailable. Decision: **INSUFFICIENT DATA**.

`BookRepository.SearchForBorrow` uses a dynamic schema variant. The current full-schema query below includes ISBN and active-book filtering; if the target DB predates those columns, use the fallback branch in the repository rather than altering the DB. The sample terms are candidates only: first run the diagnostic candidate count. If `in` is not broad in this database, choose another safe two-character fragment; if `Clean Code` is absent or not selective, choose an existing longer title/author/ISBN fragment. Use terms without `%`, `_`, `[`, or `\` so this short template's pattern equals the repository's escaped pattern.

## Broad query

```sql
-- Selective plan capture in SSMS; one warm-up + three measured executions.
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
DECLARE @Query nvarchar(4000) = N'in';
DECLARE @Pattern nvarchar(4000) = N'%' + @Query + N'%';
DECLARE @Limit int = 8;
DECLARE @AvailableStatus nvarchar(20) = N'Available';
DECLARE @LegacyUnverified nvarchar(50) = N'LegacyUnverified';
DECLARE @ActiveStatus nvarchar(20) = N'Active';

-- Diagnostic candidate-book count; record separately from the app query.
SELECT COUNT_BIG(*) AS MatchingCandidateBooks
FROM dbo.Books AS b
WHERE b.Status = @ActiveStatus
  AND (b.Title LIKE @Pattern ESCAPE N'\'
    OR b.Author LIKE @Pattern ESCAPE N'\'
    OR b.Category LIKE @Pattern ESCAPE N'\'
    OR b.ISBN LIKE @Pattern ESCAPE N'\');

-- Application query shape, limit 8.
SELECT TOP (@Limit)
    b.BookId, b.Title, b.Author, b.Category, b.ISBN,
    COUNT(*) AS AvailableCopyCount
FROM dbo.Books AS b
INNER JOIN dbo.BookCopies AS bc ON bc.BookId = b.BookId
WHERE bc.Status = @AvailableStatus
  AND bc.Condition <> @LegacyUnverified
  AND b.Status = @ActiveStatus
  AND (b.Title LIKE @Pattern ESCAPE N'\'
    OR b.Author LIKE @Pattern ESCAPE N'\'
    OR b.Category LIKE @Pattern ESCAPE N'\'
    OR b.ISBN LIKE @Pattern ESCAPE N'\')
GROUP BY b.BookId, b.Title, b.Author, b.Category, b.ISBN
ORDER BY CASE WHEN (b.Title = @Query OR b.ISBN = @Query) THEN 0 ELSE 1 END,
    b.Title ASC, b.BookId ASC;
```

## Selective query

Repeat the same script with `@Query = N'Clean Code'` (or a verified existing, selective fragment), recording candidate count, result count up to 8, each measured run, and median. Optionally repeat both search cases with `@Limit = 20` to compare only the requested result limit.

## IO/time and plan checklist

| Case | Candidate Books | Returned | Elapsed / CPU | Logical reads | Actual operators / index evidence |
|---|---:|---:|---|---|---|
| Broad, limit 8 | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| Selective, limit 8 | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| Optional broad/selective, limit 20 | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED | NOT MEASURED |

Inspect whether the actual plan scans Books, probes/seeks BookCopies by `(BookId, Status)`, scans Available copies as the driving input, filters `Condition` as a residual, and uses hash/stream aggregate and sort. No statement about `IX_BookCopies_BookId_Status` usage is possible without that plan.

**Decision:** INSUFFICIENT DATA. Do not optimize based on this blocked run.

# P2-08 Fees

**Measured result:** NOT MEASURED — no SQL connection. `FeeSearchQuery` defaults to page 1, size 50; FeesViewModel passes current filters. For All mode set Status, FeeType, and Search to NULL. The repository executes the count first and the page query separately; measure and report them separately.

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
DECLARE @Status int = NULL;
DECLARE @FeeType int = NULL;
DECLARE @Search nvarchar(300) = NULL;
DECLARE @Offset int = 0;
DECLARE @PageSize int = 50;

-- Run this count alone (warm-up + three measured runs).
SELECT COUNT_BIG(*)
FROM dbo.Fees
WHERE (@Status IS NULL OR Status = @Status)
  AND (@FeeType IS NULL OR FeeType = @FeeType)
  AND (@Search IS NULL OR ReaderNameSnapshot LIKE @Search
       OR BookTitleSnapshot LIKE @Search
       OR COALESCE(BarcodeSnapshot, N'') LIKE @Search
       OR CONVERT(nvarchar(20), FeeId) LIKE @Search
       OR CONVERT(nvarchar(20), BorrowId) LIKE @Search
       OR CONVERT(nvarchar(20), ReaderId) LIKE @Search);

-- Run this page query separately (warm-up + three measured runs).
SELECT FeeId, BorrowId, ReaderId, BookCopyId, FeeType, Amount, PaidAmount, Status,
       Reason, Description, SourceType, SourceId, ReaderNameSnapshot, BookTitleSnapshot,
       BarcodeSnapshot, BookPriceSnapshot, CreatedAt, CreatedBy, UpdatedAt, PaidAt,
       RentalPriceSnapshot, LateDays, AppliedRate, DamageLevel, WaivedAt, WaivedBy,
       WaiveReason, CancelledAt, CancelledBy, CancelReason, AppliedCapRate, BaseAmount,
       Units, CapAmount, UncappedAmount, WaivedAmount, DueDateSnapshot, ResolvedAtSnapshot
FROM dbo.Fees
WHERE (@Status IS NULL OR Status = @Status)
  AND (@FeeType IS NULL OR FeeType = @FeeType)
  AND (@Search IS NULL OR ReaderNameSnapshot LIKE @Search
       OR BookTitleSnapshot LIKE @Search
       OR COALESCE(BarcodeSnapshot, N'') LIKE @Search
       OR CONVERT(nvarchar(20), FeeId) LIKE @Search
       OR CONVERT(nvarchar(20), BorrowId) LIKE @Search
       OR CONVERT(nvarchar(20), ReaderId) LIKE @Search)
ORDER BY CreatedAt DESC, FeeId DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
```

| Fee All, page 1 | Result |
|---|---|
| Count result | NOT MEASURED |
| Count elapsed / CPU / logical reads | NOT MEASURED |
| Page elapsed / CPU / logical reads | NOT MEASURED |
| Page actual plan: existing index vs scan/sort/lookups | NOT MEASURED |
| Representative median (count and page kept separate) | NOT MEASURED |

If useful, a common filtered query can be measured in a separate run by setting its real Status/FeeType/Search values; do not combine its cost with All mode. Existing fee indexes and a potential `(CreatedAt DESC, FeeId DESC)` index must not be judged by source alone.

**Decision:** INSUFFICIENT DATA. No index is recommended from an unexecuted benchmark.

# P2-08 Notifications

**Measured result:** NOT MEASURED — no SQL connection. The app requests page 1 with 50 rows. For both All and Unread, repository `GetPageAsync` runs a total-count query and then the page query. `GetUnreadCountAsync` is an additional query and should be measured separately.

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

-- A. All count (warm-up + three measured runs).
SELECT COUNT_BIG(*) FROM dbo.Notifications;

-- A. All page 1, 50 rows (run separately; warm-up + three measured runs).
SELECT n.Id, n.Title, n.Message, n.Type, n.SourceModule,
       n.SourceEntityType, n.SourceEntityId, n.CreatedAt, n.IsRead
FROM dbo.Notifications AS n
ORDER BY n.CreatedAt DESC, n.Id DESC
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;

-- B. Unread count inside GetPageAsync (warm-up + three measured runs).
SELECT COUNT_BIG(*) FROM dbo.Notifications WHERE IsRead = 0;

-- B. Unread page 1, 50 rows (run separately; warm-up + three measured runs).
SELECT n.Id, n.Title, n.Message, n.Type, n.SourceModule,
       n.SourceEntityType, n.SourceEntityId, n.CreatedAt, n.IsRead
FROM dbo.Notifications AS n
WHERE IsRead = 0
ORDER BY n.CreatedAt DESC, n.Id DESC
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;

-- C. Standalone unread count used by GetUnreadCountAsync.
SELECT COUNT(*) FROM dbo.Notifications WHERE IsRead = 0;
```

| Notification case | Elapsed / CPU | Logical reads | Actual plan / index evidence |
|---|---|---|---|
| All count | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| All page 1 | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| Unread page count | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| Unread page 1 | NOT MEASURED | NOT MEASURED | NOT MEASURED |
| Standalone unread count | NOT MEASURED | NOT MEASURED | NOT MEASURED |

For Unread, verify whether the plan uses `IX_Notifications_IsRead_CreatedAt` and whether a sort remains for `Id DESC`. For All, inspect whether the plan scans an index/table and sorts by both keys or obtains order another way. Do not infer either plan from the index definition.

**Decision:** INSUFFICIENT DATA. The All-mode `(CreatedAt DESC, Id DESC)` index remains only a candidate. Recommend it only if measured scan/sort cost creates meaningful user-facing latency relative to its storage/write cost.

# P2-10 DueSoon

**Measured result:** NOT MEASURED — no SQL connection. `DueSoonDatePolicy` selects the app-local date two days ahead and queries the half-open day window. For a future run, set `@TargetDate` to the value returned by that policy using the app machine's local date; do not use a stale date.

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
DECLARE @TargetDate date = DATEADD(day, 2, CONVERT(date, SYSDATETIMEOFFSET())); -- local server date + ReminderDaysBeforeDue; for a remote SQL Server, supply the app machine's local target date
DECLARE @StartDate datetime2 = CONVERT(datetime2, @TargetDate);
DECLARE @EndDate datetime2 = DATEADD(day, 1, @StartDate);

-- Diagnostic matching count; measure and label separately from repository SELECT.
SELECT COUNT_BIG(*) AS MatchingDueSoonLoans
FROM dbo.BorrowRecords AS br
WHERE br.Status = N'Borrowing'
  AND br.ReturnDate IS NULL
  AND br.DueDate >= @StartDate
  AND br.DueDate < @EndDate;

-- Exact repository SELECT shape (warm-up + three measured runs).
SELECT br.BorrowId, br.ReaderId, b.Title, br.DueDate
FROM dbo.BorrowRecords AS br
INNER JOIN dbo.Books AS b ON b.BookId = br.BookId
WHERE br.Status = N'Borrowing'
  AND br.ReturnDate IS NULL
  AND br.DueDate >= @StartDate
  AND br.DueDate < @EndDate
ORDER BY br.BorrowId;
```

| DueSoon observation | Result |
|---|---|
| App-local target date | NOT RESOLVED FOR A SQL RUN |
| Matching loans | NOT MEASURED |
| SELECT elapsed / CPU / logical reads | NOT MEASURED |
| Actual use of `IX_BorrowRecords_ActiveDueDate` | NOT MEASURED |
| Notification processing / insert time | NOT MEASURED; no QA database available |
| Zero / typical / high due-loan scenarios | NOT MEASURED; no disposable QA data |
| Authentication success → MainWindow shown | NOT MEASURED; SQL connection unavailable, app not launched for timing |

`AuthWindow.OnLoginSuccessful` awaits `CheckDueSoonSafelyAsync()` before constructing/showing `MainWindow`. The checker processes returned loans sequentially, and each notification uses the idempotent notification insert path. This proves call ordering, not that the delay is material. A write benchmark must run only on disposable QA data; do not use this SQL template to insert notifications into production.

The repository write shape below is provided only to identify the measured operation. It is parameterized and **QA-only**; it was not executed. For representative sequential processing time, run the existing checker/service against a disposable QA database with zero, typical, and high due-row counts. Because the key `DueSoon:<BorrowId>:<yyyy-MM-dd>` makes repeat runs idempotent, restore a clean QA snapshot or use clean QA fixtures for each measured write run; otherwise later runs measure duplicate suppression instead of inserts. A raw INSERT timing is not a substitute for the end-to-end sequential checker measurement.

```sql
-- QA ONLY. Never execute on production. Parameters must be supplied by the QA harness.
INSERT INTO dbo.Notifications
    (Title, Message, Type, SourceModule, SourceEntityType, SourceEntityId,
     IdempotencyKey, CreatedAt, IsRead)
OUTPUT INSERTED.Id
VALUES
    (@Title, @Message, @Type, @SourceModule, @SourceEntityType, @SourceEntityId,
     @IdempotencyKey, @CreatedAt, 0);
```

**Decision:** INSUFFICIENT DATA. Do not move DueSoon work off the login path unless a safe QA/application timing shows it accounts for a noticeable portion of login-to-main time.

# Final Decision Table

| P2 | Measured? | Result | Decision | Recommended action |
|---|---|---|---|---|
| P2-06 | No | SQL connection unavailable; no actual counts, query runs, IO, CPU, or plan | INSUFFICIENT DATA | Run broad and selective Borrow search cases at representative DB size; inspect actual copy-index use before changing SQL. |
| P2-08 Fees | No | Count and page query not run | INSUFFICIENT DATA | Measure All mode count and page separately; consider an order index only if the observed sort/scan justifies it. |
| P2-08 Notifications | No | All, Unread, and unread count not run | INSUFFICIENT DATA | Capture actual plans for All and Unread; keep the candidate All-order index uncreated until measured. |
| P2-10 DueSoon | No | SELECT, notification writes, and login timing not run | INSUFFICIENT DATA | Measure SELECT and sequential writes using safe QA data; change login ordering only if DueSoon causes noticeable delay. |

# Remaining Scalability Work

**None proven necessary by measurement in this run.** P2-06, P2-08, and P2-10 remain inconclusive, not accepted/closed: the configured application SQL Server instance was unreachable, so no actual row counts or performance evidence exists. Re-run this benchmark when the existing `.\SQLEXPRESS` / `LibraryDB` target is available. Do not add indexes or change production behavior based on this blocked run.

P2-07 History ordering and P2-09 Return contains search were explicitly out of scope and were not revisited.

Audit boundary: no SQL performance query, data mutation, app launch, build, test, production-code change, migration, index, or UI change was performed. The only new file for this task is `LibraryManagement_Final_Scalability_Benchmark.md`.


