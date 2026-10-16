using System;
using System.IO;

namespace OfflineCvBioDataGenerator.Data
{
    /// <summary>
    /// Single source of truth for the folders that hold data which must survive an update.
    ///
    /// The auto-updater replaces the contents of the install folder, so nothing that the
    /// user has created may live there. The database, the profile photos and the error log
    /// are therefore kept under <c>%AppData%\OfflineCvGenerator</c>, which the updater
    /// never touches.
    ///
    /// Every member is overridable so the tests can point the whole application at a
    /// throwaway folder instead of the real profile.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Folder created inside the user's roaming profile.</summary>
        public const string FolderName = "OfflineCvGenerator";

        /// <summary>Name of the SQLite file.</summary>
        public const string DatabaseFileName = "clients.db";

        private static readonly object Sync = new();
        private static string _root = DefaultRoot;

        /// <summary>
        /// The normal location: <c>%AppData%\OfflineCvGenerator</c>
        /// (roaming, so it also follows the user on a domain profile).
        /// </summary>
        public static string DefaultRoot
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // Extremely unlikely, but never build a path out of an empty string.
                if (string.IsNullOrWhiteSpace(appData))
                    appData = Path.GetTempPath();

                return Path.Combine(appData, FolderName);
            }
        }

        /// <summary>Folder that holds all persisted user data.</summary>
        public static string Root
        {
            get { lock (Sync) return _root; }
        }

        /// <summary>Full path of the SQLite database.</summary>
        public static string DatabaseFile => Path.Combine(Root, DatabaseFileName);

        /// <summary>Folder holding the imported profile photos.</summary>
        public static string PhotoFolder => Path.Combine(Root, "ProfilePics");

        /// <summary>Where unhandled errors are appended.</summary>
        public static string LogFile => Path.Combine(Root, "error.log");

        /// <summary>Where the updater remembers "skip"/"remind me later" choices.</summary>
        public static string UpdaterStateFile => Path.Combine(Root, "update-state.json");

        /// <summary>Points the application at a different data root. Intended for tests.</summary>
        public static void UseRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Data root must not be empty.", nameof(root));

            lock (Sync)
            {
                _root = Path.GetFullPath(root);
            }
        }

        /// <summary>Restores the default <c>%AppData%</c> location.</summary>
        public static void UseDefaultRoot()
        {
            lock (Sync)
            {
                _root = DefaultRoot;
            }
        }

        /// <summary>Creates the data root if it does not exist yet.</summary>
        public static void EnsureRoot()
        {
            string root = Root;
            if (!string.IsNullOrWhiteSpace(root))
                Directory.CreateDirectory(root);
        }
    }
}
