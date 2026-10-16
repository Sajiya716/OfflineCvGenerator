using System;
using System.IO;
using System.Text;
using OfflineCvBioDataGenerator.Data;

namespace OfflineCvBioDataGenerator.Services
{
    /// <summary>
    /// Tiny append-only error log. It lives under <c>%AppData%\OfflineCvGenerator</c> so it
    /// survives an application update and is writable even when the app is installed in a
    /// protected folder such as Program Files.
    /// Unhandled errors are recorded here so the operator can report what happened
    /// instead of only seeing a message box.
    /// </summary>
    public static class ErrorLog
    {
        private static readonly object _sync = new();

        public static string LogFilePath => AppPaths.LogFile;

        public static void Write(Exception exception, string context)
        {
            if (exception == null) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}");
                sb.AppendLine(exception.ToString());
                sb.AppendLine(new string('-', 80));

                lock (_sync)
                {
                    string path = LogFilePath;
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.AppendAllText(path, sb.ToString());
                }
            }
            catch
            {
                // Logging must never throw and take the application down with it.
            }
        }
    }
}
