using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Services;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    /// <summary>
    /// Covers the "zero data loss across updates" guarantees:
    ///  * the database and the photos migrate out of the install folder into %AppData%;
    ///  * schema upgrades are versioned, backed up and never destructive;
    ///  * a database written by a newer build is refused rather than downgraded.
    /// </summary>
    public class DatabaseMigrationTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose()
        {
            DatabaseHelper.UseDefaultLegacySearchFolders();
            PhotoStore.UseDefaultLegacySearchFolders();
            _db.Dispose();
        }

        // ---------------------------------------------------------------- app paths

        [Fact]
        public void AppPaths_Root_IsUnderApplicationData()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            Assert.StartsWith(appData, AppPaths.DefaultRoot, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(AppPaths.FolderName, AppPaths.DefaultRoot.TrimEnd(Path.DirectorySeparatorChar));
        }

        [Fact]
        public void AppPaths_ExposeTheDataFilesUsedByTheApp()
        {
            Assert.Equal("clients.db", Path.GetFileName(AppPaths.DatabaseFile));
            Assert.Equal("ProfilePics", Path.GetFileName(AppPaths.PhotoFolder));
            Assert.Equal("error.log", Path.GetFileName(AppPaths.LogFile));
        }

        [Fact]
        public void AppPaths_UseRoot_RedirectsEveryLocation()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvroot_" + Guid.NewGuid().ToString("N"));

            try
            {
                AppPaths.UseRoot(root);

                Assert.Equal(Path.Combine(root, "clients.db"), AppPaths.DatabaseFile);
                Assert.Equal(Path.Combine(root, "ProfilePics"), AppPaths.PhotoFolder);
                Assert.Equal(Path.Combine(root, "error.log"), AppPaths.LogFile);
            }
            finally
            {
                AppPaths.UseRoot(_db.RootPath);
            }
        }

        // ---------------------------------------------------------------- versioning

        [Fact]
        public void InitializeDatabase_RefusesADatabaseFromANewerBuild()
        {
            using (IDbConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Execute($"PRAGMA user_version = {DatabaseHelper.CurrentSchemaVersion + 5};");
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => DatabaseHelper.InitializeDatabase());

            Assert.Contains("Install the latest version of the application", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The file must be left exactly as it was.
            using IDbConnection check = DatabaseHelper.GetConnection();
            Assert.Equal(DatabaseHelper.CurrentSchemaVersion + 5, DatabaseHelper.GetSchemaVersion(check));
        }

        [Fact]
        public void InitializeDatabase_BacksTheDatabaseUpBeforeUpgrading()
        {
            SeedLegacyDatabase(version: 0);

            DatabaseHelper.InitializeDatabase();

            List<string> backups = Directory.GetFiles(_db.RootPath, "clients.backup-v*.db").ToList();

            Assert.Single(backups);

            // The backup is a usable copy of the pre-upgrade file, with the data intact.
            using var conn = new SQLiteConnection($"Data Source={backups[0]};Version=3;Pooling=False;");
            conn.Open();
            Assert.Equal(0, DatabaseHelper.GetSchemaVersion(conn));
            Assert.Equal(1, conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM Clients WHERE NIC = '123456789V'"));
        }

        [Fact]
        public void InitializeDatabase_DoesNotBackUpAnEmptyOrMissingDatabase()
        {
            // TempDatabase already created and initialised the file, so there is nothing to
            // upgrade: running again must not produce backup copies.
            DatabaseHelper.InitializeDatabase();

            Assert.Empty(Directory.GetFiles(_db.RootPath, "clients.backup-v*.db"));
        }

        [Fact]
        public void InitializeDatabase_KeepsOnlyTheNewestFiveBackups()
        {
            for (int i = 0; i < 8; i++)
            {
                SeedLegacyDatabase(version: 0);
                DatabaseHelper.InitializeDatabase();
            }

            Assert.Equal(5, Directory.GetFiles(_db.RootPath, "clients.backup-v*.db").Length);
        }

        /// <summary>
        /// The upgrade relies on SQLite applying a migration's DDL *and* its user_version bump
        /// in one transaction. If that ever stopped being true a failed upgrade could leave a
        /// half-migrated file behind, so pin the behaviour down here.
        /// </summary>
        [Fact]
        public void Sqlite_RollsBackSchemaChangesAndTheUserVersion_WhenATransactionFails()
        {
            using IDbConnection conn = DatabaseHelper.GetConnection();

            conn.Execute("PRAGMA foreign_keys=OFF;");

            using (IDbTransaction tx = conn.BeginTransaction())
            {
                conn.Execute("CREATE TABLE RollbackProbe (Id INTEGER);", transaction: tx);
                conn.Execute("PRAGMA user_version = 99;", transaction: tx);
                tx.Rollback();
            }

            Assert.Equal(DatabaseHelper.CurrentSchemaVersion, DatabaseHelper.GetSchemaVersion(conn));
            Assert.DoesNotContain("RollbackProbe",
                conn.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToList());
        }

        // ---------------------------------------------------------------- file relocation

        [Fact]
        public void InitializeDatabase_CopiesADatabaseLeftInTheInstallFolder()
        {
            string legacyFolder = NewTempFolder("cvlegacy_");
            string tempRoot = NewTempFolder("cvnewroot_");

            SeedLegacyDatabaseIn(Path.Combine(legacyFolder, "cvdb.sqlite"), version: 0);
            DatabaseHelper.UseLegacySearchFolders(legacyFolder);

            try
            {
                AppPaths.UseRoot(tempRoot);
                DatabaseHelper.UseDefaultDatabasePath(); // destination = tempRoot\clients.db, migration on

                DatabaseHelper.InitializeDatabase();

                using IDbConnection conn = DatabaseHelper.GetConnection();

                Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Clients WHERE NIC = '123456789V'"));
                Assert.Equal(DatabaseHelper.CurrentSchemaVersion, DatabaseHelper.GetSchemaVersion(conn));
            }
            finally
            {
                AppPaths.UseRoot(_db.RootPath);
                DatabaseHelper.UseDefaultLegacySearchFolders();

                TryDeleteFolder(legacyFolder);
                TryDeleteFolder(tempRoot);
            }
        }

        [Fact]
        public void InitializeDatabase_DoesNotOverwriteAnExistingAppDataDatabase()
        {
            string legacyFolder = NewTempFolder("cvlegacy_");
            string tempRoot = NewTempFolder("cvnewroot_");

            try
            {
                AppPaths.UseRoot(tempRoot);
                DatabaseHelper.UseDefaultDatabasePath();

                // The real database already exists in %AppData% and holds the user's work.
                DatabaseHelper.InitializeDatabase();
                using (IDbConnection conn = DatabaseHelper.GetConnection())
                {
                    conn.Execute("INSERT INTO Clients (FullName, NIC) VALUES ('Current', 'CURRENT')");
                }

                // A stale database now appears in the install folder.
                SeedLegacyDatabaseIn(Path.Combine(legacyFolder, "cvdb.sqlite"), version: 0, nic: "OLD");
                DatabaseHelper.UseLegacySearchFolders(legacyFolder);

                DatabaseHelper.InitializeDatabase();

                using IDbConnection check = DatabaseHelper.GetConnection();
                Assert.Equal(1, check.ExecuteScalar<int>("SELECT COUNT(1) FROM Clients WHERE NIC = 'CURRENT'"));
                Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(1) FROM Clients WHERE NIC = 'OLD'"));
            }
            finally
            {
                AppPaths.UseRoot(_db.RootPath);
                DatabaseHelper.UseDefaultLegacySearchFolders();

                TryDeleteFolder(legacyFolder);
                TryDeleteFolder(tempRoot);
            }
        }

        [Fact]
        public void MigrateLegacyFolder_CopiesPhotosOutOfTheInstallFolder()
        {
            string legacyFolder = NewTempFolder("cvlegacypics_");

            File.WriteAllBytes(Path.Combine(legacyFolder, "old-photo.png"), TestData.OnePixelPng());
            PhotoStore.UseLegacySearchFolders(legacyFolder);

            try
            {
                PhotoStore.MigrateLegacyFolder();

                string migrated = Path.Combine(PhotoStore.ProfilePicsFolder, "old-photo.png");
                Assert.True(File.Exists(migrated));
                Assert.Equal(TestData.OnePixelPng(), File.ReadAllBytes(migrated));

                // The stored path format ("ProfilePics\old-photo.png") still resolves.
                string stored = Path.Combine("ProfilePics", "old-photo.png");
                Assert.True(File.Exists(PhotoStore.Resolve(stored)));
            }
            finally
            {
                PhotoStore.UseDefaultLegacySearchFolders();
                TryDeleteFolder(legacyFolder);
            }
        }

        [Fact]
        public void MigrateLegacyFolder_NeverOverwritesAnAlreadyMigratedPhoto()
        {
            string legacyFolder = NewTempFolder("cvlegacypics_");

            Directory.CreateDirectory(PhotoStore.ProfilePicsFolder);
            byte[] current = TestData.OnePixelPng();
            File.WriteAllBytes(Path.Combine(PhotoStore.ProfilePicsFolder, "same.png"), current);

            // A different file with the same name in the old folder.
            File.WriteAllBytes(Path.Combine(legacyFolder, "same.png"), new byte[] { 1, 2, 3, 4 });
            PhotoStore.UseLegacySearchFolders(legacyFolder);

            try
            {
                PhotoStore.MigrateLegacyFolder();

                Assert.Equal(current, File.ReadAllBytes(Path.Combine(PhotoStore.ProfilePicsFolder, "same.png")));
            }
            finally
            {
                PhotoStore.UseDefaultLegacySearchFolders();
                TryDeleteFolder(legacyFolder);
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Recreates the Clients table without PhotoPath and resets the schema version.</summary>
        private void SeedLegacyDatabase(int version)
        {
            SeedLegacyDatabaseIn(DatabaseHelper.DatabasePath, version);
        }

        private static void SeedLegacyDatabaseIn(string path, int version, string nic = "123456789V")
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var conn = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;");
            conn.Open();

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Clients (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT, Address TEXT, Phone TEXT,
                    Email TEXT, DOB DATE, NIC TEXT);");

            conn.Execute("INSERT INTO Clients (FullName, NIC) VALUES ('Legacy Person', @Nic);", new { Nic = nic });
            conn.Execute($"PRAGMA user_version = {version};");
        }

        private static string NewTempFolder(string prefix)
        {
            string folder = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void TryDeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // A leftover temp folder is not worth failing a test over.
            }
        }
    }
}
