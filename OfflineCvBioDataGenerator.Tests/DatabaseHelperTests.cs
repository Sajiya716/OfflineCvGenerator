using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class DatabaseHelperTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void UseDatabasePath_PointsTheHelperAtTheGivenFile()
        {
            Assert.Equal(_db.DatabasePath, DatabaseHelper.DatabasePath);
        }

        [Fact]
        public void GetConnection_ReturnsAnOpenConnection()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            Assert.Equal(ConnectionState.Open, conn.State);
        }

        [Fact]
        public void GetConnection_EnforcesForeignKeys()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            int enabled = conn.ExecuteScalar<int>("PRAGMA foreign_keys;");

            Assert.Equal(1, enabled);
        }

        [Fact]
        public void InitializeDatabase_CreatesAllTables()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            List<string> tables = conn
                .Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'")
                .ToList();

            Assert.Contains("Clients", tables);
            Assert.Contains("Education", tables);
            Assert.Contains("Experience", tables);
        }

        [Fact]
        public void InitializeDatabase_CreatesPhotoPathColumn()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            List<string> columns = conn
                .Query<string>("SELECT name FROM pragma_table_info('Clients')")
                .ToList();

            Assert.Contains("PhotoPath", columns);
        }

        [Fact]
        public void InitializeDatabase_UpgradesALegacyDatabase_AndKeepsExistingRows()
        {
            // A pre-photo-feature database: no PhotoPath column and, crucially, no
            // schema version yet (older builds never wrote one).
            using (IDbConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Execute("DROP TABLE Clients");
                conn.Execute(@"
                    CREATE TABLE Clients (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FullName TEXT, Address TEXT, Phone TEXT,
                        Email TEXT, DOB DATE, NIC TEXT)");
                conn.Execute(
                    "INSERT INTO Clients (FullName, NIC) VALUES ('Legacy Person', '123456789V')");
                conn.Execute("PRAGMA user_version = 0;");
            }

            // Running the upgrade must add the column without losing the row.
            DatabaseHelper.InitializeDatabase();

            using (IDbConnection conn = DatabaseHelper.GetConnection())
            {
                List<string> columns = conn
                    .Query<string>("SELECT name FROM pragma_table_info('Clients')")
                    .ToList();

                Assert.Contains("PhotoPath", columns);
                Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Clients WHERE NIC = '123456789V'"));
            }
        }

        [Fact]
        public void InitializeDatabase_SetsTheSchemaVersion()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            Assert.Equal(DatabaseHelper.CurrentSchemaVersion, DatabaseHelper.GetSchemaVersion(conn));
        }

        [Fact]
        public void InitializeDatabase_IsIdempotent()
        {
            DatabaseHelper.InitializeDatabase();
            DatabaseHelper.InitializeDatabase();

            using IDbConnection conn = DatabaseHelper.GetConnection();

            Assert.Equal(DatabaseHelper.CurrentSchemaVersion, DatabaseHelper.GetSchemaVersion(conn));
            Assert.Contains("PhotoPath",
                conn.Query<string>("SELECT name FROM pragma_table_info('Clients')").ToList());
        }

        [Fact]
        public void UseDefaultDatabasePath_PointsIntoTheApplicationDataFolder()
        {
            DatabaseHelper.UseDefaultDatabasePath();

            Assert.Equal(AppPaths.DatabaseFile, DatabaseHelper.DatabasePath);
            Assert.Equal("clients.db", Path.GetFileName(DatabaseHelper.DatabasePath));
            Assert.StartsWith(AppPaths.Root, DatabaseHelper.DatabasePath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
