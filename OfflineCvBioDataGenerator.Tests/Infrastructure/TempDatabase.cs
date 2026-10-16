using System;
using System.IO;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Services;

namespace OfflineCvBioDataGenerator.Tests.Infrastructure
{
    /// <summary>
    /// Gives each test its own throwaway SQLite file and ProfilePics folder, then
    /// removes them again. Because <see cref="DatabaseHelper"/> and
    /// <see cref="PhotoStore"/> hold static state, tests are run sequentially
    /// (see AssemblyInfo.cs).
    /// </summary>
    public sealed class TempDatabase : IDisposable
    {
        public string RootPath { get; }
        public string DatabasePath { get; }
        public string PhotoFolder { get; }

        public TempDatabase()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "cvtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);

            DatabasePath = Path.Combine(RootPath, "test-cvdb.sqlite");
            PhotoFolder = Path.Combine(RootPath, "ProfilePics");

            // Point the whole application at the throwaway folder so nothing in the real
            // %AppData% profile is touched (and no legacy data is migrated into a test).
            AppPaths.UseRoot(RootPath);
            DatabaseHelper.UseDatabasePath(DatabasePath);
            PhotoStore.UseFolder(PhotoFolder);
            DatabaseHelper.InitializeDatabase();
        }

        /// <summary>Writes a small valid PNG into the temp folder and returns its path.</summary>
        public string WritePng(string fileName = "photo.png")
        {
            string path = Path.Combine(RootPath, fileName);
            File.WriteAllBytes(path, TestData.OnePixelPng());
            return path;
        }

        /// <summary>Writes a small valid PNG outside the temp folder (simulates a user-picked file).</summary>
        public static string WritePngInNewFolder(string fileName = "picked.png")
        {
            string folder = Path.Combine(Path.GetTempPath(), "cvpics_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, TestData.OnePixelPng());
            return path;
        }

        public void Dispose()
        {
            // Put the application back on its normal locations before the folder disappears.
            AppPaths.UseDefaultRoot();
            DatabaseHelper.UseDefaultDatabasePath();
            PhotoStore.UseDefaultFolder();

            try
            {
                if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // A leftover temp folder is not worth failing a test over.
            }
        }
    }
}
