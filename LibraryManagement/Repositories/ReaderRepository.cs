using System;
using System.Collections.Generic;
using System.Data;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Repositories
{
    public class ReaderRepository
    {
        // Borrow transactions acquire this lock before touching a copy or loan.
        public virtual bool LockForBorrow(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(
                "SELECT ReaderId FROM dbo.Readers WITH (UPDLOCK, HOLDLOCK) WHERE ReaderId = @ReaderId",
                connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            return command.ExecuteScalar() != null;
        }

        public virtual bool ExistsForCirculation(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(
                "SELECT ReaderId FROM dbo.Readers WITH (HOLDLOCK, ROWLOCK) WHERE ReaderId = @ReaderId",
                connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            return command.ExecuteScalar() != null;
        }

        private static readonly object SchemaLock = new object();
        private static bool _schemaChecked;

        public virtual List<Reader> GetAll(bool includeDeleted = false)
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = includeDeleted
                    ? "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers"
                    : "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE IsDeleted = 0";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        readers.Add(MapToReader(reader));
                    }
                }
            }
            return readers;
        }

        public virtual Reader? GetById(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE ReaderId = @ReaderId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapToReader(reader);
                        }
                    }
                }
            }
            return null;
        }

        public virtual Reader? GetById(SqlConnection connection, SqlTransaction transaction, int readerId)
        {
            using var command = new SqlCommand(@"
                SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email,
                    Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason,
                    SuspendedDate, IsDeleted
                FROM dbo.Readers WHERE ReaderId = @ReaderId", connection, transaction);
            command.Parameters.AddWithValue("@ReaderId", readerId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapToReader(reader) : null;
        }

        public virtual int Add(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = @"INSERT INTO Readers (FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted)
                               OUTPUT INSERTED.ReaderId
                               VALUES (@FullName, @ReaderType, @StudentId, @IdentityNumber, @LecturerCode, @Department, @Phone, @Email, @Address, @RegistrationDate, @MembershipExpiresOn, @Status, @SuspensionReason, @SuspendedDate, 0)";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@ReaderType", string.IsNullOrWhiteSpace(reader.ReaderType) ? "Student" : reader.ReaderType);
                    cmd.Parameters.AddWithValue("@StudentId", (object?)reader.StudentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdentityNumber", (object?)reader.IdentityNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@LecturerCode", (object?)reader.LecturerCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Department", (object?)reader.Department ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object?)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object?)reader.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object?)reader.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RegistrationDate", reader.RegistrationDate == default ? DateTime.Now : reader.RegistrationDate);
                    cmd.Parameters.AddWithValue("@MembershipExpiresOn", (object?)reader.MembershipExpiresOn?.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
                    cmd.Parameters.AddWithValue("@SuspensionReason", (object?)reader.SuspensionReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SuspendedDate", (object?)reader.SuspendedDate ?? DBNull.Value);
                    int newId = (int)cmd.ExecuteScalar();
                    reader.ReaderId = newId;
                    return newId;
                }
            }
        }

        public virtual bool Update(Reader reader)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string sql = @"UPDATE Readers SET
                                   FullName = @FullName,
                                   ReaderType = @ReaderType,
                                   StudentId = @StudentId,
                                   IdentityNumber = @IdentityNumber,
                                   LecturerCode = @LecturerCode,
                                   Department = @Department,
                                   Phone = @Phone,
                                   Email = @Email,
                                   Address = @Address,
                                   MembershipExpiresOn = @MembershipExpiresOn,
                                   Status = @Status,
                                   SuspensionReason = @SuspensionReason,
                                   SuspendedDate = @SuspendedDate
                               WHERE ReaderId = @ReaderId AND IsDeleted = 0";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ReaderId", reader.ReaderId);
                    cmd.Parameters.AddWithValue("@FullName", reader.FullName);
                    cmd.Parameters.AddWithValue("@ReaderType", string.IsNullOrWhiteSpace(reader.ReaderType) ? "Student" : reader.ReaderType);
                    cmd.Parameters.AddWithValue("@StudentId", (object?)reader.StudentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdentityNumber", (object?)reader.IdentityNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@LecturerCode", (object?)reader.LecturerCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Department", (object?)reader.Department ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object?)reader.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object?)reader.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Address", (object?)reader.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@MembershipExpiresOn", (object?)reader.MembershipExpiresOn?.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(reader.Status) ? "Active" : reader.Status);
                    cmd.Parameters.AddWithValue("@SuspensionReason", (object?)reader.SuspensionReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SuspendedDate", (object?)reader.SuspendedDate ?? DBNull.Value);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public virtual bool Delete(int id)
        {
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                string checkSql = "SELECT IsDeleted FROM Readers WHERE ReaderId = @ReaderId";
                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@ReaderId", id);
                    var isDelObj = checkCmd.ExecuteScalar();
                    if (isDelObj == null)
                    {
                        return false;
                    }
                    if (Convert.ToBoolean(isDelObj))
                    {
                        return false;
                    }
                }

                string checkHistorySql = "SELECT COUNT(*) FROM BorrowRecords WHERE ReaderId = @ReaderId";
                using (var historyCmd = new SqlCommand(checkHistorySql, conn))
                {
                    historyCmd.Parameters.AddWithValue("@ReaderId", id);
                    int borrowCount = (int)historyCmd.ExecuteScalar();

                    if (borrowCount > 0)
                    {
                        // Đã có lịch sử mượn trả: Xóa mềm để bảo toàn lịch sử và ràng buộc khóa ngoại
                        string softDeleteSql = "UPDATE Readers SET IsDeleted = 1 WHERE ReaderId = @ReaderId";
                        using (var cmd = new SqlCommand(softDeleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ReaderId", id);
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }
                    else
                    {
                        // Chưa từng mượn sách: Xóa cứng khỏi CSDL
                        string hardDeleteSql = "DELETE FROM Readers WHERE ReaderId = @ReaderId";
                        using (var cmd = new SqlCommand(hardDeleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ReaderId", id);
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }
                }
            }
        }

        public virtual List<Reader> Search(string keyword)
        {
            return Search(keyword, null, null);
        }

        public virtual List<Reader> Search(string keyword, string? readerType, string? status)
        {
            var readers = new List<Reader>();
            using (var conn = Database.GetConnection())
            {
                conn.Open();
                EnsureEnhancementColumns(conn);
                var conditions = new List<string> { "IsDeleted = 0" };

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    conditions.Add(@"(
                        FullName LIKE @Keyword 
                        OR Phone LIKE @Keyword 
                        OR Email LIKE @Keyword 
                        OR StudentId LIKE @Keyword 
                        OR IdentityNumber LIKE @Keyword 
                        OR LecturerCode LIKE @Keyword
                        OR CAST(ReaderId AS NVARCHAR(20)) LIKE @Keyword
                        OR ('R' + RIGHT('000000' + CAST(ReaderId AS NVARCHAR(20)), 6)) LIKE @Keyword
                    )");
                }

                if (!string.IsNullOrWhiteSpace(readerType) && !string.Equals(readerType, "All", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("ReaderType = @ReaderType");
                }

                if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("Status = @Status");
                }

                string sql = "SELECT ReaderId, FullName, ReaderType, StudentId, IdentityNumber, LecturerCode, Department, Phone, Email, Address, RegistrationDate, MembershipExpiresOn, Status, SuspensionReason, SuspendedDate, IsDeleted FROM Readers WHERE "
                    + string.Join(" AND ", conditions);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        cmd.Parameters.AddWithValue("@Keyword", $"%{keyword.Trim()}%");
                    }
                    if (!string.IsNullOrWhiteSpace(readerType) && !string.Equals(readerType, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.Parameters.AddWithValue("@ReaderType", readerType.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.Parameters.AddWithValue("@Status", status.Trim());
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            readers.Add(MapToReader(reader));
                        }
                    }
                }
            }
            return readers;
        }

        public virtual ReaderPage GetPage(ReaderPageQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);

            var conditions = new List<string> { "r.IsDeleted = 0" };
            if (query.Keyword != null)
            {
                conditions.Add(@"(
                    r.FullName LIKE @Keyword ESCAPE N'\'
                    OR r.Phone LIKE @Keyword ESCAPE N'\'
                    OR r.Email LIKE @Keyword ESCAPE N'\'
                    OR r.StudentId LIKE @Keyword ESCAPE N'\'
                    OR r.IdentityNumber LIKE @Keyword ESCAPE N'\'
                    OR r.LecturerCode LIKE @Keyword ESCAPE N'\'
                    OR r.Department LIKE @Keyword ESCAPE N'\'
                    OR CAST(r.ReaderId AS NVARCHAR(20)) LIKE @Keyword ESCAPE N'\'
                    OR (N'R' + RIGHT(N'000000' + CAST(r.ReaderId AS NVARCHAR(20)), 6)) LIKE @Keyword ESCAPE N'\'
                )");
            }
            if (query.ReaderType != null)
                conditions.Add("r.ReaderType = @ReaderType");
            if (query.Status != null)
                conditions.Add("r.Status = @Status");

            string whereClause = string.Join(" AND ", conditions);
            var items = new List<Reader>(query.PageSize);
            int totalCount;

            using (var connection = Database.GetConnection())
            {
                connection.Open();
                EnsureEnhancementColumns(connection);

                using (var countCommand = new SqlCommand(
                    $"SELECT COUNT_BIG(*) FROM dbo.Readers AS r WHERE {whereClause};", connection))
                {
                    AddPageFilterParameters(countCommand, query);
                    totalCount = checked(Convert.ToInt32(countCommand.ExecuteScalar()));
                }

                int pageNumber = query.GetEffectivePageNumber(totalCount);
                int offset = checked((pageNumber - 1) * query.PageSize);
                string sql = $@"
                    SELECT r.ReaderId, r.FullName, r.ReaderType, r.StudentId, r.IdentityNumber, r.LecturerCode, r.Department,
                           r.Phone, r.Email, r.Address, r.RegistrationDate, r.MembershipExpiresOn,
                           r.Status, r.SuspensionReason, r.SuspendedDate, r.IsDeleted
                    FROM dbo.Readers AS r
                    WHERE {whereClause}
                    ORDER BY {query.OrderBySql}
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                using (var pageCommand = new SqlCommand(sql, connection))
                {
                    AddPageFilterParameters(pageCommand, query);
                    pageCommand.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
                    pageCommand.Parameters.Add("@PageSize", SqlDbType.Int).Value = query.PageSize;

                    using (var reader = pageCommand.ExecuteReader())
                    {
                        while (reader.Read())
                            items.Add(MapToReader(reader));
                    }
                }

                return new ReaderPage
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = query.PageSize
                };
            }
        }

        private static void AddPageFilterParameters(SqlCommand command, ReaderPageQuery query)
        {
            if (query.Keyword != null)
            {
                command.Parameters.Add("@Keyword", SqlDbType.NVarChar, 4000).Value =
                    $"%{EscapeLikePattern(query.Keyword)}%";
            }
            if (query.ReaderType != null)
                command.Parameters.Add("@ReaderType", SqlDbType.NVarChar, 50).Value = query.ReaderType;
            if (query.Status != null)
                command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = query.Status;
        }

        private static string EscapeLikePattern(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);

        public virtual List<Reader> SearchForBorrow(string query, int limit)
        {
            var readers = new List<Reader>();
            if (string.IsNullOrWhiteSpace(query)) return readers;

            using var connection = Database.GetConnection();
            connection.Open();
            EnsureEnhancementColumns(connection);

            int? exactReaderId = int.TryParse(query, out int readerId) ? readerId : null;
            const string formattedReaderId = "N'R' + RIGHT(N'000000' + CONVERT(NVARCHAR(20), r.ReaderId), 6)";
            string exactMatch = $@"(@ExactReaderId IS NOT NULL AND r.ReaderId = @ExactReaderId)
                OR r.StudentId = @Query
                OR r.IdentityNumber = @Query
                OR r.LecturerCode = @Query
                OR r.Phone = @Query
                OR r.Email = @Query
                OR {formattedReaderId} = @Query";
            string searchCondition = query.Length < 2
                ? $"({exactMatch})"
                : @"(r.FullName LIKE @Pattern
                    OR r.Phone LIKE @Pattern
                    OR r.Email LIKE @Pattern
                    OR r.StudentId LIKE @Pattern
                    OR r.IdentityNumber LIKE @Pattern
                    OR r.LecturerCode LIKE @Pattern
                    OR CONVERT(NVARCHAR(20), r.ReaderId) LIKE @Pattern
                    OR " + formattedReaderId + " LIKE @Pattern)";

            string sql = $@"SELECT TOP (@Limit)
                    r.ReaderId, r.FullName, r.ReaderType, r.StudentId, r.IdentityNumber, r.LecturerCode, r.Department,
                    r.Phone, r.Email, r.Address, r.RegistrationDate, r.MembershipExpiresOn,
                    r.Status, r.SuspensionReason, r.SuspendedDate, r.IsDeleted
                FROM dbo.Readers r
                WHERE r.IsDeleted = 0 AND {searchCondition}
                ORDER BY CASE WHEN ({exactMatch}) THEN 0 ELSE 1 END,
                    r.FullName ASC, r.ReaderId ASC";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
            command.Parameters.Add("@Query", SqlDbType.NVarChar, 4000).Value = query;
            command.Parameters.Add("@Pattern", SqlDbType.NVarChar, 4000).Value = $"%{query}%";
            command.Parameters.Add("@ExactReaderId", SqlDbType.Int).Value = exactReaderId.HasValue
                ? exactReaderId.Value
                : DBNull.Value;

            using var reader = command.ExecuteReader();
            while (reader.Read()) readers.Add(MapToReader(reader));
            return readers;
        }

        public virtual BorrowRecommendationPage<Reader> SearchForBorrow(
            string? query, int pageNumber, int pageSize)
        {
            string normalizedQuery = query?.Trim() ?? string.Empty;
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 20);

            if (normalizedQuery.Length == 1 && !int.TryParse(normalizedQuery, out _))
                return new BorrowRecommendationPage<Reader>(Array.Empty<Reader>(), false);

            bool hasQuery = normalizedQuery.Length > 0;
            using var connection = Database.GetConnection();
            connection.Open();
            EnsureEnhancementColumns(connection);

            int? exactReaderId = int.TryParse(normalizedQuery, out int readerId) ? readerId : null;
            const string formattedReaderId = "N'R' + RIGHT(N'000000' + CONVERT(NVARCHAR(20), r.ReaderId), 6)";
            const string exactMatch = "(@ExactReaderId IS NOT NULL AND r.ReaderId = @ExactReaderId) " +
                "OR r.StudentId = @Query OR r.IdentityNumber = @Query OR r.LecturerCode = @Query OR r.Phone = @Query " +
                "OR r.Email = @Query OR " + formattedReaderId + " = @Query";

            string searchCondition = !hasQuery
                ? "r.Status = @ActiveStatus"
                : normalizedQuery.Length < 2
                    ? $"({exactMatch})"
                    : @"(r.FullName LIKE @Pattern
                        OR r.Phone LIKE @Pattern
                        OR r.Email LIKE @Pattern
                        OR r.StudentId LIKE @Pattern
                        OR r.IdentityNumber LIKE @Pattern
                        OR r.LecturerCode LIKE @Pattern
                        OR CONVERT(NVARCHAR(20), r.ReaderId) LIKE @Pattern
                        OR " + formattedReaderId + " LIKE @Pattern)";

            string orderBy = hasQuery
                ? $"CASE WHEN ({exactMatch}) THEN 0 ELSE 1 END, r.FullName ASC, r.ReaderId ASC"
                : "r.ReaderId ASC";
            long offset = ((long)pageNumber - 1) * pageSize;
            string sql = $@"SELECT r.ReaderId, r.FullName, r.ReaderType, r.StudentId, r.IdentityNumber, r.LecturerCode, r.Department,
                    r.Phone, r.Email, r.Address, r.RegistrationDate, r.MembershipExpiresOn,
                    r.Status, r.SuspensionReason, r.SuspendedDate, r.IsDeleted
                FROM dbo.Readers r
                WHERE r.IsDeleted = 0 AND {searchCondition}
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @FetchSize ROWS ONLY";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Offset", SqlDbType.BigInt).Value = offset;
            command.Parameters.Add("@FetchSize", SqlDbType.Int).Value = pageSize + 1;
            if (hasQuery)
            {
                command.Parameters.Add("@Query", SqlDbType.NVarChar, 4000).Value = normalizedQuery;
                command.Parameters.Add("@ExactReaderId", SqlDbType.Int).Value = exactReaderId.HasValue
                    ? exactReaderId.Value
                    : DBNull.Value;
                if (normalizedQuery.Length >= 2)
                    command.Parameters.Add("@Pattern", SqlDbType.NVarChar, 4000).Value = $"%{normalizedQuery}%";
            }
            else
            {
                command.Parameters.Add("@ActiveStatus", SqlDbType.NVarChar, 50).Value = "Active";
            }

            var items = new List<Reader>(pageSize + 1);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) items.Add(MapToReader(reader));
            }

            bool hasMore = items.Count > pageSize;
            if (hasMore) items.RemoveAt(items.Count - 1);
            return new BorrowRecommendationPage<Reader>(items, hasMore);
        }

        public virtual bool IdentificationExists(string readerType, string identification, int excludeReaderId = 0)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            EnsureEnhancementColumns(conn);
            string column = string.Equals(readerType, "External", StringComparison.OrdinalIgnoreCase)
                ? "IdentityNumber"
                : string.Equals(readerType, "Lecturer", StringComparison.OrdinalIgnoreCase) ? "LecturerCode" : "StudentId";
            string sql = $@"SELECT 1 FROM Readers
                            WHERE IsDeleted = 0 AND ReaderType = @ReaderType
                              AND UPPER(LTRIM(RTRIM({column}))) = UPPER(@Identification)
                              AND (@ExcludeReaderId = 0 OR ReaderId <> @ExcludeReaderId)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ReaderType", readerType);
            cmd.Parameters.AddWithValue("@Identification", identification.Trim());
            cmd.Parameters.AddWithValue("@ExcludeReaderId", excludeReaderId);
            return cmd.ExecuteScalar() != null;
        }

        private static Reader MapToReader(SqlDataReader reader)
        {
            int readerIdOrdinal = reader.GetOrdinal("ReaderId");
            int fullNameOrdinal = reader.GetOrdinal("FullName");
            int phoneOrdinal = GetOrdinalOrDefault(reader, "Phone");
            int emailOrdinal = GetOrdinalOrDefault(reader, "Email");
            int typeOrdinal = GetOrdinalOrDefault(reader, "ReaderType");
            int studentIdOrdinal = GetOrdinalOrDefault(reader, "StudentId");
            int idNumOrdinal = GetOrdinalOrDefault(reader, "IdentityNumber");
            int lecturerCodeOrdinal = GetOrdinalOrDefault(reader, "LecturerCode");
            int departmentOrdinal = GetOrdinalOrDefault(reader, "Department");
            int addressOrdinal = GetOrdinalOrDefault(reader, "Address");
            int regDateOrdinal = GetOrdinalOrDefault(reader, "RegistrationDate");
            int membershipOrdinal = GetOrdinalOrDefault(reader, "MembershipExpiresOn");
            int statusOrdinal = GetOrdinalOrDefault(reader, "Status");
            int suspensionReasonOrdinal = GetOrdinalOrDefault(reader, "SuspensionReason");
            int suspendedDateOrdinal = GetOrdinalOrDefault(reader, "SuspendedDate");
            int isDeletedOrdinal = GetOrdinalOrDefault(reader, "IsDeleted");

            return new Reader
            {
                ReaderId = reader.GetInt32(readerIdOrdinal),
                FullName = reader.GetString(fullNameOrdinal),
                ReaderType = typeOrdinal >= 0 && !reader.IsDBNull(typeOrdinal) ? reader.GetString(typeOrdinal) : "Student",
                StudentId = studentIdOrdinal >= 0 && !reader.IsDBNull(studentIdOrdinal) ? reader.GetString(studentIdOrdinal) : string.Empty,
                IdentityNumber = idNumOrdinal >= 0 && !reader.IsDBNull(idNumOrdinal) ? reader.GetString(idNumOrdinal) : string.Empty,
                LecturerCode = lecturerCodeOrdinal >= 0 && !reader.IsDBNull(lecturerCodeOrdinal) ? reader.GetString(lecturerCodeOrdinal) : null,
                Department = departmentOrdinal >= 0 && !reader.IsDBNull(departmentOrdinal) ? reader.GetString(departmentOrdinal) : null,
                Phone = phoneOrdinal >= 0 && !reader.IsDBNull(phoneOrdinal) ? reader.GetString(phoneOrdinal) : string.Empty,
                Email = emailOrdinal >= 0 && !reader.IsDBNull(emailOrdinal) ? reader.GetString(emailOrdinal) : string.Empty,
                Address = addressOrdinal >= 0 && !reader.IsDBNull(addressOrdinal) ? reader.GetString(addressOrdinal) : string.Empty,
                RegistrationDate = regDateOrdinal >= 0 && !reader.IsDBNull(regDateOrdinal) ? reader.GetDateTime(regDateOrdinal) : DateTime.Now,
                MembershipExpiresOn = membershipOrdinal >= 0 && !reader.IsDBNull(membershipOrdinal) ? reader.GetDateTime(membershipOrdinal) : null,
                Status = statusOrdinal >= 0 && !reader.IsDBNull(statusOrdinal) ? reader.GetString(statusOrdinal) : "Active",
                SuspensionReason = suspensionReasonOrdinal >= 0 && !reader.IsDBNull(suspensionReasonOrdinal) ? reader.GetString(suspensionReasonOrdinal) : string.Empty,
                SuspendedDate = suspendedDateOrdinal >= 0 && !reader.IsDBNull(suspendedDateOrdinal) ? reader.GetDateTime(suspendedDateOrdinal) : null,
                IsDeleted = isDeletedOrdinal >= 0 && !reader.IsDBNull(isDeletedOrdinal) && reader.GetBoolean(isDeletedOrdinal)
            };
        }

        private static void EnsureEnhancementColumns(SqlConnection connection)
        {
            if (_schemaChecked)
            {
                return;
            }

            lock (SchemaLock)
            {
                if (_schemaChecked)
                {
                    return;
                }

                const string sql = @"
                    IF COL_LENGTH('dbo.Readers', 'SuspensionReason') IS NULL
                        ALTER TABLE dbo.Readers ADD SuspensionReason NVARCHAR(500) NULL;

                    IF COL_LENGTH('dbo.Readers', 'SuspendedDate') IS NULL
                        ALTER TABLE dbo.Readers ADD SuspendedDate DATETIME NULL;
                    IF COL_LENGTH('dbo.Readers', 'MembershipExpiresOn') IS NULL
                        ALTER TABLE dbo.Readers ADD MembershipExpiresOn DATE NULL;";

                using var command = new SqlCommand(sql, connection);
                command.ExecuteNonQuery();
                _schemaChecked = true;
            }
        }

        private static int GetOrdinalOrDefault(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName);
            }
            catch (IndexOutOfRangeException)
            {
                return -1;
            }
        }
    }
}
