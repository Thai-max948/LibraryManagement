# Final Dead Code Audit

Scope: local working tree on `AIfixes`, audited without changing circulation, fee, authentication, authorization, or account business rules.

## Removed

| Symbol / file | Why safe to remove | Evidence |
|---|---|---|
| `BorrowViewModel.HasReaderResults`, `HasBookResults` and their notifications | No C# or XAML consumer; only self-notifications existed. | Searched all ViewModels, views, and tests. |
| `HistoryViewModel.HasRecords`, `RefreshCommand`, `IsLoading` and backing state/notifications | No view, code-behind, main-window, or test consumer. | History view/code-behind and test references inspected; paging/filter commands remain. |
| `FeesViewModel.CurrentPaymentHistory`, its backing field, and `HasFees` | Payment history is exposed through the collection actually used by the view; the view uses `HasNoFees`. | Searched C# and XAML bindings. |
| `DashboardViewModel` legacy aliases (`TotalBooks`, `TotalReaders`, `AvailableBooks`, `CurrentlyBorrowed`, `OverdueBooks`) | No current in-solution binding or caller; current metrics remain. | Dashboard XAML, tests, other view models, and services searched. `Data` was kept because a test reads it. |
| `ReadersViewModel.DeleteCommand` and its UI wrapper | The current Readers view offers View Card, Detail, and Edit only; deletion is not a UI action. | View bindings and code-behind inspected. `ReaderService.DeleteReader`, `ReaderRepository.Delete`, and service tests remain. |
| `UserService.UpdateUser(User)` | No caller, interface requirement, or test contract; account flows use `UpdateAccount` / `UpdateMyProfile`. | Whole-solution search and account flows inspected. |
| `IFeeService.GetFeesByReaderAsync` and `FeeRepository.GetByReaderIdAsync` | No caller, test contract, or documented compatibility contract. | Whole-solution search; `GetFeesByBorrowAsync` and `GetFeeAsync` remain. |
| `BookRepository.Add(Book)` convenience wrapper | No caller; transactional `Add(SqlConnection, SqlTransaction?, Book)` remains in use by `AddWithCopies`. | Whole-solution search. |
| `BookRepository.Delete(int)` hard-delete path and obsolete “never hard-delete” mock assertions | No production caller or compatibility documentation; archive/restore uses `SetArchived`. | Book service and repository callers inspected; archive semantics and tests remain. |
| Unbound `AuthViewModel` command wrappers (`SignInCommand`, `RegisterCommand`, `SwipeToRegisterCommand`, `SwipeToSignInCommand`), `IsRegisterMode`, and `IsLoading` state | No XAML, code-behind, test, or other C# consumer. The view calls the existing execution/transition methods directly. | Whole-solution search; login/register methods, events, password fallbacks, and `ForgotPasswordCommand` remain unchanged. |
| `App.xaml` resource `SidebarBorderBrush` | No `StaticResource` / `DynamicResource` or code consumer. | Full XAML/C# resource-key search. |

## Removed feature residue

No Renewal Fee, Lost Fee, or History date-type selector residue was present in source before this cleanup, so none was removed in this pass. Lost remains a circulation state and lost-book replacement remains `FeeType.Replacement`.

## Kept intentionally

| Symbol / area | Reason |
|---|---|
| `BookCopyRepository.GetFirstAvailableCopyId`, `BookRepository.EnsureCopiesForLegacyBook` | Explicitly retained by `docs/architecture.md` as compatibility APIs. |
| `Books.Quantity`, `Books.AvailableQuantity` and their mappings | Legacy inventory snapshots used for compatibility, migration, and inventory comparison. |
| Migrations and schema guards | Application must still support databases that have not yet been migrated. |
| Audit/history, snapshots, source identifiers, idempotency keys, uniqueness indexes, and transaction/concurrency guards | Preserve historical records, data integrity, retry safety, and concurrent-operation correctness. |
| `ReaderService.DeleteReader` / `ReaderRepository.Delete` | Domain behavior and service tests remain even though the current UI has no delete command. |
| `BookService.DeleteBook` archive behavior and `BookRepository.SetArchived` | Preserve soft-delete/archive semantics. |
| `BookRepository.Update` | Used by `UpdateWithCopies` and a fee foundation test. |
| `DashboardViewModel.Data` | Read by a test that verifies initial-load failure leaves data unset. |

## Needs review

None identified within the solution. External consumers outside this application repository cannot be discovered by a local solution search; no removed symbol had a compatibility note or documented external contract in this project.

## Verification

- Build: `dotnet build LibraryManagement.slnx --configuration Release --no-restore -p:UseSharedCompilation=false -m:1` — passed, 0 warnings, 0 errors.
- Non-SQL tests: blocked before test results. Command: `dotnet test LibraryManagement.Tests/LibraryManagement.Tests.csproj --configuration Release --no-build --no-restore --filter "Category!=Integration" -m:1 --logger "console;verbosity=normal"`. The first attempt reported that the testhost could not connect; a controlled retry produced no test output and was stopped after remaining at startup for over 100 seconds. Pass/fail totals are unavailable.
- SQL integration tests: skipped as permitted by the task; no SQL test result is claimed.
- Final source searches: removed candidate symbols and removed feature tokens have no source references; compatibility APIs above remain. Generated `bin`/`obj` files are excluded from source-residue results.
