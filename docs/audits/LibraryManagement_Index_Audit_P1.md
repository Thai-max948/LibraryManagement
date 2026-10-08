# Existing indexes

This inventory reflects index DDL tracked in `LibraryDB.sql` and `LibraryManagement/Data/*Migration.cs`. The SQL file and migrations repeat some definitions under the same index name; repeated declarations are one logical index, not additional indexes. This is a source audit, not a query of a deployed database.

## BookCopies

| Name | Type / uniqueness | Key columns (order and direction) | INCLUDE columns | Filter |
|---|---|---|---|---|
| Primary key (name is assigned by SQL Server) | Primary key; clustered by SQL Server's default for the declared table | `CopyId ASC` | — | — |
| `UX_BookCopies_Barcode_NotNull` | Unique nonclustered | `Barcode ASC` | — | `Barcode IS NOT NULL` |

`UX_BookCopies_Barcode_NotNull` is declared in `LibraryDB.sql` and `BookCopyBarcodeMigration.cs`. The primary key is declared inline in the table DDL, not as a named `CREATE INDEX` statement. No tracked index has `BookId` or `Status` as a leading key. The barcode index supports barcode equality lookups; it does not support `BookId` filtering.

## BorrowRecords

| Name | Type / uniqueness | Key columns (order and direction) | INCLUDE columns | Filter |
|---|---|---|---|---|
| Primary key (name is assigned by SQL Server) | Primary key; clustered by SQL Server's default for the declared table | `BorrowId ASC` | — | — |
| `IX_BorrowRecords_HistoryBorrowDate` | Nonunique nonclustered | `BorrowDate DESC, BorrowId DESC` | `ReaderId, BookId, CopyId, DueDate, ReturnDate, Status, LostDate` | — |
| `IX_BorrowRecords_HistoryReturnDate` | Nonunique nonclustered | `ReturnDate DESC, BorrowId DESC` | `ReaderId, BookId, CopyId, DueDate, Status` | `ReturnDate IS NOT NULL` |
| `IX_BorrowRecords_ActiveDueDate` | Nonunique nonclustered | `DueDate ASC` | `BorrowId` | `Status = 'Borrowing'` |
| `UX_BorrowRecords_ActiveCopy` | Unique nonclustered | `CopyId ASC` | — | `CopyId IS NOT NULL AND Status = 'Borrowing'` |

The three `IX_...` definitions are in `HistorySchemaMigration.cs`. `UX_BorrowRecords_ActiveCopy` is declared in `LibraryDB.sql` and `BookCopyMigration.cs`. None of these indexes has `ReaderId` as a key column. In the two history indexes, `ReaderId` is only an included column and therefore is not a seek prefix.

# Query paths

## P1-03 BookCopies

| Path | Predicate / join | Ordering | Selected or aggregated data |
|---|---|---|---|
| `BookCopyRepository.GetByBookId` | `BookCopies WHERE BookId = @BookId` | `CopyId` | `CopyId, BookId, Barcode, Status, Condition, CreatedAt` |
| `GetAvailableByBookId` | `BookId = @BookId AND Status = @Status AND Condition <> 'LegacyUnverified'` | `Barcode` | `CopyId, BookId, Barcode, Status, Condition, CreatedAt` |
| `CountByBookId` | `BookId = @BookId` | — | `COUNT(*)` |
| `CountActiveByBookId` | `BookId = @BookId AND Status <> @Retired` | — | `COUNT(*)` |
| `CountAvailableByBookId` | `BookId = @BookId AND Status = @Status` | — | `COUNT(*)` |
| `GetFirstAvailableCopyId` | `BookId = @BookId AND Status = @Status` | `TOP (1) CopyId` | `CopyId` |
| `RetireAvailableCopies` | CTE filters `BookId = @BookId AND Status = @Available` | `TOP (@Count) CopyId DESC`; then updates selected `CopyId`s | `CopyId`, then status update |
| Book detail/catalog metrics | Correlated counts on `BookId = b.BookId` for non-Retired, Available, and Borrowed; catalog metric groups the whole `BookCopies` table by `BookId` and aggregates the same statuses | Grouping by `BookId` | Counts by book and status |
| Borrow book suggestions | `Books b JOIN BookCopies bc ON bc.BookId = b.BookId`; `bc.Status = Available AND bc.Condition <> LegacyUnverified` | Results grouped by book and ranked by book title | `COUNT(*)` available copies per book |
| `BorrowRepository.CountActiveBorrowsByBook` | BorrowRecords joins BookCopies on `bc.CopyId = br.CopyId`; book filter is `COALESCE(bc.BookId, br.BookId) = @BookId`, status is Borrowing | — | `COUNT_BIG(*)` |

The `CountActiveBorrowsByBook` path looks up a copy by its primary key. A `BookCopies(BookId, ...)` index does not directly accelerate that join; its relevance is to the repository and catalog queries above that filter or group copies by `BookId`.

## P1-04 Reader/BorrowRecords

| Path | Predicate / join | Ordering | Selected or aggregated data |
|---|---|---|---|
| `HasActiveBorrowByReader` | `ReaderId = @ReaderId AND Status = 'Borrowing'` | — | `EXISTS` |
| `HasBorrowHistoryByReader` | `ReaderId = @ReaderId` | — | `EXISTS` |
| Reader profile summary | `ReaderId = @ReaderId`; aggregate expressions test `UPPER(Status) = 'BORROWING'` and `DueDate < @AsOfDate` | — | total, active, and overdue `COUNT_BIG` values |
| Reader profile recent history | `ReaderId = @ReaderId`; joins Books by `BookId` | `ISNULL(ReturnDate, BorrowDate) DESC, BorrowId DESC` | `BorrowId, BookId` for title lookup, `BorrowDate, DueDate, ReturnDate, Status` |
| Reader profile eligibility result | `ReaderId = @ReaderId AND UPPER(Status) = 'BORROWING'` | — | `BorrowId, ReaderId, DueDate, Status` |
| `GetEligibilityRecords` / `CountActiveBorrowsByReader` | `ReaderId = @ReaderId AND Status = 'Borrowing'` | — | eligibility rows (`BorrowId, ReaderId, DueDate, Status`) or count |
| `HasUnresolvedLostByReader` | `ReaderId = @ReaderId AND Status = 'Lost'` | — | `EXISTS` |
| `GetHistory` when a reader is selected | Optional `ReaderId = @ReaderId`; may also filter by BookId, Status, and a `BorrowDate` range | `ISNULL(ReturnDate, BorrowDate) DESC, BorrowId DESC` | BorrowRecord fields plus joined book/copy display data |

`ReaderService` uses the optimized profile query and active-loan existence checks. `ReaderEligibilityService` uses the reader eligibility rows. In profile SQL, `UPPER(Status)` is applied to the column, so SQL Server cannot use `Status` as a direct equality seek predicate for those particular expressions. A composite index can still seek on the leading `ReaderId`, then evaluate status and due-date conditions over that reader's range.

# Candidate comparison

## BookCopies candidates

| Candidate | Queries helped | Overlap | Write cost | Recommendation |
|---|---|---|---|---|
| A: `(BookId, Status, CopyId)` | Exact book/status counts and lookups; available-copy `TOP` queries ordered by `CopyId`; status counts grouped by book | Serves the same Available subset as filtered candidate C. The table declares a `CopyId` primary key, which SQL Server normally appends as the clustered row locator to a nonunique nonclustered index; explicitly repeating it may not add useful coverage. | Medium: each copy insert adds an entry; status transitions change an indexed key. | **RECOMMENDED in reduced form:** `(BookId, Status)`. With the declared clustered `CopyId` primary key, its leaf row locator supplies the `CopyId` tie-break for queries fixing both leading keys. Confirm the deployed primary-key clustering before relying on that ordering detail. |
| B: `(Status, BookId, CopyId)` | Status-first scans, such as extracting Available copies across all books | Does not seek efficiently for common `BookId = @BookId` paths without a status filter. Overlaps status-specific lookups but sacrifices the frequent book-leading access pattern. | Medium: inserts and status transitions maintain the index. | **DO NOT ADD.** It does not fit the range of current per-book queries as well as a BookId-leading index. |
| C: filtered `(BookId, CopyId) WHERE Status = 'Available'` | Available counts, first-available lookup, and available-copy selection ordered by `CopyId`; smaller than an unfiltered index because it contains only available copies | Overlaps candidate A's Available lookups. It does not support all-copy/retired/status counts or the whole-table grouped metrics. `GetAvailableByBookId` orders by `Barcode`, so this key does not remove that sort. | Medium: each transition into/out of Available inserts/deletes an index entry. | **OPTIONAL alternative only if measured Available-copy paths dominate. Do not add alongside the recommended index without plan/usage evidence.** |

For a minimal set, candidate A reduced to `(BookId, Status)` covers the recurring per-book/status counts, available selection by `CopyId`, and narrow grouped status aggregation. It seeks by `BookId` for the all-status queries, but `GetByBookId ORDER BY CopyId` can still require sorting across statuses. `GetAvailableByBookId ORDER BY Barcode` can still require a sort. No barcode-ordered filtered index is recommended without evidence that this specific sort is a bottleneck.

## BorrowRecords reader candidates

| Candidate | Queries helped | Overlap | Write cost | Recommendation |
|---|---|---|---|---|
| `(ReaderId, Status) INCLUDE (DueDate)` | Reader-only lookup via the `ReaderId` prefix; active/lost existence and count queries with exact status; reader profile totals/active/overdue aggregates; eligibility rows, including `DueDate` | No key-prefix overlap with existing BorrowDate, ReturnDate, due-date, or active-CopyId indexes. `BorrowId` is already the declared primary key and is normally carried as the clustered row locator. | Medium: every borrow insert adds an entry; return/lost status transitions change the key. `DueDate` is included for overdue/profile and eligibility reads. | **RECOMMENDED.** Smallest reader/status index that covers the important active eligibility and overdue columns without including full history rows. |
| `(ReaderId, BorrowId)` | Reader exact lookup and potentially ordering by BorrowId within a reader | `BorrowId` is the clustered primary key/row locator. It does not cover status-based paths, and it does not satisfy the effective-date ordering used by reader history. | Medium: one entry per borrow insert; redundant storage/key work relative to the recommended index. | **DO NOT ADD.** ReaderId-leading status index already seeks by ReaderId; explicit BorrowId duplicates the row locator and does not solve the history sort. |
| `(ReaderId, BorrowDate)` | Reader-specific history constrained by a BorrowDate range; could provide BorrowDate order for a different query shape | Shares the `ReaderId` prefix with the recommended index. Current recent-history order uses an expression that can choose ReturnDate instead of BorrowDate. | Medium: one entry per borrow insert; additional storage and maintenance. | **OPTIONAL only if plans show reader-scoped BorrowDate-range history is a hot path. Not justified in the minimum set.** |

The proposed reader index can locate a reader's records and directly apply exact status predicates. It does **not** eliminate the sort for:

```sql
ORDER BY ISNULL(ReturnDate, BorrowDate) DESC, BorrowId DESC
```

The expression chooses `ReturnDate` for returned records and `BorrowDate` otherwise. Existing global `BorrowDate` and `ReturnDate` indexes cannot provide this single expression order, and `ReaderId` is included rather than a leading key in both. Even `(ReaderId, BorrowDate)` would only order by BorrowDate and would still need a sort for the expression when return dates are present.

# Final proposed minimal set

1. `BookCopies (BookId ASC, Status ASC)` — nonunique, unfiltered, no INCLUDE columns. The declared `CopyId` primary key is the clustered locator under the DDL default and can provide the tie-break order when both indexed columns are equality-constrained. If the deployed schema made `CopyId` nonclustered, reassess whether explicit `CopyId` as a third key is needed for available `TOP` queries.
2. `BorrowRecords (ReaderId ASC, Status ASC) INCLUDE (DueDate)` — nonunique, unfiltered. This covers reader lookup, exact active/lost-status paths, and profile/eligibility due-date reads. It does not promise sort elimination for recent history.

Priority: both are **RECOMMENDED**, not proven mandatory. This is a **static recommendation only** for the stated scale (about 10k books, 50k copies, 10k readers, and 100k+ borrow records). It is not based on measured execution plans or workload statistics.

# Risks / unknowns

- No live SQL Server connection, deployed-schema metadata, actual execution plan, or index-usage statistics were available in this audit. Seek choice, sort elimination, latency, and percentage cost changes are unverified.
- The tracked DDL may differ from the user's deployed database or migration state. Verify clustered-primary-key details and actual index presence against the target database before deployment.
- `UPPER(Status)` in two reader-profile predicates is non-sargable for a direct Status seek. The proposed index still supports a ReaderId range scan, but the exact status filter may be evaluated per row.
- `ISNULL(ReturnDate, BorrowDate)` recent-history ordering can still require a sort; the proposed index only narrows records to one reader. Do not describe a simple ReaderId index as solving this expression sort.
- `GetAvailableByBookId` requests `ORDER BY Barcode`; BookId/status indexes support filtering but do not provide that barcode ordering. The existing unique barcode index is keyed by Barcode, not BookId.
- Every additional index consumes storage and adds insert/update work. BookCopies changes status in borrow, return, damage, loss, repair, and retire flows; BorrowRecords inserts on borrow and changes status on return/loss. This favors the two-index minimum over status-specific duplicates.
- Legacy borrow-count fallback queries filtering `BorrowRecords.BookId` are outside these two candidates and are not evidence for a BookCopies BookId index.

# Scope and repository safety

Audit-only artifact. No indexes were created, and no production source, migration, or test file was changed. Existing local scalability changes were preserved. No reset, checkout, clean, commit, or push was run.
