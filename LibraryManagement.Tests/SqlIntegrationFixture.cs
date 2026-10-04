using System;
using System.IO;
using System.Text.RegularExpressions;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LibraryManagement.Tests;

// Integration tests are explicit: they never fall back to the application's LibraryDB.
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIBRARY_TEST_SQL_MASTER_CONNECTION")))
            Skip = "Set LIBRARY_TEST_SQL_MASTER_CONNECTION to a local SQL Server master database.";
    }
}

public sealed class SqlIntegrationFixture : IDisposable
{
    private readonly string _masterConnection;
    private readonly string? _previousOverride;
    private readonly string _databaseName = "LibraryMgmtTest_" + Guid.NewGuid().ToString("N");
    private bool _created;

    public SqlIntegrationFixture()
    {
        _masterConnection = Environment.GetEnvironmentVariable("LIBRARY_TEST_SQL_MASTER_CONNECTION")
            ?? throw new InvalidOperationException("Integration SQL connection was not configured.");

        var builder = new SqlConnectionStringBuilder(_masterConnection);
        string server = builder.DataSource;
        if (server.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase) || server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            server = server[4..];
        string host = server.Split('\\', ',')[0];
        bool isLocal = server.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
            || host == "." || host == "127.0.0.1" || host == "[::1]"
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase) || !isLocal)
            throw new InvalidOperationException("Integration tests require a local SQL Server master connection.");

        _previousOverride = Environment.GetEnvironmentVariable("LIBRARY_TEST_DB_CONNECTION_STRING");
        try
        {
            using var connection = new SqlConnection(_masterConnection);
            connection.Open();
            string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LibraryDB.sql"))
                .Replace("LibraryDB", _databaseName, StringComparison.Ordinal);
            foreach (string batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                command.ExecuteNonQuery();
                _created = true;
            }

            builder.InitialCatalog = _databaseName;
            Environment.SetEnvironmentVariable("LIBRARY_TEST_DB_CONNECTION_STRING", builder.ConnectionString);
            AuthService.CurrentUser = new UserRepository().GetById(1);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LIBRARY_TEST_DB_CONNECTION_STRING", _previousOverride);
        AuthService.CurrentUser = null;
        if (!_created) return;

        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(_masterConnection);
        connection.Open();
        using var command = new SqlCommand(
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END",
            connection) { CommandTimeout = 60 };
        command.ExecuteNonQuery();
    }
}
