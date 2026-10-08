# 1. Final Status

Code-level scalability roadmap completed for the current local working tree. This closure records the completed source-level work and verification boundaries; it does **not** claim that target-scale runtime performance has been proven.

No production source, XAML, SQL, project, test, or migration file was changed for this closure. Existing local work on branch `AIfixes` was preserved. The only file created for this task is this report.

# 2. Completed Improvements

| Area | Old behavior | New behavior | Scale benefit |
|---|---|---|---|
| Books | Catalog results and search/filter operations could require broad client-side handling. | SQL-side search, advanced filters, sort, and paging; free-text input is debounced. | Keeps each result page bounded and avoids querying on every keystroke. |
| Readers | Reader listing/search could load or process broad result sets in the UI. | SQL paging and bounded Borrow search; type/status filters and sorting use the page query. | Limits rows returned to the desktop and narrows reader lookups. |
| Reader detail and lifecycle | Profile history and lifecycle guards risked repeated/full history reads. | Profile uses a scoped projection; delete/deactivate guards use existence checks. | Avoids loading unrelated history or materializing rows just to answer yes/no. |
| Borrow | Reader/book suggestions and current-borrowing display could use broad catalog/history data. | Suggestions are bounded; current borrowings use a dedicated projection; barcode Enter remains exact and immediate. | Bounds suggestion and display payloads while retaining exact barcode lookup. |
| Return | Search could depend on broad reader/book data and manual filtering. | Active-loan search returns a bounded projection; exact barcode lookup remains available. | Limits returned records and avoids loading full catalogs for normal search. |
| History | History listing/search could process records in the UI. | SQL-side search, filters, and paging return a bounded page. | Keeps page payload bounded; effective activity-date sorting remains a measured/deferred concern. |
| Fees | Fee list could require unbounded result retrieval. | Fee list is server-paged. | Limits rows displayed per request; All-mode database cost remains unmeasured. |
| Notifications | Notification center history could load broad lists. | Center requests a bounded latest page, supports server-side All/Unread filtering, and reads the authoritative global unread count separately. | Bounds history payload while preserving the badge count. |
| Inventory check | Reconciliation could materialize copies and loans for application-side comparison. | Reconciliation uses a SQL-aggregated report and filters review rows. | Avoids moving the full copy/loan population into application memory. |
| Startup migration | Legacy copy reconciliation could repeat on later logins. | Durable `BookCopyBackfillV1` completion marker skips the completed reconciliation loop while schema guards still run. | Avoids repeating completed backfill work at startup. |
| Book edit | Active loans could be materialized to validate a book update. | Update validation uses a scalar active-borrow count by book. | Returns a count rather than loading active-loan entities. |

These are source-level behavior descriptions, not measured latency or capacity claims.

# 3. Database Indexes Added

The fresh schema and migration paths contain exactly these two scalability indexes:

1. `IX_BookCopies_BookId_Status` on `dbo.BookCopies (BookId, Status)`.
2. `IX_BorrowRecords_ReaderId_Status` on `dbo.BorrowRecords (ReaderId, Status) INCLUDE (DueDate)`.

Static inspection confirmed the BookCopy migration creates its index before checking the completed backfill marker and returning. The marker skips the reconciliation loop, not schema/index guards. No index was added during this closure.

# 4. Search Responsiveness

Static inspection confirmed 300 ms free-text debounce in all six paths:

- Books
- Readers
- Borrow Reader suggestions
- Borrow Book suggestions
- Return
- History

Explicit actions remain immediate: barcode Enter lookup; filter changes/apply/clear; sort changes; page navigation; page-size changes; CRUD refresh; and Borrow/Return operation refresh. Barcode lookup cancels a pending free-text search before performing the exact lookup.

# 5. Deferred / Unverified Items

| Item | Status and reason | Evidence needed to reopen |
|---|---|---|
| P2-06 Borrow availability | **INSUFFICIENT DATA.** No representative SQL benchmark was available to measure broad/selective Borrow searches, available-copy aggregation, or actual query-plan behavior. | At representative catalog/copy volume, capture broad and selective query timings, reads/IO, CPU, actual execution plans, and whether the existing copy index is used effectively. |
| P2-07 History effective ordering | **DEFERRED.** History ordering derives an effective activity date that may use the latest audit event; paging bounds output but does not establish the cost of sorting the filtered population. | At representative history volume, capture the actual plan and latency, including sort/apply cost and rows processed, and demonstrate material user-visible delay. |
| P2-08 Fees / Notifications All mode | **INSUFFICIENT DATA.** Fee and notification count/page requests and the unread-count query were not run against representative SQL data. | Measure All-mode count and page queries separately for Fees and Notifications; measure Unread page and global unread count separately; capture actual plans, reads, and elapsed time at representative row counts. |
| P2-09 Return contains search / FTS | **DEFERRED.** Contains matching over joined display fields can scan rows despite the result limit; no representative active-loan benchmark was available to show that this is a material problem. | Benchmark exact barcode and ordinary contains searches at representative active-loan volume, recording rows read, plan, sort cost, and user-visible latency. Reopen only if evidence shows a material issue. |
| P2-10 DueSoon login path | **INSUFFICIENT DATA.** The SQL query, notification writes, and login-to-main timing were not measured with a representative same-day due cohort. | In a disposable QA database, measure the due-date query, sequential notification writes, and login-to-main time for a representative due cohort. Keep idempotency behavior in the measured flow. |

The benchmark environment had no usable SQL Server connection. These items remain inconclusive/deferred; this report makes no runtime performance determination and does not change their implementation.

# 6. Verification

- **Application Release build:** `dotnet build LibraryManagement\LibraryManagement.csproj -c Release` — succeeded, 0 warnings, 0 errors.
- **Test project Release build:** `dotnet build LibraryManagement.Tests\LibraryManagement.Tests.csproj -c Release` — succeeded, 0 warnings, 0 errors.
- **Test execution:** Attempted the pure non-SQL group `NotificationPageQueryTests`. Testhost matched one test assembly but produced no test result, count, or completion after about 55 seconds. The runner was stopped as instructed. **TEST EXECUTION BLOCKED BY TESTHOST ENVIRONMENT.**
- **SQL integration / benchmark:** Not run, as explicitly excluded from this closure. No SQL environment or connection configuration was changed.
- **Static regression audit:** Passed for the requested consumer paths: BorrowViewModel avoids full reader/book preloads and full borrowing-list display; ReturnViewModel avoids full catalog loads for normal search; ReaderService profile uses scoped borrow data rather than full history/per-row book lookup; reader lifecycle uses existence checks; InventoryCheckService uses `GetAggregatedReport`; NotificationViewModel uses paged history and a separate unread count; BookService book updates use `CountActiveBorrowsByBook`; BookCopy migration marker leaves schema/index guards ahead of the backfill fast path.
- **Git safety checks:** Branch is `AIfixes`. Existing local changes remain present. `git diff --check` reported no whitespace errors; Git printed existing LF-to-CRLF conversion warnings for dirty files. The closure report is the only new file from this task.
- **Manual QA:** Not run in this source/report-only closure. Use this checklist in the application with suitable test data:

## Manual QA Checklist

- [ ] **Login:** Sign in; verify login completes and MainWindow opens.
- [ ] **Books:** Open the list; search; apply filters; navigate pages; edit a book; open Manage Copies.
- [ ] **Readers:** Search; use type/status filters; navigate pages; open Reader Detail; verify edit, deactivate, and delete guards.
- [ ] **Borrow:** Search Reader; search Book; use exact barcode lookup; select a physical copy; complete a borrow; verify Current Borrowings refresh.
- [ ] **Return:** Search; use exact barcode lookup; perform a normal return; perform a damaged return; mark a copy lost.
- [ ] **History:** Search by text; filter by BorrowDate range; navigate pages.
- [ ] **Fees:** Navigate the paged list; inspect a fee; exercise payment flow if suitable test data permits.
- [ ] **Notifications:** Switch All/Unread; mark one notification read; mark all read; verify the unread badge.
- [ ] **Inventory check:** Open the report and verify that reconciliation does not automatically repair data.

# 7. Practical Scalability Assessment

The architecture and query paths are designed for approximately 10k Readers, 10k Books, 50k BookCopies, 100k+ BorrowRecords, 50k Fees, and 50k Notifications. These target sizes have **not** been load/performance proven because representative SQL benchmark data was unavailable. Design intent must not be read as a measured capacity guarantee.

# 8. Reopen Criteria

Reopen scalability work only when at least one of these is observed and documented:

- Actual UI latency is observed at a representative workload.
- An actual execution plan shows an expensive scan or sort on a relevant path.
- Memory grows materially during representative usage.
- A benchmark at representative data size exceeds the application's acceptable latency.

Preserve the current local working tree and gather that evidence before selecting another optimization.
