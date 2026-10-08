# Library Management — Final Application-Wide Scalability Audit

**Audit date:** 2026-10-07  
**Repository:** C:\Users\Admin\LibraryManagement1  
**Branch / audited HEAD:** AIfixes / a67d9bd  
**Scope:** Static read-only review of production code, tracked schema and migrations against the requested scale: Readers 10k, Books 10k, BookCopies 50k, BorrowRecords/History 100k+, Fees 50k+, Notifications 50k+.

## Executive result

**Overall: Needs targeted work before claiming readiness at all target sizes.** Main Books, Readers, Fees, Notifications and History result collections are paged on the server; Borrow and Return display bounded result sets. The largest remaining risks are in startup and selected-record operations rather than loading every module’s full data into its main grid.

| Severity | Findings |
|---|---:|
| P0 — release blocker | 0 |
| P1 — high | 4 |
| P2 — medium | 6 |
| P3 — low | 1 |

Severity is based on source query shape and requested row counts, not measured response time. No live SQL Server query plans, production database index inventory, load test, or benchmark was available for this audit.

## Module scorecard

| Area | Assessment | Evidence / scale note |
|---|---|---|
| Startup and migrations | **Needs attention** | Login invokes all startup migrations. BookCopy migration walks every book and issues per-book queries under a Serializable transaction (Finding P1-01). Due-soon notifications are written serially before the main window opens (P2-10). |
| Dashboard | **Ready for stated target, monitor I/O** | Dashboard repository returns aggregate metrics and bounded recent rows; service awaits asynchronous database calls. Aggregates still read relevant tables, but do not materialize all rows into the UI. |
| Books | **Partial** | Main catalog uses SQL COUNT + OFFSET/FETCH and configurable page sizes 25/50/100. Book editing materializes every active loan to count loans for one title (P1-02). Search is synchronous per keystroke and repeats schema probes (P2-05); author suggestions load every distinct author (P3-11). |
| Book copies | **Needs attention** | Queries return all copies for a selected book and filter by BookId without a declared BookId-leading index in tracked schema/migrations (P1-03). At 50k copies this can repeatedly scan the copy table; a highly skewed title can also return a large UI collection. |
| Readers | **Partial** | Main reader grid uses SQL paging (10/20/50), and borrow suggestions are TOP-limited. Reader detail issues three ReaderId-scoped history queries without a ReaderId-leading BorrowRecords index (P1-04). Search is synchronous as text changes (P2-05). |
| Borrow | **Partial** | Current loans are projected with TOP limit 100 (maximum 200), avoiding full entity loads. Reader suggestions are bounded; book suggestions are TOP 20 but aggregate available copies before TOP and are called while the user types (P2-06). |
| Return | **Partial** | Active loans are TOP-limited (maximum 200), but searching uses contains-pattern predicates over display values and sorts by BorrowDate without a matching declared filtered index (P2-09). Search runs as the input changes (P2-05). |
| History | **Needs optimization** | Result page is bounded and audit events use an index by BorrowId, but the page sorts by a computed ActionDate requiring a latest-event lookup per candidate loan (P2-07). |
| Fees | **Ready with index gap** | Async server paging, page size capped at 200, and payment history is scoped to one fee. The All view orders by CreatedAt/FeeId but the tracked index starts with Status (P2-08). |
| Notifications | **Ready with index gap** | Async server paging returns at most 100, with a separate unread count. The All view’s CreatedAt/Id order is not covered by the existing IsRead-leading index (P2-08). |
| Inventory check | **Acceptable for targets; watch lock duration** | User-triggered report aggregates copies/loans in SQL and returns one row per book. It runs in a Serializable transaction, so lock duration should be measured under concurrent circulation load. The old in-memory report is not called by the production service. |
| My Account | **Ready** | Profile operations are scoped to the current account; no large collection load found. |
| User Accounts | **Acceptable for expected small staff set** | Admin view loads all users and filters in memory. This is appropriate only while staff-account count remains small; it is not a public reader-scale path. |

## Findings

### P1-01 — BookCopy migration repeats per-book work on every successful login

**Evidence:** [AuthWindow.xaml.cs](../../LibraryManagement/Views/AuthWindow.xaml.cs#L363) calls LibraryDatabaseStartupMigration after login and waits for the due-soon scan before opening MainWindow. [LibraryDatabaseStartupMigration.cs](../../LibraryManagement/Data/LibraryDatabaseStartupMigration.cs#L8) calls BookCopyMigration.Apply. That migration opens a Serializable transaction, reads all books, and loops over them; inside the loop it queries unconverted active-loan IDs and counts copies for each book. See [BookCopyMigration.cs](../../LibraryManagement/Data/Migrations/BookCopyMigration.cs#L11), [lines 77–108](../../LibraryManagement/Data/Migrations/BookCopyMigration.cs#L77), and [lines 157–165](../../LibraryManagement/Data/Migrations/BookCopyMigration.cs#L157).

At 10k books, this is at least two SQL executions per book on every login, and existing-copy rows may trigger another per-book count. The loan query filters by BookId, status and CopyId, while no matching BookId-leading BorrowRecords index is declared in the tracked schema/migrations. This creates repeated scans and holds a Serializable transaction during the walk. The first-run migration is expected work; repeating the reconciliation walk after migration completion is the scale concern.

**Smallest next step:** Gate reconciliation on an explicit migration version/completion marker. When reconciliation is needed, batch the per-book loan/copy aggregates rather than issuing commands inside the book loop; validate supporting indexes against actual plans.

### P1-02 — Editing one book materializes every active loan

**Evidence:** [BookService.cs](../../LibraryManagement/Services/Books/BookService.cs#L53) calls GetBorrowingRecords at line 71, then counts matching BookId values in application memory. The method is used to validate a single edited book’s quantity.

At 100k+ history and up to 50k copies, a book edit transfers/materializes the whole active-loan set for one count. The main Borrow grid already has a bounded projection, but this edit path bypasses it.

**Smallest next step:** Add a repository aggregate returning COUNT for the edited BookId and active status, and use that scalar in the validation.

### P1-03 — Per-book copy reads are unpaged and lack a declared BookId-leading index

**Evidence:** [BookCopyRepository.cs](../../LibraryManagement/Repositories/BookCopyRepository.cs#L103) loads every copy for a BookId; [line 115](../../LibraryManagement/Repositories/BookCopyRepository.cs#L115) does the same for available copies. Related scalar queries count or select copies by BookId at [lines 192–219](../../LibraryManagement/Repositories/BookCopyRepository.cs#L192). The tracked base schema declares the barcode unique index ([LibraryDB.sql](../../LibraryDB.sql#L177)) but no BookId-leading copy index; the tracked migrations likewise declare no such index.

These queries are used in copy management and borrow selection. Without an appropriate index the database may scan up to 50k copies per operation. The collection is also unbounded for a single title, so a skewed distribution can send many rows to the desktop at once.

**Smallest next step:** Evaluate a BookId/Status-leading index for per-title lookups and counts; consider server paging if one title can own a large share of all copies. Validate Barcode ordering and include columns with real execution plans.

### P1-04 — Reader profile history has no ReaderId-leading BorrowRecords index

**Evidence:** [BorrowRepository.cs](../../LibraryManagement/Repositories/BorrowRepository.cs#L413) executes three result sets scoped by ReaderId: lifetime/status totals at lines 421–425, recent history at lines 428–437, and active eligibility records at lines 439–441. [ReaderService.cs](../../LibraryManagement/Services/Readers/ReaderService.cs#L229) invokes this when building a reader profile. Tracked BorrowRecords indexes cover BorrowDate, ReturnDate, DueDate, active CopyId, and audit BorrowId, but none begins with ReaderId; see [HistorySchemaMigration.cs](../../LibraryManagement/Data/Migrations/HistorySchemaMigration.cs#L76) and [LibraryDB.sql](../../LibraryDB.sql#L480).

At 100k+ borrow records, opening a profile can require repeated scans of global history to find one reader’s rows. Reader deletion/lifecycle checks also query BorrowRecords by ReaderId.

**Smallest next step:** Evaluate a ReaderId-leading index, likely including Status and the date/book/copy fields needed by profile queries. Recent-history ordering is an expression, so verify whether the index helps that result or if its query shape also needs revision.

### P2-05 — Search issues synchronous SQL work for each typed character

**Evidence:** Books, Readers, History and Return text boxes use UpdateSourceTrigger=PropertyChanged, and their ViewModel setters immediately reload. Examples: [BooksViewModel.cs](../../LibraryManagement/ViewModels/BooksViewModel.cs#L67), [ReadersViewModel.cs](../../LibraryManagement/ViewModels/ReadersViewModel.cs#L24), [HistoryViewModel.cs](../../LibraryManagement/ViewModels/HistoryViewModel.cs#L23), and [ReturnViewModel.cs](../../LibraryManagement/ViewModels/ReturnViewModel.cs#L35). Borrow recommendation setters also call repository search directly ([BorrowViewModel.cs](../../LibraryManagement/ViewModels/BorrowViewModel.cs#L33), [lines 354–401](../../LibraryManagement/ViewModels/BorrowViewModel.cs#L354)). These calls are synchronous in the desktop interaction path.

The Book page query uses contains-pattern matching and issues six schema capability scalar queries before its COUNT and page queries ([BookRepository.cs](../../LibraryManagement/Repositories/BookRepository.cs#L368)). Reader and History pages likewise execute count/data work for each search update. At target sizes, repeated database round trips can block the UI even though the returned page is bounded.

**Smallest next step:** Debounce search and run queries off the UI thread with cancellation/stale-result protection. Cache schema capabilities after startup migrations; keep server-side filtering and paging.

### P2-06 — Borrow book recommendations aggregate available copies before applying TOP

**Evidence:** [BookRepository.cs](../../LibraryManagement/Repositories/BookRepository.cs#L404) joins BookCopies, filters available/non-legacy rows, groups by book, counts copies and only then returns TOP(@Limit). The UI invokes this synchronously on each search change ([BorrowViewModel.cs](../../LibraryManagement/ViewModels/BorrowViewModel.cs#L388)). Existing tracked BookCopies indexing is by barcode, not Status/BookId.

TOP 20 bounds the returned books, but does not bound the copy rows inspected and grouped. At 50k copies, repeated recommendation searches can re-aggregate the available inventory on every typed character.

**Smallest next step:** Use measured plans to evaluate a Status/BookId-leading available-copy index (with Condition available to the query); debounce/cancel repeated search requests.

### P2-07 — History sorts by a computed action date across the filtered history set

**Evidence:** [HistoryRepository.cs](../../LibraryManagement/Repositories/HistoryRepository.cs#L10) issues count, overdue count and page queries. The page computes ActionDate with an OUTER APPLY for the latest audit event, falls back to LostDate/ReturnDate/BorrowDate, then orders by ActionDate before OFFSET/FETCH at [lines 56–68](../../LibraryManagement/Repositories/HistoryRepository.cs#L56). The audit index on BorrowId/OccurredAt helps each latest-event lookup ([LibraryDB.sql](../../LibraryDB.sql#L328)), but it cannot directly provide global order by this COALESCE expression.

At 100k+ history, SQL may need to evaluate the latest event for many matching loans and sort them before returning one page. This is a query-shape risk; actual plan and latency were not measured.

**Smallest next step:** Capture an actual execution plan at target data volume. If the sort/apply dominates, consider an indexable last-action timestamp maintained with circulation events or another query design that preserves current history semantics.

### P2-08 — All Fees and All Notifications lack an index matching global page order

**Evidence:** Fee paging orders every view by CreatedAt DESC, FeeId DESC ([FeeRepository.cs](../../LibraryManagement/Repositories/FeeRepository.cs#L40)); the declared status index starts with Status ([FeeMigration.cs](../../LibraryManagement/Data/Migrations/FeeMigration.cs#L215)). Notification paging orders by CreatedAt DESC, Id DESC ([NotificationPageQuery.cs](../../LibraryManagement/Models/Notifications/NotificationPageQuery.cs#L9)); its declared page index starts with IsRead ([NotificationMigration.cs](../../LibraryManagement/Data/Migrations/NotificationMigration.cs#L39)).

The existing indexes support filtered/status-specific shapes better than the unfiltered All views. At 50k+ rows, All-page retrieval may need an additional sort even though output rows are paged.

**Smallest next step:** Check the actual plan and filter usage; if the All view sorts large sets, evaluate CreatedAt/FeeId and CreatedAt/Id indexes. Account for write/storage cost before adding both.

### P2-09 — Return search is bounded in output but may scan and sort all active loans

**Evidence:** [BorrowRepository.cs](../../LibraryManagement/Repositories/BorrowRepository.cs#L268) filters active loans with Status and ReturnDate, applies contains-pattern matching to computed reader/book/barcode display values, and orders by BorrowDate/BorrowId before TOP. The existing [ActiveDueDate index](../../LibraryManagement/Data/Migrations/HistorySchemaMigration.cs#L88) is keyed by DueDate, so it does not match this return search order.

TOP 100/200 bounds returned rows, not necessarily rows examined or sorted. With up to 50k active copies, repeated free-text searches can read and sort a large active-loan set; substring matching on joined display fields cannot use ordinary prefix indexes.

**Smallest next step:** Evaluate a filtered BorrowDate/BorrowId index for active, unreturned loans. For free-text search, test realistic active-loan volumes and consider a separate exact barcode/ID lookup path versus contains search.

### P2-10 — Due-soon processing writes one notification at a time before showing the main window

**Evidence:** Login awaits CheckDueSoonSafelyAsync before creating MainWindow ([AuthWindow.xaml.cs](../../LibraryManagement/Views/AuthWindow.xaml.cs#L377)). The checker loads all active loans due on the target date, then awaits one event publication per loan in a sequential loop ([DueSoonNotificationChecker.cs](../../LibraryManagement/Services/Notifications/DueSoonNotificationChecker.cs#L25)). Each handler calls NotifyAsync for that loan ([NotificationEvents.cs](../../LibraryManagement/Services/Notifications/NotificationEvents.cs#L58)). The date-range query is supported by the tracked ActiveDueDate index, but result count and notification writes are not batched.

If many loans share a due date, this serial database work delays every login before the app’s main window opens. It remains relevant even with 50k notifications because repeated idempotent checks still visit each matching loan.

**Smallest next step:** Measure login time with a large same-day due cohort. If material, move checking off the critical path and batch or chunk notification upserts while retaining idempotency.

### P3-11 — Books loads all distinct authors into memory for local suggestions

**Evidence:** [BookRepository.cs](../../LibraryManagement/Repositories/BookRepository.cs#L517) returns every distinct author. [BooksViewModel.cs](../../LibraryManagement/ViewModels/BooksViewModel.cs#L373) loads the full list into _allAuthors on load and filters it locally in [UpdateAuthorSuggestions](../../LibraryManagement/ViewModels/BooksViewModel.cs#L654).

At 10k books this is bounded by the catalog’s distinct author count and is unlikely to be a top issue, but it is not a bounded suggestion query.

**Smallest next step:** If author cardinality or load time becomes material, use a server-side TOP suggestion query and fetch options on demand.

## Index candidates to validate

These are candidates from source query shapes, **not instructions to add indexes blindly**. No production SQL Server index inventory or execution plans were available. The “missing” statement means no equivalent index is declared in the tracked base schema/migrations reviewed.

| Candidate | Query shape it may support | Existing tracked equivalent |
|---|---|---|
| BookCopies (BookId, Status, CopyId) | Per-title copy lists, available/active counts and first available copy | None found; only barcode uniqueness is declared. Validate separate key order for Barcode sorting. |
| BookCopies (Status, BookId), optionally filtered to Available and including Condition | Borrow catalog availability grouping by book | None found. |
| BorrowRecords (BookId, BorrowId) filtered to Borrowing and CopyId IS NULL | Legacy open-loan lookup during BookCopy migration | None found; active-copy unique index is keyed by CopyId. |
| BorrowRecords (ReaderId, Status) with profile date/book/copy columns included as justified | Reader profile totals, active eligibility and lifecycle lookups | None found; existing history indexes are date-leading. |
| BorrowRecords (BorrowDate DESC, BorrowId DESC) filtered to Borrowing and ReturnDate IS NULL | Return search ordering | None found; ActiveDueDate is DueDate-leading. |
| Fees (CreatedAt DESC, FeeId DESC) | Unfiltered All fees page | Existing Status_Created index is Status-leading. |
| Notifications (CreatedAt DESC, Id DESC) | Unread and All ordering; especially All without IsRead filter | Existing IsRead_CreatedAt index is IsRead-leading. |

## Bounded paths and dormant unbounded APIs

- Main Books and Readers pages use server-side page queries; Fees, Notifications and History also return server pages.
- Borrow current-loan output and reader suggestions are bounded. Notification page size is capped at 100; fee page size at 200.
- Dashboard reads aggregates and limited recent activity; it does not create an entity list proportional to all history.
- InventoryCheckService uses the SQL aggregate report. The older InventoryCheckRepository.GetReport materializes books/copies/loans and performs nested in-memory comparisons, but no production caller was found.
- Unbounded compatibility APIs remain exposed: BorrowService.GetBorrowingBooks, ReaderService.GetAllReaders/SearchReader, NotificationService.GetAllAsync/GetUnreadAsync, and bulk BookService list/search methods. The audited main UI paths use bounded APIs; these should not be reused for large grids. The Books duplicate-ISBN error path also reloads the full catalog to locate one ID, but only on the exceptional duplicate path.
- User Accounts loads all users for local search. Treat this as bounded by the small internal staff population; if accounts become a large directory, migrate it to server-side search/page.

## Audit method and limits

- Reviewed production ViewModels, repositories, services, schema SQL and migrations for full-table materialization, SQL paging/limits, N+1 queries, client-side filtering, aggregation and sort/filter indexes.
- Assessed paths statically against the requested targets. No build, test suite, live database query, SQL execution plan, concurrent-load test, or UI benchmark was run; this was an audit-only task.
- The local branch already contained modified production and test files before this audit. Those changes were preserved. This audit added only this report and did not modify production source or tests.

