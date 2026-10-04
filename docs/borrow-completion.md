# Borrow completion

## Inventory authority

After BookCopy migration, BookRepository derives Quantity, AvailableQuantity and BorrowedCopies from BookCopies. Circulation and copy status changes do not write the old Books counters. Those columns remain as legacy snapshots for databases that have not been migrated; they are not current inventory authority. Editing the requested total copy count still creates or retires physical copies in one transaction. No automatic drift repair is performed.

Books → Đối soát tồn kho reports legacy counter differences, active loan/copy status inconsistencies and unlinked legacy loans. A counter difference alone is expected when an old snapshot ages; copy/loan inconsistencies require investigation. Reading the report never creates copies, maps loans or updates counters.

## Barcode

Borrow supports barcode textbox → Enter → indexed equality lookup → exact copy and parent book. Only Available and verified copies proceed. Editing the input clears the previously selected copy. Not found, Borrowed, UnderRepair, Lost, Retired and LegacyUnverified are reported explicitly. Final availability, reader eligibility, loan policy and authentication are checked again in BorrowService.

LegacyUnverified copies cannot be returned to circulation by manual status changes. Authenticated legacy loan mapping confirms the selected barcode in the same transaction as copy claim, loan association and audit.

## Transactions and callers

New loans: Reader update lock → authoritative eligibility → loan policy → shared parent lifecycle guard → conditional exact-copy claim → BorrowRecord insert → Audit insert → commit. Failure rolls back copy, record and event. No runtime caller creates loans directly outside BorrowService. Repository insert requires a transaction and physical copy ID; null CopyId remains legacy only.

The parent guard uses a shared HOLDLOCK, not an update lock or counter write. This lets different readers borrow different copies of one title concurrently while preventing the parent from being archived or edited during the transaction. Removing every parent guard would reintroduce the archive/borrow race. Return/Lost use record transition then exact-copy transition; legacy mapping uses copy claim then conditional record association. Inventory checks read Books → Copies → active loans in a short serializable transaction.

TimeProvider.System supplies authoritative time by default. Tests may inject a fixed TimeProvider for borrow dates, due dates, eligibility and audit timestamps.

## Validation

CI already runs unit tests and SQL integration against an isolated LocalDB database. Local SQL Express can be used through `scripts/Test-BorrowSql.ps1`. The fixture accepts only a local master connection and creates uniquely named temporary databases; it never targets the application LibraryDB.

Windows authentication must be available to the process running tests. A running SQL service does not prove the test process can log in. Do not report SQL integration as passed when authentication or fixture setup fails.

This document focuses on physical-copy inventory and borrowing consistency. Current return outcomes and fee behavior are implemented in the Return and Fee modules; renewal and reservations are outside this document's scope.
