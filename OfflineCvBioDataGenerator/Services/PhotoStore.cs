using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfflineCvBioDataGenerator.Data;

namespace OfflineCvBioDataGenerator.Services
{
    /// <summary>
    /// Owns the "ProfilePics" folder: importing a chosen photo and resolving the stored path.
    ///
    /// Contract:
    ///  * Photos live under <c>%AppData%\OfflineCvGenerator\ProfilePics</c> (see
    ///    <see cref="AppPaths"/>), never inside the install folder, so an application update
    ///    cannot delete them.
    ///  * The database only ever stores a path relative to the data root (or an absolute path
    ///    if the file lives outside it).
    ///  * Importing a photo that is already inside the profile folder is a no-op, so saving
    ///    the same client repeatedly never creates photo_1.jpg, photo_2.jpg, ... copies.
    /// </summary>
    public static class PhotoStore
    {
        /// <summary>Set only by <see cref="UseFolder"/> (tests). Otherwise the data root decides.</summary>
        private static string? _folderOverride;

        /// <summary>Set only by <see cref="UseLegacySearchFolders"/> (tests).</summary>
        private static IReadOnlyList<string>? _legacySearchFoldersOverride;

        /// <summary>Absolute folder where profile photos are kept.</summary>
        public static string ProfilePicsFolder => _folderOverride ?? AppPaths.PhotoFolder;

        /// <summary>Overrides the photo folder (used by tests).</summary>
        public static void UseFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Photo folder must not be empty.", nameof(folder));

            _folderOverride = Path.GetFullPath(folder);
        }

        /// <summary>Restores the default folder (<c>%AppData%\OfflineCvGenerator\ProfilePics</c>).</summary>
        public static void UseDefaultFolder()
        {
            _folderOverride = null;
        }

        /// <summary>
        /// Overrides the folders searched for photos left behind by an older release.
        /// Intended for tests; the application itself uses the real install folder.
        /// </summary>
        public static void UseLegacySearchFolders(params string[] folders)
        {
            _legacySearchFoldersOverride = folders == null
                ? Array.Empty<string>()
                : folders.Select(Path.GetFullPath).ToArray();
        }

        /// <summary>Restores the default legacy search folders.</summary>
        public static void UseDefaultLegacySearchFolders()
        {
            _legacySearchFoldersOverride = null;
        }

        /// <summary>Creates the photo folder if it does not exist yet.</summary>
        public static void EnsureFolder()
        {
            Directory.CreateDirectory(ProfilePicsFolder);
        }

        /// <summary>
        /// Copies photos left in the install folder by an older release into the new
        /// <c>%AppData%</c> folder, once. Existing files are never overwritten, so this is
        /// safe to run on every start. The stored relative paths ("ProfilePics\photo.png")
        /// keep working because they are now resolved against the data root.
        /// </summary>
        public static void MigrateLegacyFolder()
        {
            try
            {
                string destination = ProfilePicsFolder;

                foreach (string legacy in LegacyPhotoFolders())
                {
                    if (!Directory.Exists(legacy)) continue;

                    Directory.CreateDirectory(destination);

                    foreach (string file in Directory.GetFiles(legacy))
                    {
                        string target = Path.Combine(destination, Path.GetFileName(file));
                        if (File.Exists(target)) continue;

                        try { File.Copy(file, target); }
                        catch { /* one unreadable photo must not stop the rest */ }
                    }
                }
            }
            catch
            {
                // Best effort only - never block application start.
            }
        }

        private static IEnumerable<string> LegacyPhotoFolders()
        {
            if (_legacySearchFoldersOverride != null)
            {
                foreach (string folder in _legacySearchFoldersOverride)
                    yield return folder;

                yield break;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            yield return Path.GetFullPath(Path.Combine(baseDir, "ProfilePics"));
            yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "ProfilePics"));
        }

        /// <summary>
        /// Turns a stored path (relative or absolute) into a full path.
        /// Returns <see cref="string.Empty"/> when there is no photo.
        /// </summary>
        public static string Resolve(string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return string.Empty;

            return Path.IsPathRooted(storedPath)
                ? storedPath
                : Path.GetFullPath(Path.Combine(AppPaths.Root, storedPath));
        }

        /// <summary>
        /// Copies <paramref name="sourcePath"/> into the profile folder (unless it is already
        /// there) and returns the value that should be stored in the database.
        /// </summary>
        public static string Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source photo path must not be empty.", nameof(sourcePath));

            string fullSource = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSource))
                throw new FileNotFoundException("The selected photo could not be found.", fullSource);

            EnsureFolder();

            if (IsInsideProfileFolder(fullSource))
            {
                // Already imported - reuse it instead of creating numbered duplicates.
                return ToStoredPath(fullSource);
            }

            string destination = BuildUniqueDestination(fullSource);

            // BuildUniqueDestination returns an existing file when it holds exactly the
            // same picture, in which case there is nothing to copy.
            if (!File.Exists(destination))
            {
                File.Copy(fullSource, destination);
            }

            return ToStoredPath(destination);
        }

        private static bool IsInsideProfileFolder(string fullSourcePath)
        {
            string folder = Path.GetFullPath(ProfilePicsFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullSourcePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildUniqueDestination(string fullSource)
        {
            string name = Path.GetFileName(fullSource);
            string dest = Path.Combine(ProfilePicsFolder, name);

            if (!File.Exists(dest)) return dest;

            // The same picture saved twice must not produce "photo_1.png".
            if (FilesAreIdentical(fullSource, dest)) return dest;

            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);

            for (int i = 1; i < 10_000; i++)
            {
                string candidate = Path.Combine(ProfilePicsFolder, $"{stem}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
                if (FilesAreIdentical(fullSource, candidate)) return candidate;
            }

            return Path.Combine(ProfilePicsFolder, $"{stem}_{Guid.NewGuid():N}{ext}");
        }

        private static bool FilesAreIdentical(string first, string second)
        {
            try
            {
                var firstInfo = new FileInfo(first);
                var secondInfo = new FileInfo(second);
                if (firstInfo.Length != secondInfo.Length) return false;

                const int bufferSize = 64 * 1024;
                byte[] bufferA = new byte[bufferSize];
                byte[] bufferB = new byte[bufferSize];

                using var streamA = File.OpenRead(first);
                using var streamB = File.OpenRead(second);

                while (true)
                {
                    int readA = streamA.Read(bufferA, 0, bufferSize);
                    int readB = streamB.Read(bufferB, 0, bufferSize);

                    if (readA != readB) return false;
                    if (readA == 0) return true;

                    for (int i = 0; i < readA; i++)
                    {
                        if (bufferA[i] != bufferB[i]) return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ToStoredPath(string fullPath)
        {
            string relative = Path.GetRelativePath(AppPaths.Root, fullPath);

            // Keep it relative only when it really is under the data root;
            // otherwise '..\' paths would be fragile across machines.
            return relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative;
        }
    }
}
