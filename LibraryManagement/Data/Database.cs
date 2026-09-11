using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Text; 
namespace LibraryManagement.Data
    {
    public class Database
        {
            private const string ConnectionString =
                "Server=DESKTOP-LTBFBRM\\SQLEXPRESS;Database=LibraryDB;Trusted_Connection=True;TrustServerCertificate=True;";

            public SqlConnection GetConnection()
            {
                return new SqlConnection(ConnectionString);
            }
        }
    }


