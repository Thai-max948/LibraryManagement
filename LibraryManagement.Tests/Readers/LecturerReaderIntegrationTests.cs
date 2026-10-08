using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class LecturerReaderIntegrationTests : IClassFixture<SqlIntegrationFixture>
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void MigrationIsIdempotentAndLecturerCodeSupportsSqlSearchAndPagedFiltering()
    {
        using (var connection = Database.GetConnection())
        {
            connection.Open();
            using (var dropIndex = new SqlCommand(
                "DROP INDEX UX_Readers_LecturerCode_Active ON dbo.Readers", connection))
                dropIndex.ExecuteNonQuery();
            using (var dropDepartment = new SqlCommand(
                "ALTER TABLE dbo.Readers DROP COLUMN Department", connection))
                dropDepartment.ExecuteNonQuery();
            using (var dropLecturerCode = new SqlCommand(
                "ALTER TABLE dbo.Readers DROP COLUMN LecturerCode", connection))
                dropLecturerCode.ExecuteNonQuery();
        }

        LecturerReaderMigration.Apply();
        LecturerReaderMigration.Apply();

        var service = new ReaderService();
        string code = "GV-" + Guid.NewGuid().ToString("N");
        int lecturerId = service.AddReader(new Reader
        {
            FullName = "Lecturer integration reader",
            ReaderType = "Lecturer",
            LecturerCode = code,
            Department = null,
            Phone = "0901234567"
        });

        Reader saved = new ReaderRepository().GetById(lecturerId)!;
        Assert.Equal("Lecturer", saved.ReaderType);
        Assert.Equal(code, saved.LecturerCode);
        Assert.Null(saved.Department);
        Assert.Equal("Student", new ReaderRepository().GetById(1)!.ReaderType);
        Assert.Equal("External", new ReaderRepository().GetById(2)!.ReaderType);

        var filteredPage = new ReaderRepository().GetPage(
            new ReaderPageQuery(code, "Lecturer", "All", "Name A-Z", pageNumber: 1, pageSize: 5));
        Assert.Equal(1, filteredPage.TotalCount);
        Assert.Equal(lecturerId, Assert.Single(filteredPage.Items).ReaderId);

        var borrowSuggestions = new ReaderRepository().SearchForBorrow(code, pageNumber: 1, pageSize: 5);
        Assert.False(borrowSuggestions.HasMore);
        Assert.Equal(lecturerId, Assert.Single(borrowSuggestions.Items).ReaderId);

        using var duplicateConnection = Database.GetConnection();
        duplicateConnection.Open();
        using var duplicateInsert = new SqlCommand(@"
            INSERT INTO dbo.Readers (FullName, ReaderType, LecturerCode, Phone, Status, IsDeleted)
            VALUES (N'Duplicate lecturer', N'Lecturer', @LecturerCode, N'0901234568', N'Active', 0);",
            duplicateConnection);
        duplicateInsert.Parameters.AddWithValue("@LecturerCode", code.ToLowerInvariant());
        Assert.Throws<SqlException>(() => duplicateInsert.ExecuteNonQuery());
    }
}
