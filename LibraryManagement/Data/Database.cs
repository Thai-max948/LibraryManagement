using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Data
{
    public class Database
    {
        private static string? _connectionString;

        public static string ConnectionString
        {
            get
            {
                if (!string.IsNullOrEmpty(_connectionString))
                {
                    return _connectionString;
                }

                try
                {
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                    if (File.Exists(configPath))
                    {
                        string json = File.ReadAllText(configPath);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings) &&
                            connStrings.TryGetProperty("DefaultConnection", out var defaultConn))
                        {
                            string? val = defaultConn.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                _connectionString = val;
                                return _connectionString;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback on error reading config
                }

                // Default fallback: works on any machine with local SQL Express
                _connectionString = "Server=.\\SQLEXPRESS;Database=LibraryDB;Trusted_Connection=True;TrustServerCertificate=True;";
                return _connectionString;
            }
        }

        public SqlConnection GetConnection()
        {
            return new SqlConnection(ConnectionString);
        }
    }
}
