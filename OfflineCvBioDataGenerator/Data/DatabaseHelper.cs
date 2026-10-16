using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace OfflineCvBioDataGenerator.Data
{
    /// <summary>
    /// Central access point for the SQLite database.
    ///
    /// Design notes:
    ///  * The database lives under <c>%AppData%\OfflineCvGenerator\clients.db</c>
    ///    (see <see cref="AppPaths"/>), never inside the install folder. The auto-updater
    ///    replaces the install folder when a new version arrives, so keeping the data
    ///    elsewhere is what guarantees zero data loss.
    ///  * <see cref="DatabasePath"/> is overridable so unit tests can point the app at a throwaway file.
    ///  * Connections are created per operation and disposed by the caller (via <c>using</c>), so no
    ///    handles are left open between operations.
    ///  * Pooling is explicitly disabled so that disposing a connection really closes the file -
    ///    this keeps tests (and the app) from hitting "file in use" when deleting/replacing the DB.
    ///  * The schema is versioned through <c>PRAGMA user_version</c>. <see cref="InitializeDatabase"/>
    ///    brings an old database up to <see cref="CurrentSchemaVersion"/> by running the pending
    ///    migrations in order, each inside its own transaction, after taking a backup copy.
    /// </summary>
    public static class DatabaseHelper
    {
        /// <summary>
        /// Schema version this build creates/expects.
        ///
        /// * 1 - baseline: Clients / Education / Experience, index on Clients.NIC,
        ///       and the Clients.PhotoPath column.
        ///
        /// To evolve the schema: bump this number and append a matching entry to
        /// <see cref="Migrations"/>. Never edit an already shipped migration.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>How many automatic pre-migration backups to keep.</summary>
        private const int BackupsToKeep = 5;

        private const string VersionPragma = "PRAGMA user_version;";

        private static readonly object _sync = new();

        private static string _databasePath = AppPaths.DatabaseFile;
        private static bool _pathOverridden;
        private static IReadOnlyList<string>? _legacySearchFoldersOverride;

        /// <summary>
        /// The ordered list of schema migrations. Each entry is applied only when the
        /// database's <c>user_version</c> is lower than the entry's version.
        /// </summary>
        private static readonly Migration[] Migrations =
        {
            new Migration(
                1,
                "Baseline schema (Clients, Education, Experience) and Clients.PhotoPath",
                ApplyBaselineSchema),

            // Add future migrations here, for example:
            //
            // new Migration(2, "Add Clients.CreatedAt", conn =>
            //     Run(conn, "ALTER TABLE Clients ADD COLUMN CreatedAt TEXT NULL;")),
        };

        /// <summary>Full path of the SQLite file currently in use.</summary>
        public static string DatabasePath
        {
            get { lock (_sync) return _databasePath; }
        }

        /// <summary>Connection string used for every new connection.</summary>
        public static string ConnectionString =>
            $"Data Source={_databasePath};Version=3;Pooling=False;Default Timeout=30;";

        /// <summary>
        /// Points the application at a different database file.
        /// Intended for tests; the application itself uses the default location.
        /// </summary>
        public static void UseDatabasePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Database path must not be empty.", nameof(path));

            lock (_sync)
            {
                _databasePath = Path.GetFullPath(path);
                _pathOverridden = true;
            }
        }

        /// <summary>Restores the default (<c>%AppData%\OfflineCvGenerator\clients.db</c>) location.</summary>
        public static void UseDefaultDatabasePath()
        {
            lock (_sync)
            {
                _databasePath = AppPaths.DatabaseFile;
                _pathOverridden = false;
            }
        }

        /// <summary>
        /// Overrides the folders searched for a database left behind by an older release.
        /// Intended for tests; the application itself uses the real install folder.
        /// </summary>
        public static void UseLegacySearchFolders(params string[] folders)
        {
            lock (_sync)
            {
                _legacySearchFoldersOverride = folders == null
                    ? Array.Empty<string>()
                    : folders.Select(Path.GetFullPath).ToArray();
            }
        }

        /// <summary>Restores the default legacy search folders.</summary>
        public static void UseDefaultLegacySearchFolders()
        {
            lock (_sync)
            {
                _legacySearchFoldersOverride = null;
            }
        }

        /// <summary>Creates and opens a new connection. Callers must dispose it.</summary>
        public static IDbConnection GetConnection()
        {
            string fullPath = Path.GetFullPath(_databasePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var conn = new SQLiteConnection(ConnectionString);
            conn.Open();

            // SQLite does not enforce FOREIGN KEY clauses unless this pragma is set per connection.
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys=ON;";
                pragma.ExecuteNonQuery();
            }

            return conn;
        }

        /// <summary>
        /// Brings the database up to <see cref="CurrentSchemaVersion"/>.
        ///
        /// Steps:
        ///  1. Copy a database left by an older release from the install folder into <c>%AppData%</c>.
        ///  2. Create the file/schema if it does not exist.
        ///  3. Run every migration whose version is newer than the stored <c>user_version</c>,
        ///     each in its own transaction, after backing up the file.
        ///
        /// Safe to call on every start and safe to call more than once.
        /// </summary>
        public static void InitializeDatabase()
        {
            lock (_sync)
            {
                MigrateLegacyDatabaseFile();

                int currentVersion;
                using (IDbConnection probe = GetConnection())
                {
                    currentVersion = GetSchemaVersion(probe);
                }

                if (currentVersion > CurrentSchemaVersion)
                {
                    // Never guess: an older build must not "downgrade" a newer file.
                    throw new InvalidOperationException(
                        $"The database uses schema version {currentVersion}, but this build only " +
                        $"understands version {CurrentSchemaVersion}. " +
                        "Install the latest version of the application and try again. " +
                        $"Your data was left untouched at: {DatabasePath}");
                }

                if (currentVersion == CurrentSchemaVersion)
                {
                    return;
                }

                BackupBeforeMigration(currentVersion);

                using IDbConnection conn = GetConnection();

                foreach (Migration migration in Migrations
                             .Where(m => m.Version > currentVersion)
                             .OrderBy(m => m.Version))
                {
                    ApplyMigration(conn, migration);
                }
            }
        }

        /// <summary>Reads <c>PRAGMA user_version</c>. Returns 0 for a brand new or legacy file.</summary>
        public static int GetSchemaVersion(IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using IDbCommand cmd = connection.CreateCommand();
            cmd.CommandText = VersionPragma;

            object? result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return 0;

            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Applies a single migration. The schema change and the <c>user_version</c> bump
        /// share one transaction, so a failure leaves the database exactly as it was.
        /// </summary>
        private static void ApplyMigration(IDbConnection conn, Migration migration)
        {
            // foreign_keys cannot be changed while a transaction is open, so do it first.
            Run(conn, "PRAGMA foreign_keys=OFF;");
            try
            {
                using IDbTransaction tx = conn.BeginTransaction();
                try
                {
                    migration.Apply(conn);

                    // The value is an int we control, so string interpolation is safe here
                    // (PRAGMA statements cannot take parameters).
                    Run(conn, $"PRAGMA user_version={migration.Version};", tx);

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* connection already broken */ }
                    throw;
                }
            }
            finally
            {
                Run(conn, "PRAGMA foreign_keys=ON;");
            }
        }

        /// <summary>Version 1: the original three tables plus the photo column.</summary>
        private static void ApplyBaselineSchema(IDbConnection conn)
        {
            Run(conn, @"
                CREATE TABLE IF NOT EXISTS Clients (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL DEFAULT '',
                    Address TEXT NOT NULL DEFAULT '',
                    Phone TEXT NOT NULL DEFAULT '',
                    Email TEXT NOT NULL DEFAULT '',
                    DOB DATE NULL,
                    NIC TEXT NOT NULL DEFAULT '',
                    PhotoPath TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS Education (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientId INTEGER NOT NULL,
                    Qualification TEXT NOT NULL DEFAULT '',
                    Institute TEXT NOT NULL DEFAULT '',
                    Year TEXT NOT NULL DEFAULT '',
                    Results TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY (ClientId) REFERENCES Clients(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Experience (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientId INTEGER NOT NULL,
                    JobTitle TEXT NOT NULL DEFAULT '',
                    Company TEXT NOT NULL DEFAULT '',
                    Duration TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY (ClientId) REFERENCES Clients(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Education_ClientId ON Education(ClientId);
                CREATE INDEX IF NOT EXISTS IX_Experience_ClientId ON Experience(ClientId);
                CREATE INDEX IF NOT EXISTS IX_Clients_NIC ON Clients(NIC);
            ");

            // Databases created before the photo feature existed are missing this column.
            // A NOT NULL column with a constant default can be added in place, no table rebuild.
            AddColumnIfMissing(conn, "Clients", "PhotoPath", "TEXT NOT NULL DEFAULT ''");
        }

        private static void AddColumnIfMissing(IDbConnection conn, string table, string column, string type)
        {
            if (ColumnExists(conn, table, column)) return;

            Run(conn, $"ALTER TABLE {table} ADD COLUMN {column} {type};");
        }

        private static bool ColumnExists(IDbConnection conn, string table, string column)
        {
            using IDbCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";

            using IDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Runs a statement, optionally inside a transaction.</summary>
        private static void Run(IDbConnection conn, string sql, IDbTransaction? transaction = null)
        {
            using IDbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = transaction;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Copies the database to a timestamped sibling before a schema change, so a failed
        /// upgrade can always be recovered by hand. Keeps the newest <see cref="BackupsToKeep"/>.
        /// Never throws - a backup failure must not stop the application from starting.
        /// </summary>
        private static void BackupBeforeMigration(int fromVersion)
        {
            try
            {
                string path = Path.GetFullPath(_databasePath);
                if (!File.Exists(path)) return;
                if (new FileInfo(path).Length == 0) return;

                string dir = Path.GetDirectoryName(path) ?? ".";
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Two upgrades inside the same second must not collide.
                string backup = Path.Combine(dir, $"clients.backup-v{fromVersion}-{stamp}.db");
                for (int i = 1; File.Exists(backup); i++)
                {
                    backup = Path.Combine(dir, $"clients.backup-v{fromVersion}-{stamp}-{i}.db");
                }

                File.Copy(path, backup, overwrite: false);
                PruneBackups(dir);
            }
            catch
            {
                // Best effort only.
            }
        }

        private static void PruneBackups(string directory)
        {
            try
            {
                List<string> oldBackups = Directory
                    .GetFiles(directory, "clients.backup-v*.db")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(BackupsToKeep)
                    .ToList();

                foreach (string file in oldBackups)
                {
                    try { File.Delete(file); } catch { /* in use / already gone */ }
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        /// <summary>
        /// Copies a database left in the install folder by an older release into the new
        /// <c>%AppData%</c> location, once, so existing users keep their data after upgrading.
        ///
        /// Sources, in priority order:
        ///   1. <c>&lt;install&gt;\cvdb.sqlite</c>       - the previous released location.
        ///   2. <c>&lt;install&gt;\clients.db</c>        - in case a build used the new name but
        ///                                          a build placed it next to the executable.
        ///   3. <c>&lt;project root&gt;\cvdb.sqlite</c>  - very old dev builds (bin/Debug/net8.0-windows).
        /// </summary>
        private static void MigrateLegacyDatabaseFile()
        {
            if (_pathOverridden) return;

            try
            {
                string destination = Path.GetFullPath(_databasePath);
                if (File.Exists(destination)) return;

                foreach (string source in LegacyDatabaseFiles())
                {
                    if (!File.Exists(source)) continue;

                    string? dir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.Copy(source, destination);
                    return;
                }
            }
            catch
            {
                // Migration is best effort - a failure here must never block application start.
            }
        }

        private static IEnumerable<string> LegacyDatabaseFiles()
        {
            if (_legacySearchFoldersOverride != null)
            {
                foreach (string folder in _legacySearchFoldersOverride)
                {
                    yield return Path.Combine(folder, "cvdb.sqlite");
                    yield return Path.Combine(folder, "clients.db");
                }

                yield break;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            yield return Path.GetFullPath(Path.Combine(baseDir, "cvdb.sqlite"));
            yield return Path.GetFullPath(Path.Combine(baseDir, "clients.db"));
            yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "cvdb.sqlite"));
        }

        /// <summary>A single schema upgrade.</summary>
        private sealed class Migration
        {
            public Migration(int version, string description, Action<IDbConnection> apply)
            {
                Version = version;
                Description = description;
                Apply = apply;
            }

            /// <summary>The <c>user_version</c> this migration produces.</summary>
            public int Version { get; }

            /// <summary>Human readable summary (used in comments/tests).</summary>
            public string Description { get; }

            /// <summary>The statements that perform the upgrade.</summary>
            public Action<IDbConnection> Apply { get; }
        }
    }
}
