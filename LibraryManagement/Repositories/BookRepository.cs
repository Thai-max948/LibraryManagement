    using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class BookRepository
    {
        public virtual List<Book> GetAll()
            => GetList(archived: false, includeArchived: false);

        public virtual List<Book> GetArchived()
            => GetList(archived: true, includeArchived: false);

        public virtual List<Book> GetAllIncludingArchived()
            => GetList(archived: false, includeArchived: true);

        private List<Book> GetList(bool archived, bool includeArchived)
        {
            var books = new List<Book>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                bool hasStatus = BookArchiveMigration.HasSchema(conn);
                string sql = BookSelect(conn, null) + " FROM Books b"
                    + (includeArchived || !hasStatus ? string.Empty : " WHERE b.Status = @Status");
                if (archived && !hasStatus) return books;
                using (var cmd = new SqlCommand(sql, conn))
                {
                if (!includeArchived && hasStatus) cmd.Parameters.AddWithValue("@Status", archived ? BookStatuses.Archived : BookStatuses.Active);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        books.Add(BookDataMapper.Map(reader));
                    }
                }
                }
            }
            return books;
        }

        public virtual Book? GetById(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                return GetById(conn, null, id);
            }
        }

        public virtual Book? GetByIsbn(string isbn)
        {
            using var connection = Database.GetConnection();
            connection.Open();
            if (!BookIsbnMigration.HasSchema(connection)) return null;
            using var command = new SqlCommand(BookSelect(connection, null) + " FROM dbo.Books b WHERE b.ISBN = @ISBN", connection);
            command.Parameters.AddWithValue("@ISBN", isbn);
            using var reader = command.ExecuteReader();
            return reader.Read() ? BookDataMapper.Map(reader) : null;
        }

        public virtual Book? GetById(SqlConnection conn, SqlTransaction? tran, int id)
        {
            string sql = BookSelect(conn, tran) + " FROM Books b WHERE b.BookId = @BookId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return BookDataMapper.Map(reader);
                    }
                }
            }
            return null;
        }

        public virtual void LockBookRow(SqlConnection connection, SqlTransaction transaction, int bookId)
        {
            using var command = new SqlCommand("SELECT BookId FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) WHERE BookId = @BookId", connection, transaction);
            command.Parameters.AddWithValue("@BookId", bookId);
            command.ExecuteScalar();
        }

        // Shared lifecycle guard: borrows of different copies may run together;
        // archive/edit waits until circulation commits. Do not read inventory here.
        public virtual string? GetCirculationStatus(SqlConnection connection, SqlTransaction transaction, int bookId)
        {
            bool hasStatus = BookArchiveMigration.HasSchema(connection, transaction);
            using var command = new SqlCommand(hasStatus
                ? "SELECT Status FROM dbo.Books WITH (HOLDLOCK, ROWLOCK) WHERE BookId = @BookId"
                : "SELECT CAST('Active' AS NVARCHAR(20)) FROM dbo.Books WITH (HOLDLOCK, ROWLOCK) WHERE BookId = @BookId",
                connection, transaction);
            command.Parameters.AddWithValue("@BookId", bookId);
            return command.ExecuteScalar() as string;
        }

        public virtual int Add(SqlConnection conn, SqlTransaction? tran, Book book)
        {
            BookIsbnMigration.Apply(conn, tran);
            BookArchiveMigration.Apply(conn, tran);
            BookMetadataMigration.Apply(conn, tran);
            BookValueMigration.Apply(conn, tran);
            BookAuditMigration.Apply(conn, tran);
            string sql = @"INSERT INTO Books (Title, Author, Category, Publisher, Language, PublishYear, Quantity, AvailableQuantity, ISBN, ReplacementValue, RentalPrice)
                           OUTPUT INSERTED.BookId
                           VALUES (@Title, @Author, @Category, @Publisher, @Language, @PublishYear, @Quantity, @AvailableQuantity, @ISBN, @ReplacementValue, @RentalPrice)";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@Title", book.Title);
                cmd.Parameters.AddWithValue("@Author", book.Author);
                cmd.Parameters.AddWithValue("@Category", (object)book.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Publisher", (object?)book.Publisher ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Language", (object?)book.Language ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PublishYear", book.PublishYear);
                cmd.Parameters.AddWithValue("@Quantity", book.Quantity);
                cmd.Parameters.AddWithValue("@AvailableQuantity", book.AvailableQuantity);
                cmd.Parameters.AddWithValue("@ISBN", (object?)book.Isbn ?? DBNull.Value);
                AddReplacementValue(cmd, book.ReplacementValue);
                AddRentalPrice(cmd, book.RentalPrice);
                return (int)cmd.ExecuteScalar();
            }
        }

        public virtual int AddWithCopies(SqlConnection conn, SqlTransaction? tran, Book book, int copyCount)
        {
            int bookId = Add(conn, tran, book);
            var copies = new BookCopyRepository();
            if (copies.HasSchema(conn, tran)) copies.AddCopies(conn, tran, bookId, copyCount);
            return bookId;
        }

        public virtual int AddWithCopies(Book book, int copyCount)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                int id = AddWithCopies(conn, tran, book, copyCount);
                tran.Commit();
                return id;
            }
            catch { tran.Rollback(); throw; }
        }

        public virtual int EnsureCopiesForLegacyBook(SqlConnection conn, SqlTransaction? tran, int bookId, int quantity, int availableQuantity)
        {
            var copies = new BookCopyRepository();
            int existing = copies.CountByBookId(conn, tran, bookId);
            int active = copies.CountActiveByBookId(conn, tran, bookId);
            if (existing == 0)
            {
                using var unresolvedCommand = new SqlCommand(@"SELECT COUNT(*) FROM dbo.BorrowRecords
                    WHERE BookId = @BookId AND Status = 'Borrowing' AND CopyId IS NULL", conn, tran);
                unresolvedCommand.Parameters.AddWithValue("@BookId", bookId);
                int unresolvedLoans = Convert.ToInt32(unresolvedCommand.ExecuteScalar());
                if (unresolvedLoans > quantity)
                    throw new BusinessRuleException("Số phiếu mượn cũ chưa đối chiếu lớn hơn số bản sách. Cần kiểm tra dữ liệu trước khi chỉnh sửa.");
                copies.AddCopies(conn, tran, bookId, quantity);
                int toQuarantine = Math.Max(unresolvedLoans, Math.Clamp(quantity - availableQuantity, 0, quantity));
                if (toQuarantine > 0)
                {
                    using var quarantine = new SqlCommand(@"WITH Candidates AS (
                        SELECT TOP (@Count) CopyId FROM dbo.BookCopies
                        WHERE BookId = @BookId AND Status = 'Available' ORDER BY CopyId
                    )
                    UPDATE dbo.BookCopies SET Status = 'UnderRepair', Condition = 'LegacyUnverified'
                    WHERE CopyId IN (SELECT CopyId FROM Candidates)", conn, tran);
                    quarantine.Parameters.AddWithValue("@Count", toQuarantine);
                    quarantine.Parameters.AddWithValue("@BookId", bookId);
                    if (quarantine.ExecuteNonQuery() != toQuarantine)
                        throw new BusinessRuleException("Không thể giữ đủ bản sách cũ để đối chiếu.");
                }
                return quantity;
            }

            if (active < quantity)
            {
                copies.AddCopies(conn, tran, bookId, quantity - active);
            }
            else if (active > quantity)
            {
                int excess = active - quantity;
                if (copies.RetireAvailableCopies(conn, tran, bookId, excess) != excess)
                    throw new BusinessRuleException("Không thể giảm số lượng vì không đủ bản sách đang có sẵn để chuyển sang Ngừng lưu hành.");
            }
            using (var unresolved = new SqlCommand(@"SELECT COUNT(*) FROM dbo.BorrowRecords
                WHERE BookId = @BookId AND Status = 'Borrowing' AND CopyId IS NULL", conn, tran))
            using (var reserved = new SqlCommand(@"SELECT COUNT(*) FROM dbo.BookCopies
                WHERE BookId = @BookId AND Status = 'UnderRepair' AND Condition = 'LegacyUnverified'", conn, tran))
            {
                unresolved.Parameters.AddWithValue("@BookId", bookId);
                reserved.Parameters.AddWithValue("@BookId", bookId);
                int missing = Convert.ToInt32(unresolved.ExecuteScalar()) - Convert.ToInt32(reserved.ExecuteScalar());
                if (missing > 0)
                {
                    using var quarantine = new SqlCommand(@"WITH Candidates AS (
                        SELECT TOP (@Count) CopyId FROM dbo.BookCopies
                        WHERE BookId = @BookId AND Status = 'Available' ORDER BY CopyId
                    )
                    UPDATE dbo.BookCopies SET Status = 'UnderRepair', Condition = 'LegacyUnverified'
                    WHERE CopyId IN (SELECT CopyId FROM Candidates)", conn, tran);
                    quarantine.Parameters.AddWithValue("@Count", missing);
                    quarantine.Parameters.AddWithValue("@BookId", bookId);
                    if (quarantine.ExecuteNonQuery() != missing)
                        throw new BusinessRuleException("Không thể giữ đủ bản sách cũ để đối chiếu.");
                }
            }
            return Math.Max(0, quantity - active);
        }

        public virtual bool UpdateWithCopies(Book book)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                if (BookArchiveMigration.HasSchema(conn, tran))
                {
                    using var state = new SqlCommand("SELECT Status FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) WHERE BookId = @BookId", conn, tran);
                    state.Parameters.AddWithValue("@BookId", book.BookId);
                    if ((string?)state.ExecuteScalar() == BookStatuses.Archived)
                        throw new BusinessRuleException("Cần khôi phục đầu sách trước khi chỉnh sửa.");
                }
                if (!Update(conn, tran, book))
                {
                    tran.Rollback();
                    return false;
                }
                if (new BookCopyRepository().HasSchema(conn, tran))
                    EnsureCopiesForLegacyBook(conn, tran, book.BookId, book.Quantity, book.AvailableQuantity);
                tran.Commit();
                return true;
            }
            catch { tran.Rollback(); throw; }
        }

        public virtual int EnsureCopiesForLegacyBook(int bookId, int quantity, int availableQuantity)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                int added = EnsureCopiesForLegacyBook(conn, tran, bookId, quantity, availableQuantity);
                tran.Commit();
                return added;
            }
            catch { tran.Rollback(); throw; }
        }

        public virtual bool Update(Book book)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                return Update(conn, null, book);
            }
        }

        public virtual bool Update(SqlConnection conn, SqlTransaction? tran, Book book)
        {
            BookIsbnMigration.Apply(conn, tran);
            BookArchiveMigration.Apply(conn, tran);
            BookMetadataMigration.Apply(conn, tran);
            BookValueMigration.Apply(conn, tran);
            BookAuditMigration.Apply(conn, tran);
            string sql = @"UPDATE Books SET
                               Title = @Title,
                               Author = @Author,
                               Category = @Category,
                               Publisher = @Publisher,
                               Language = @Language,
                               PublishYear = @PublishYear,
                               Quantity = @Quantity,
                               AvailableQuantity = @AvailableQuantity,
                               UpdatedAt = SYSUTCDATETIME(),
                               ISBN = @ISBN
                               , ReplacementValue = @ReplacementValue
                               , RentalPrice = @RentalPrice
                           WHERE BookId = @BookId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", book.BookId);
                cmd.Parameters.AddWithValue("@Title", book.Title);
                cmd.Parameters.AddWithValue("@Author", book.Author);
                cmd.Parameters.AddWithValue("@Category", (object)book.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Publisher", (object?)book.Publisher ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Language", (object?)book.Language ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PublishYear", book.PublishYear);
                cmd.Parameters.AddWithValue("@Quantity", book.Quantity);
                cmd.Parameters.AddWithValue("@AvailableQuantity", book.AvailableQuantity);
                cmd.Parameters.AddWithValue("@ISBN", (object?)book.Isbn ?? DBNull.Value);
                AddReplacementValue(cmd, book.ReplacementValue);
                AddRentalPrice(cmd, book.RentalPrice);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public virtual bool SetArchived(int id, bool archived)
        {
            using var connection = Database.GetConnection();
            connection.Open();
            BookArchiveMigration.Apply(connection);
            using var transaction = connection.BeginTransaction();
            try
            {
                string? current;
                int quantity;
                using (var read = new SqlCommand("SELECT Status, Quantity FROM dbo.Books WITH (UPDLOCK, HOLDLOCK) WHERE BookId = @BookId", connection, transaction))
                {
                    read.Parameters.AddWithValue("@BookId", id);
                    using var reader = read.ExecuteReader();
                    if (!reader.Read()) { transaction.Rollback(); return false; }
                    current = reader.GetString(0);
                    quantity = reader.GetInt32(1);
                }
                if (current == (archived ? BookStatuses.Archived : BookStatuses.Active))
                    throw new BusinessRuleException(archived ? "Đầu sách đã được lưu trữ." : "Đầu sách đang hoạt động.");
                if (archived)
                {
                    using var loans = new SqlCommand("SELECT COUNT(*) FROM dbo.BorrowRecords WHERE BookId = @BookId AND Status = 'Borrowing'", connection, transaction);
                    loans.Parameters.AddWithValue("@BookId", id);
                    if (Convert.ToInt32(loans.ExecuteScalar()) > 0)
                        throw new BusinessRuleException("Không thể lưu trữ đầu sách khi còn phiếu mượn đang mở.");

                    if (new BookCopyRepository().HasSchema(connection, transaction))
                    {
                        using var states = new SqlCommand(@"SELECT Status, COUNT(*) FROM dbo.BookCopies
                            WHERE BookId = @BookId AND Status <> @Retired GROUP BY Status ORDER BY Status", connection, transaction);
                        states.Parameters.AddWithValue("@BookId", id);
                        states.Parameters.AddWithValue("@Retired", BookCopyStatuses.Retired);
                        using var reader = states.ExecuteReader();
                        var blockers = new List<string>();
                        while (reader.Read()) blockers.Add($"{reader.GetString(0)}: {reader.GetInt32(1)}");
                        if (blockers.Count > 0)
                            throw new BusinessRuleException("Không thể lưu trữ: còn bản sách chưa ngừng lưu hành (" + string.Join(", ", blockers) + ").");
                    }
                    else if (quantity > 0)
                        throw new BusinessRuleException($"Không thể lưu trữ: còn {quantity} bản sách chưa ngừng lưu hành.");
                }
                using var update = new SqlCommand(@"UPDATE dbo.Books SET Status = @Status,
                    ArchivedAt = CASE WHEN @Status = 'Archived' THEN SYSUTCDATETIME() ELSE NULL END
                    WHERE BookId = @BookId", connection, transaction);
                update.Parameters.AddWithValue("@BookId", id);
                update.Parameters.AddWithValue("@Status", archived ? BookStatuses.Archived : BookStatuses.Active);
                if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Book status could not be updated.");
                transaction.Commit();
                return true;
            }
            catch { transaction.Rollback(); throw; }
        }

        public virtual List<Book> Search(string keyword)
            => SearchByStatus(keyword, archived: false);

        public virtual List<Book> SearchArchived(string keyword)
            => SearchByStatus(keyword, archived: true);

        public virtual PagedResult<Book> GetPaged(BookSearchQuery query)
        {
            if (query.PageNumber < 1) throw new ArgumentOutOfRangeException(nameof(query.PageNumber));
            if (query.PageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(query.PageSize));

            using var connection = Database.GetConnection();
            connection.Open();
            bool hasStatus = BookArchiveMigration.HasSchema(connection);
            bool hasIsbn = BookIsbnMigration.HasSchema(connection);
            bool hasPublisher = BookMetadataMigration.HasPublisher(connection);
            bool hasLanguage = BookMetadataMigration.HasLanguage(connection);
            bool hasReplacementValue = BookValueMigration.HasSchema(connection);
            bool hasCreatedAt = BookAuditMigration.HasCreatedAt(connection);
            if (query.Status == BookStatusFilter.Archived && !hasStatus)
                return new PagedResult<Book>(Array.Empty<Book>(), 0, 1, query.PageSize);

            string where = BuildPagedWhere(query, hasStatus, hasIsbn, hasPublisher, hasLanguage, hasReplacementValue);
            string orderBy = BuildPagedOrderBy(query, hasCreatedAt);
            using var countCommand = new SqlCommand($"SELECT COUNT(*) FROM dbo.Books b {where}", connection);
            AddPagedFilters(countCommand, query, hasStatus, hasIsbn);
            int totalCount = Convert.ToInt32(countCommand.ExecuteScalar());
            int totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
            int pageNumber = Math.Min(query.PageNumber, totalPages);

            string sql = BookSelect(connection, null) + $" FROM dbo.Books b {where} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
            using var pageCommand = new SqlCommand(sql, connection);
            AddPagedFilters(pageCommand, query, hasStatus, hasIsbn);
            pageCommand.Parameters.AddWithValue("@Offset", (long)(pageNumber - 1) * query.PageSize);
            pageCommand.Parameters.AddWithValue("@PageSize", query.PageSize);
            var books = new List<Book>(query.PageSize);
            using (var reader = pageCommand.ExecuteReader())
                while (reader.Read()) books.Add(BookDataMapper.Map(reader));

            return new PagedResult<Book>(books, totalCount, pageNumber, query.PageSize);
        }

        public virtual IReadOnlyList<BookFilterOption> GetCategoryFilterOptions()
        {
            var options = new List<BookFilterOption>
            {
                new(BookFilterCodes.All, "All categories"),
                new(BookFilterCodes.Uncategorized, "Uncategorized")
            };
            using var connection = Database.GetConnection();
            connection.Open();
            using var command = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(Category)) AS Category
                FROM dbo.Books
                WHERE Category IS NOT NULL AND LTRIM(RTRIM(Category)) <> ''
                ORDER BY Category", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string category = reader.GetString(0);
                options.Add(new BookFilterOption(category, category));
            }
            return options;
        }

        public virtual IReadOnlyList<BookFilterOption> GetPublisherFilterOptions()
        {
            var options = new List<BookFilterOption> { new(BookFilterCodes.All, "All publishers") };
            using var connection = Database.GetConnection();
            connection.Open();
            if (!BookMetadataMigration.HasPublisher(connection)) return options;

            using var command = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(Publisher)) AS Publisher
                FROM dbo.Books
                WHERE Publisher IS NOT NULL AND LTRIM(RTRIM(Publisher)) <> ''
                ORDER BY LTRIM(RTRIM(Publisher))", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string publisher = reader.GetString(0);
                options.Add(new BookFilterOption(publisher, publisher));
            }
            return options;
        }

        public virtual IReadOnlyList<string> GetDistinctAuthors()
        {
            var authors = new List<string>();
            using var connection = Database.GetConnection();
            connection.Open();
            using var command = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(Author)) AS Author
                FROM dbo.Books
                WHERE Author IS NOT NULL AND LTRIM(RTRIM(Author)) <> ''
                ORDER BY Author", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read()) authors.Add(reader.GetString(0));
            return authors;
        }

        public virtual BookPriceRange GetBookPriceRange()
        {
            using var connection = Database.GetConnection();
            connection.Open();
            if (!BookValueMigration.HasSchema(connection)) return new BookPriceRange(null, null);

            using var command = new SqlCommand("SELECT MIN(ReplacementValue), MAX(ReplacementValue) FROM dbo.Books WHERE ReplacementValue IS NOT NULL", connection);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1)) return new BookPriceRange(null, null);
            return new BookPriceRange(reader.GetDecimal(0), reader.GetDecimal(1));
        }

        public virtual BookCatalogMetrics GetActiveCatalogMetrics()
        {
            using var connection = Database.GetConnection();
            connection.Open();
            bool hasStatus = BookArchiveMigration.HasSchema(connection);
            string activeWhere = hasStatus ? "WHERE b.Status = @ActiveStatus" : string.Empty;
            string sql;
            bool hasCopies = new BookCopyRepository().HasSchema(connection, null);
            if (hasCopies)
            {
                sql = $@"SELECT COALESCE(SUM(COALESCE(c.ActiveCopies, 0)), 0),
                        COALESCE(SUM(COALESCE(c.AvailableCopies, 0)), 0),
                        COALESCE(SUM(COALESCE(c.BorrowedCopies, 0)), 0)
                    FROM dbo.Books b
                    LEFT JOIN (
                        SELECT BookId,
                            SUM(CASE WHEN Status <> @RetiredStatus THEN 1 ELSE 0 END) AS ActiveCopies,
                            SUM(CASE WHEN Status = @AvailableStatus THEN 1 ELSE 0 END) AS AvailableCopies,
                            SUM(CASE WHEN Status = @BorrowedStatus THEN 1 ELSE 0 END) AS BorrowedCopies
                        FROM dbo.BookCopies GROUP BY BookId
                    ) c ON c.BookId = b.BookId {activeWhere}";
            }
            else
            {
                string activeLoanFilter = hasStatus ? " AND x.Status = @ActiveStatus" : string.Empty;
                sql = $@"SELECT COALESCE(SUM(b.Quantity), 0), COALESCE(SUM(b.AvailableQuantity), 0),
                        (SELECT COUNT(*) FROM dbo.BorrowRecords br
                         INNER JOIN dbo.Books x ON x.BookId = br.BookId
                         WHERE br.Status = 'Borrowing'{activeLoanFilter})
                    FROM dbo.Books b {activeWhere}";
            }

            using var command = new SqlCommand(sql, connection);
            if (hasStatus) command.Parameters.AddWithValue("@ActiveStatus", BookStatuses.Active);
            if (hasCopies)
            {
                command.Parameters.AddWithValue("@RetiredStatus", BookCopyStatuses.Retired);
                command.Parameters.AddWithValue("@AvailableStatus", BookCopyStatuses.Available);
                command.Parameters.AddWithValue("@BorrowedStatus", BookCopyStatuses.Borrowed);
            }
            using var reader = command.ExecuteReader();
            reader.Read();
            return new BookCatalogMetrics(Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)),
                Convert.ToInt32(reader.GetValue(2)));
        }

        private static string BuildPagedWhere(BookSearchQuery query, bool hasStatus,
            bool hasIsbn, bool hasPublisher, bool hasLanguage, bool hasReplacementValue)
        {
            var clauses = new List<string>();
            if (query.Status != BookStatusFilter.All)
            {
                if (hasStatus) clauses.Add("b.Status = @CatalogStatus");
                else if (query.Status == BookStatusFilter.Archived) clauses.Add("1 = 0");
            }

            string? search = query.SearchText?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                var searchColumns = new List<string> { "b.Title LIKE @Keyword", "b.Author LIKE @Keyword" };
                if (hasIsbn && NormalizeIsbnSearch(search).Length > 0)
                    searchColumns.Add("REPLACE(REPLACE(b.ISBN, '-', ''), ' ', '') LIKE @IsbnKeyword");
                if (hasPublisher) searchColumns.Add("b.Publisher LIKE @Keyword");
                clauses.Add("(" + string.Join(" OR ", searchColumns) + ")");
            }

            string? category = query.Category?.Trim();
            if (!string.IsNullOrEmpty(category) && category != BookFilterCodes.All)
                clauses.Add(category == BookFilterCodes.Uncategorized
                    ? "(b.Category IS NULL OR LTRIM(RTRIM(b.Category)) = '')"
                    : "b.Category = @Category");

            string? language = query.LanguageCode?.Trim();
            if (!string.IsNullOrEmpty(language) && language != LanguageCatalog.AllFilterCode)
            {
                string languageColumn = hasLanguage ? "b.Language" : "CAST(NULL AS NVARCHAR(50))";
                clauses.Add(language == LanguageCatalog.UnknownFilterCode
                    ? $"({languageColumn} IS NULL OR LTRIM(RTRIM({languageColumn})) = '')"
                    : $"{languageColumn} = @Language");
            }

            if (!string.IsNullOrWhiteSpace(query.Publisher))
            {
                clauses.Add(hasPublisher ? "LTRIM(RTRIM(b.Publisher)) = @Publisher" : "1 = 0");
            }

            if (query.PublishYearFrom is not null) clauses.Add("b.PublishYear >= @PublishYearFrom");
            if (query.PublishYearTo is not null) clauses.Add("b.PublishYear <= @PublishYearTo");
            if (!string.IsNullOrWhiteSpace(query.Author)) clauses.Add("LTRIM(RTRIM(b.Author)) = @Author");
            if (query.MinBookPrice is not null || query.MaxBookPrice is not null)
            {
                if (!hasReplacementValue)
                    clauses.Add("1 = 0");
                else
                {
                    if (query.MinBookPrice is not null) clauses.Add("b.ReplacementValue >= @MinBookPrice");
                    if (query.MaxBookPrice is not null) clauses.Add("b.ReplacementValue <= @MaxBookPrice");
                }
            }

            clauses.RemoveAll(string.IsNullOrEmpty);
            return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        }

        private static void AddPagedFilters(SqlCommand command, BookSearchQuery query, bool hasStatus, bool hasIsbn)
        {
            if (hasStatus && query.Status != BookStatusFilter.All)
                command.Parameters.AddWithValue("@CatalogStatus", query.Status == BookStatusFilter.Archived
                    ? BookStatuses.Archived
                    : BookStatuses.Active);
            string? search = query.SearchText?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                command.Parameters.AddWithValue("@Keyword", $"%{search}%");
                string normalizedIsbn = NormalizeIsbnSearch(search);
                if (hasIsbn && normalizedIsbn.Length > 0)
                    command.Parameters.AddWithValue("@IsbnKeyword", $"%{normalizedIsbn}%");
            }
            string? category = query.Category?.Trim();
            if (!string.IsNullOrEmpty(category) && category != BookFilterCodes.All && category != BookFilterCodes.Uncategorized)
                command.Parameters.AddWithValue("@Category", category);
            string? language = query.LanguageCode?.Trim();
            if (!string.IsNullOrEmpty(language) && language != LanguageCatalog.AllFilterCode && language != LanguageCatalog.UnknownFilterCode)
                command.Parameters.AddWithValue("@Language", language);
            if (!string.IsNullOrWhiteSpace(query.Publisher))
                command.Parameters.AddWithValue("@Publisher", query.Publisher.Trim());
            if (!string.IsNullOrWhiteSpace(query.Author))
                command.Parameters.AddWithValue("@Author", query.Author.Trim());
            if (query.PublishYearFrom is int yearFrom)
                command.Parameters.AddWithValue("@PublishYearFrom", yearFrom);
            if (query.PublishYearTo is int yearTo)
                command.Parameters.AddWithValue("@PublishYearTo", yearTo);
            if (query.MinBookPrice is decimal minBookPrice)
                command.Parameters.AddWithValue("@MinBookPrice", minBookPrice);
            if (query.MaxBookPrice is decimal maxBookPrice)
                command.Parameters.AddWithValue("@MaxBookPrice", maxBookPrice);
        }

        private static string BuildPagedOrderBy(BookSearchQuery query, bool hasCreatedAt)
        {
            string direction = query.SortDirection?.ToUpperInvariant() switch
            {
                "ASC" => "ASC",
                "DESC" => "DESC",
                _ => throw new ArgumentException("Sort direction is not supported.", nameof(query))
            };
            return query.SortBy switch
            {
                "Title" => $"b.Title {direction}, b.BookId ASC",
                "PublishYear" => $"b.PublishYear {direction}, b.BookId ASC",
                "CreatedAt" when direction == "DESC" && hasCreatedAt => "CASE WHEN b.CreatedAt IS NULL THEN 1 ELSE 0 END ASC, b.CreatedAt DESC, b.BookId DESC",
                "CreatedAt" when direction == "DESC" => "b.BookId DESC",
                _ => throw new ArgumentException("Sort field is not supported.", nameof(query))
            };
        }

        private static string NormalizeIsbnSearch(string search)
            => new(search.Where(character => character != '-' && !char.IsWhiteSpace(character)).ToArray());

        private List<Book> SearchByStatus(string keyword, bool archived)
        {
            var books = new List<Book>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                bool hasIsbn = BookIsbnMigration.HasSchema(conn);
                bool hasPublisher = BookMetadataMigration.HasPublisher(conn);
                bool hasStatus = BookArchiveMigration.HasSchema(conn);
                if (archived && !hasStatus) return books;
                string sql = BookSelect(conn, null) + " FROM Books b WHERE (b.Title LIKE @Keyword OR b.Author LIKE @Keyword"
                    + (hasIsbn ? " OR b.ISBN LIKE @IsbnKeyword" : string.Empty)
                    + (hasPublisher ? " OR b.Publisher LIKE @Keyword" : string.Empty) + ")"
                    + (hasStatus ? " AND b.Status = @Status" : string.Empty);
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
                    if (hasStatus) cmd.Parameters.AddWithValue("@Status", archived ? BookStatuses.Archived : BookStatuses.Active);
                    if (hasIsbn) cmd.Parameters.AddWithValue("@IsbnKeyword", $"%{new string(keyword.Where(character => character != '-' && !char.IsWhiteSpace(character)).ToArray())}%");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            books.Add(BookDataMapper.Map(reader));
                        }
                    }
                }
            }
            return books;
        }

        private static string BookSelect(SqlConnection connection, SqlTransaction? transaction)
        {
            string isbn = BookIsbnMigration.HasSchema(connection, transaction) ? "b.ISBN" : "CAST(NULL AS NVARCHAR(13)) AS ISBN";
            string metadata = $"SELECT b.BookId, b.Title, b.Author, b.Category, b.PublishYear, {isbn}";
            metadata += BookMetadataMigration.HasPublisher(connection, transaction)
                ? ", b.Publisher" : ", CAST(NULL AS NVARCHAR(150)) AS Publisher";
            metadata += BookMetadataMigration.HasLanguage(connection, transaction)
                ? ", b.Language" : ", CAST(NULL AS NVARCHAR(50)) AS Language";
            metadata += BookValueMigration.HasSchema(connection, transaction)
                ? ", b.ReplacementValue" : ", CAST(NULL AS DECIMAL(18,2)) AS ReplacementValue";
            metadata += BookValueMigration.HasRentalPriceSchema(connection, transaction)
                ? ", b.RentalPrice" : ", CAST(NULL AS DECIMAL(18,2)) AS RentalPrice";
            metadata += BookArchiveMigration.HasSchema(connection, transaction)
                ? ", b.Status, b.ArchivedAt"
                : ", CAST('Active' AS NVARCHAR(20)) AS Status, CAST(NULL AS DATETIME2) AS ArchivedAt";
            metadata += BookAuditMigration.HasCreatedAt(connection, transaction)
                ? ", b.CreatedAt" : ", CAST(NULL AS DATETIME2) AS CreatedAt";
            metadata += BookAuditMigration.HasUpdatedAt(connection, transaction)
                ? ", b.UpdatedAt" : ", CAST(NULL AS DATETIME2) AS UpdatedAt";
            if (!new BookCopyRepository().HasSchema(connection, transaction))
                return metadata + ", b.Quantity, b.AvailableQuantity";
            return metadata + @",
                (SELECT COUNT(*) FROM dbo.BookCopies c WHERE c.BookId = b.BookId AND c.Status <> 'Retired') AS Quantity,
                (SELECT COUNT(*) FROM dbo.BookCopies c WHERE c.BookId = b.BookId AND c.Status = 'Available') AS AvailableQuantity,
                (SELECT COUNT(*) FROM dbo.BookCopies c WHERE c.BookId = b.BookId AND c.Status = 'Borrowed') AS BorrowedCopies";
        }

        private static void AddReplacementValue(SqlCommand command, decimal? value)
        {
            var parameter = command.Parameters.Add("@ReplacementValue", System.Data.SqlDbType.Decimal);
            parameter.Precision = 18;
            parameter.Scale = 2;
            parameter.Value = (object?)value ?? DBNull.Value;
        }

        private static void AddRentalPrice(SqlCommand command, decimal? value)
        {
            var parameter = command.Parameters.Add("@RentalPrice", System.Data.SqlDbType.Decimal);
            parameter.Precision = 18;
            parameter.Scale = 2;
            parameter.Value = (object?)value ?? DBNull.Value;
        }
    }
}
