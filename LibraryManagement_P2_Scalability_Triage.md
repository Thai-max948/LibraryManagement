# Executive Summary

This is a source-level triage of the current `AIfixes` working tree at the requested target sizes (about 10k books, 50k copies, 10k readers, 100k+ borrow records, and 50k+ fees/notifications). The P2 query paths and indexes were inspected; no live SQL Server execution plans, representative database, or latency measurements were available, so runtime cost estimates below are reasoned estimates, not measured results.

| P2 | Decision | Confidence | Summary |
|---|---|---|---|
| P2-06 | BENCHMARK FIRST | Medium | The Borrow query filters candidate books, counts their available copies, then returns at most 8 by default (20 maximum). The new `(BookId, Status)` index may help, but whether it is used efficiently depends on the actual plan. |
| P2-07 | DEFER | Medium | History sorts by a computed effective activity date that can come from the latest audit event. Existing date indexes can help some filters but cannot provide this combined ordering. At 100k rows this is a measurable-plan question, not a safe schema-only index tweak. |
| P2-08 | BENCHMARK FIRST | Medium | Fee All-mode has no CreatedAt-leading index; Notifications has an unread-oriented index but not a global All-mode order. Each page is bounded, yet both paths count the full matching population. |
| P2-09 | DEFER | High | Return uses leading-wildcard matching on projected reader/book/barcode text, so ordinary indexes will not turn it into seeks. Active-loan restriction, 100-row default limit, 200-row cap, and debounce make FTS premature. |
| P2-10 | BENCHMARK FIRST | High | The due-soon check is awaited before MainWindow is shown, so it can extend login-to-main time. Its SQL is for one target day and has a filtered DueDate index; measure the query and sequential notification writes before changing the flow. |

**Coding now: none.** Benchmark P2-06, P2-08 and P2-10 against representative data and capture actual plans/elapsed time before selecting an implementation. P2-07 and P2-09 can remain deferred unless users report or measurements show a material delay.

# P2 Decision Table

| ID | Area | Current Risk | Recent Mitigation | Decision | Reason |
|---|---|---|---|---|---|
| P2-06 | Borrow available-copy aggregation | Low to medium at 10k books / 50k copies; broad substring searches still examine candidate data. | Search is debounced 300 ms; default 8 suggestions; service/repository clamp to 20; `(BookId, Status)` index; BookCopy backfill marker skips the completed legacy backfill. | BENCHMARK FIRST | The result limit does not cap how many matching books need counts. Index usefulness depends on join order and lookups, but this dataset is not large enough to justify speculative rewrites. |
| P2-07 | History effective activity date ordering | Medium theoretical for unfiltered pages: effective date is computed per borrowing row and page size does not bound sort input. | True page size 50; date/status filters; BorrowDate/ReturnDate/DueDate indexes; latest audit lookup index by BorrowId/time; History search debounce 300 ms. | DEFER | Existing indexes can narrow some queries, but none represents effective activity date. A computed column cannot include the latest event from another table; denormalizing it needs write-path/schema work. Measure first. |
| P2-08 | Fee and Notification All-mode ordering | Medium theoretical at 50k rows; page payload is bounded, but total-count queries still inspect matching rows. | Fee/Notification paging; Notification Center requests latest 50; unread notification index; Fee status/created and reader/status indexes. | BENCHMARK FIRST | Current indexes do not provide a guaranteed global All-mode ordering. Whether the sort/scan justifies another index at 50k must be confirmed with plans and timings. |
| P2-09 | Return contains/fuzzy search | Low to medium; a text search may inspect all active loans, but usually far fewer than all 100k historical records. | Search is debounced 300 ms; default top 100 and hard maximum 200; query restricts to Borrowing and `ReturnDate IS NULL`. | DEFER | `%keyword%` on projected/joined text is not seekable with normal indexes. Full-Text Search adds schema and consistency complexity for a small active subset; current bounds are adequate absent measured pain. |
| P2-10 | DueSoon login path | Medium user-perceived risk: awaited before MainWindow appears; event publishing is sequential for each matching loan. | One-day target window; filtered `IX_BorrowRecords_ActiveDueDate`; idempotency key prevents duplicate notification rows; BookCopy backfill marker avoids repeat backfill work during startup migrations. | BENCHMARK FIRST | The call is on the navigation path, but the database query is date-bounded and indexed. Measure login-to-main with realistic due rows before moving work off-path. |

# P2-06 Detailed Review

**Current mechanism.** `BookRepository.SearchForBorrow` joins `Books` to `BookCopies`, filters copies to Available and excludes `LegacyUnverified`, and filters book text across title, author, category, and ISBN using escaped `%query%` patterns. The book text predicate is applied before `GROUP BY`; SQL does not count every copy in the entire library regardless of search. It counts copy rows for every book matching the text query, groups by book, sorts exact matches/title, and only then applies `TOP (@Limit)`. Therefore the aggregation is not globally limited to the first 8 books: all text-matching candidate books need a count so the requested ordering/counts are correct.

**Worst realistic cost.** A broad 2-character query can scan about 10k book rows because the search patterns have a leading wildcard, then read/count qualifying available-copy rows among about 50k copies and sort candidate book groups. This is bounded by the catalog size, not a 10k × 50k cartesian operation. The result list is 8 by default and at most 20 (`BorrowViewModel`, `BookService`, `BookRepository`). The repository call is synchronous inside the post-debounce update; debounce limits request frequency but does not move a slow query off the caller context.

**Recent mitigation.** The Borrow book search has 300 ms debounce; service/repository enforce 1–20; the new `IX_BookCopies_BookId_Status` can support a BookId-driven lookup for a candidate book. `BookCopyMigration` checks `BookCopyBackfillV1` after ensuring schema/indexes and returns without rescanning/backfilling legacy rows when the marker exists; that reduces repeat migration startup work, not the per-search aggregation itself.

**Remaining risk.** The index keys are `(BookId, Status)`, while `Condition <> LegacyUnverified` is a residual predicate not included in the key. The index is useful if SQL Server drives from candidate Books and seeks copies by BookId/status; if it drives from available copies, the key order may be less useful. The text predicates remain non-sargable. Source inspection cannot establish which plan SQL Server chooses.

**Smallest possible fix.** No code change is justified yet. Capture the actual plan and IO/time at 10k/50k. If copy work dominates, test a plan shape that obtains matching book IDs first and probes/counts Available copies by `(BookId, Status)`; compare against the current grouped join before retaining any change. Do not pre-limit candidate books because that could alter exact-match ordering or availability counts.

**Recommendation: BENCHMARK FIRST. Confidence: Medium.**

Source: `LibraryManagement/Repositories/BookRepository.cs` (`SearchForBorrow`); `LibraryManagement/Services/BookService.cs` (`SearchForBorrow` clamp/minimum length); `LibraryManagement/ViewModels/BorrowViewModel.cs` (`DefaultRecommendationLimit`, 300 ms debounce); `LibraryManagement/Data/BookCopyMigration.cs` (copy index and completion marker).

# P2-07 Detailed Review

**Current mechanism.** Current History SQL orders by `COALESCE(lastEvent.OccurredAt, br.LostDate, br.ReturnDate, br.BorrowDate) DESC, br.BorrowId DESC`. `lastEvent` is an `OUTER APPLY TOP (1)` over `CirculationAuditEvents`, ordered by event time and audit ID. This is more involved than the originally described `ISNULL(ReturnDate, BorrowDate)`; the actual query includes latest audit events and LostDate. It fetches one 50-row page, while a separate count query counts all rows matching the filters and another query counts overdue rows.

**Worst realistic cost.** With Status=All and no date/search filter, the page can evaluate the effective date for the borrowing history and sort up to 100k rows before returning 50. `OFFSET` also has to skip preceding ordered rows for deeper pages. A text search uses leading-wildcard predicates over snapshot/joined reader, book, and barcode text; date filters narrow by BorrowDate, not ActionDate.

**Recent mitigation.** History is database-paged at 50 rows, supports status/date filters, and text entry is debounced. `IX_BorrowRecords_HistoryBorrowDate`, filtered `IX_BorrowRecords_HistoryReturnDate`, and the BorrowId/time audit index can help candidate filtering or latest-event lookup. `IX_BorrowRecords_ActiveDueDate` supports the overdue count. These do not establish the final ActionDate order.

**Remaining risk.** SQL Server will likely need a sort for the computed ActionDate after determining the latest audit event. The BorrowDate and ReturnDate indexes only order those individual columns; no combination of them can supply `COALESCE(latest audit event, LostDate, ReturnDate, BorrowDate)`. A persisted computed column cannot reference the correlated latest event in another table. A true indexed solution would need a maintained last-activity value/read model and coordinated writes, which increases schema and consistency complexity.

**Smallest possible fix.** Keep current behavior and measure the actual plan for unfiltered page 1, a deep page, and common filtered searches at 100k rows. If sorting is materially slow, evaluate a maintained effective-activity timestamp updated transactionally with every circulation event and index it with BorrowId; a computed-column-only change would not preserve current semantics.

**Recommendation: DEFER. Confidence: Medium.** The full-scan/sort shape is visible in source, but its practical impact at 100k has not been measured.

Source: `LibraryManagement/Repositories/HistoryRepository.cs` (`GetPage`, `BuildWhere`); `LibraryManagement/Models/HistoryQuery.cs` (page size 50); `LibraryManagement/Data/HistorySchemaMigration.cs` (history/due indexes); `LibraryManagement/Data/CirculationAuditMigration.cs` (BorrowId/event-time index).

# P2-08 Detailed Review

**Current mechanism — Fees.** `FeeRepository.GetPageAsync` runs `COUNT_BIG(*)` for every page, then selects the requested rows with `ORDER BY CreatedAt DESC, FeeId DESC OFFSET ... FETCH NEXT ...`; page size is clamped to 1–200. The WHERE clause has optional status and fee type predicates and optional contains search across reader/book/barcode snapshots and converted IDs. Those leading-wildcard/converted predicates are not seekable by ordinary indexes. The migration defines `IX_Fees_Status_Created(Status, CreatedAt DESC) INCLUDE (FeeType, Amount, PaidAmount)`, `IX_Fees_Reader_Status`, and `IX_Fees_Borrow_Created`, but none has CreatedAt as the first key for the unfiltered All list; Status_Created can only help some status-filtered plans and does not cover the full row projection.

**Current mechanism — Notifications.** `NotificationRepository.GetPageAsync` counts all matching rows then fetches a page ordered by `CreatedAt DESC, Id DESC`. The page query defaults to 50 and allows up to 100; the Notification Center requests page 1 with 50. Unread mode adds `WHERE IsRead = 0`. `IX_Notifications_IsRead_CreatedAt(IsRead, CreatedAt DESC)` supports unread filtering and the primary date order. Because `IsRead` leads, that index does not provide one global CreatedAt order across both read states in All mode. `Id` is the clustered primary key and is implicitly present in nonclustered leaves, but its direction is not declared as the required descending tie-breaker; verify whether a sort remains in the actual unread plan.

**Worst realistic cost.** For 50k fees, All mode can count the matching population and sort/scan an ordered page from the table or another index; status/type and text filters can add residual work. For notifications, All mode can sort up to 50k rows to return only 50; unread mode can seek/range-scan the read-state index, with possible lookups and tie-order work. These are not million-row workloads, and counts remain a full matching-set operation even after adding an order index.

**Recent mitigation.** Both views now use server-side pages. Notifications fetch the latest 50 and maintain a separate unread count. Fee indexes already support reader/status balance queries and some status/date access; the unread notification index covers its primary filter.

**Remaining risk and index classification.**

- **Fees, All order:** no CreatedAt-leading index. An index on `(CreatedAt DESC, FeeId DESC)` is **OPTIONAL**, not required before measurement. It could remove an All-mode ordering sort, but would add write/storage cost and would not fix broad text filtering or the full count query.
- **Notifications, All order:** current `(IsRead, CreatedAt DESC)` is not a global chronological index. `(CreatedAt DESC, Id DESC)` is **OPTIONAL** at 50k given page size 50 and no measured latency.
- **Notifications, Unread order:** another broad index is **UNNECESSARY** unless the actual plan shows an expensive tie-break sort; the existing unread index supports the filter and date range/order.

**Smallest possible fix.** First capture actual plans and elapsed/IO for Fee All page 1, Fee filtered pages, Notification All page 1, and Notification Unread page 1. If All-mode sort is a real cost, test the narrow composite order index for that table. Do not add a wide covering index without measuring; the page projects most columns and key lookups for at most 200 fees/50 notifications may be cheaper.

**Recommendation: BENCHMARK FIRST. Confidence: Medium.** The index/key shapes are confirmed from migrations and queries; exact sort, lookup, and count costs need a database plan.

Source: `LibraryManagement/Repositories/FeeRepository.cs` (`GetPageAsync`, `BuildFilter`); `LibraryManagement/Data/FeeMigration.cs` (fee indexes); `LibraryManagement/Repositories/NotificationRepository.cs` (`GetPageAsync`); `LibraryManagement/Models/NotificationPageQuery.cs` (page bounds/order); `LibraryManagement/Data/NotificationMigration.cs` (notification index).

# P2-09 Detailed Review

**Current mechanism.** `BorrowRepository.SearchActiveLoansForReturn` selects from BorrowRecords with `Status = Borrowing` and `ReturnDate IS NULL`, joins reader/book/copy display data, constructs display names/titles/barcodes with `CROSS APPLY`, filters those projected values using escaped `%keyword%` patterns, and orders by BorrowDate/BorrowId descending. The service defaults to 100 and clamps the maximum to 200; the repository also clamps to 200.

**Worst realistic cost.** With a text query, SQL may inspect all currently active loans and sort the matching subset before returning at most 100/200. The upper data target of 100k historical borrow rows is not the same as 100k active loans; the active predicate typically narrows the population, but no count was measured. When search text is empty, the query can stop after the top bounded rows if the plan can use an appropriate order, though that also needs plan confirmation.

**Recent mitigation.** Return search waits 300 ms after typing, is active-loan-only, and returns at most 100 by default/200 maximum. This prevents a request and UI materialization for the full history on each keystroke. The search call itself is synchronous after the 300 ms delay, so debounce does not cap the work or make an individual slow query non-blocking.

**Remaining risk.** The `LIKE '%...%'` predicates wrap COALESCE/LTRIM-based display expressions spanning joined tables. Conventional indexes on ReaderName, BookTitle, or Barcode cannot make those leading-wildcard projected expressions into index seeks. Full-Text Search would require indexing/maintaining text spread across snapshots and related tables, and barcode substring semantics may not fit FTS tokenization.

**Smallest possible fix.** Defer FTS. If measurements show slow searches, first measure active-loan cardinality and execution plan. Consider a product-approved prefix/exact search for reader/title while retaining barcode scan behavior, or a dedicated denormalized search projection only if the active set has become large; both change more than an index-only fix.

**Recommendation: DEFER. Confidence: High.** Source confirms non-sargable contains search and current bounds; exact active population is unknown.

Source: `LibraryManagement/Repositories/BorrowRepository.cs` (`SearchActiveLoansForReturn`); `LibraryManagement/Services/BorrowService.cs` (100/200 clamp); `LibraryManagement/Services/IReturnCirculationService.cs` (default 100); `LibraryManagement/ViewModels/ReturnViewModel.cs` (300 ms debounce).

# P2-10 Detailed Review

**Current mechanism and call ordering.** `App.OnStartup` calls `NotificationRuntime.Initialize`, which configures event handling and does not itself run the due-soon query. After successful authentication, `AuthWindow.OnLoginSuccessful` applies schema migrations, then `await`s `NotificationRuntime.CheckDueSoonSafelyAsync()`, and only after it completes creates/shows `MainWindow`. The wait is asynchronous (it need not synchronously block the dispatcher thread), but the user cannot reach the main window until it completes. Failures are caught and traced, so they do not prevent entry after the scan fails.

`DueSoonNotificationChecker` computes one target date and queries active loans with `Status='Borrowing'`, `ReturnDate IS NULL`, and `DueDate >= start AND DueDate < end` for that day. It then publishes one notification event per result sequentially. Notification keys are `DueSoon:<BorrowId>:<yyyy-MM-dd>` and have a unique index, preventing duplicate rows on a repeated check.

**Worst realistic cost.** The database range is one target day, not a scan over the entire BorrowRecords table if the index is selected. The filtered `IX_BorrowRecords_ActiveDueDate(DueDate) INCLUDE (BorrowId) WHERE Status='Borrowing'` supports the date range; `ReturnDate IS NULL` is an additional predicate, and the query needs ReaderId/BookId/DueDate for output, so lookups can occur. It also orders by BorrowId, which may require ordering the date-range candidates. The more meaningful variable is how many loans are due on that one day: each produces a sequential notification service/insert attempt before MainWindow opens. Large reminder batches could extend login time even when the query itself is fast.

**Recent mitigation.** The specific-date range and filtered DueDate index bound database work. Idempotency prevents repeated inserts. The `BookCopyBackfillV1` marker lets BookCopyMigration skip the expensive legacy copy backfill after the first completed migration; it reduces repeat login migration work but does not remove the awaited DueSoon work. `IX_BorrowRecords_ReaderId_Status INCLUDE(DueDate)` is not the key index for this query because it has no ReaderId predicate.

**Remaining risk.** This work is proven to be in the post-authentication path before MainWindow appears, but its actual delay depends on due-row cardinality, SQL plan, and sequential notification round trips. No evidence shows those are currently large enough to harm the user experience.

**Smallest possible fix.** Measure login-success-to-MainWindow time with zero, typical, and high due-row counts, including notification writes. If the scan is material, show MainWindow first and schedule `CheckDueSoonSafelyAsync` as a background task with existing exception logging/idempotency preserved. Do not change to fire-and-forget without retaining failure handling and ensuring it does not touch UI-bound state.

**Recommendation: BENCHMARK FIRST. Confidence: High for call ordering/query scope; Medium for runtime impact.**

Source: `LibraryManagement/App.xaml.cs` (`OnStartup`); `LibraryManagement/Views/AuthWindow.xaml.cs` (`OnLoginSuccessful`); `LibraryManagement/Services/NotificationEvents.cs` (`Initialize`, safe checker, handler); `LibraryManagement/Services/DueSoonNotificationChecker.cs`; `LibraryManagement/Repositories/BorrowRepository.cs` (`GetActiveLoansDueOnAsync`); `LibraryManagement/Data/HistorySchemaMigration.cs` (BorrowRecords indexes); `LibraryManagement/Data/BookCopyMigration.cs` (backfill marker).

# Recommended Next Action

No coding task is recommended now. Run one representative SQL benchmark covering P2-06, P2-08 and P2-10:

1. At the stated row counts, capture actual execution plans, logical reads, and elapsed time for Borrow search (broad 2-character and selective term), Fees/Notifications All and Unread page 1, and the DueSoon date query plus notification inserts.
2. Decide from those measurements whether the optional order indexes or a post-window DueSoon background check materially improve the user path. Preserve current behavior unless a measured bottleneck is found.

# Deferred Items

- **P2-07 History effective-date index/read model:** defer because the ordering depends on the latest audit event from another table; a computed-column index alone cannot represent it, and maintaining a denormalized timestamp adds transaction/schema complexity. Revisit if actual plans show the 100k-row sort is a user-visible bottleneck.
- **P2-09 Full-Text Search:** defer because active loans are a subset of historical records, the query is already debounced and bounded, and FTS across fallback snapshots/related tables would be disproportionate without evidence.
- **New Fee/Notification All-mode indexes:** defer pending plans because at 50k rows the output page is only 200/50 and count/search work remains even if ordering is indexed. Their additional write/storage cost needs measured benefit.
- **P2-06 query rewrite:** defer until the plan proves the new `(BookId, Status)` index is not being used effectively or the 50k-copy aggregation is material; the current query already applies book search before grouping and caps returned suggestions.
- **P2-10 moving work after window creation:** defer pending login timing because the scan is date-bounded/indexed and the number of matching loans is the unknown driver.

Audit boundary: no production code, tests, migrations, indexes, or features were changed for this triage. No live SQL plan or performance benchmark was run.

