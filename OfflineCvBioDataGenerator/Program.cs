using System;
using System.Windows.Forms;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.Services;

namespace OfflineCvBioDataGenerator
{
    static class Program
    {
        /// <summary>
        /// Application entry point.
        ///
        /// Startup order matters:
        ///  1. QuestPDF licence + fonts.
        ///  2. Global exception handlers, so nothing crashes silently later.
        ///  3. Data bootstrap - %AppData% root, database relocation/upgrade, photo relocation.
        ///  4. Show the main window.
        ///  5. Kick off the (non-blocking) update check once the window is visible.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Both the licence and the fonts must be set up before any document is created.
            PdfGenerator.EnsureLicense();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) =>
            {
                ErrorLog.Write(args.Exception, "UI thread (unhandled)");
                MessageBox.Show(
                    "An unexpected error occurred:\n\n" + args.Exception.Message +
                    "\n\nDetails were written to " + ErrorLog.LogFilePath + ".",
                    "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    ErrorLog.Write(ex, "AppDomain (unhandled)");
            };

            try
            {
                InitializeApplicationData();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "Startup (data initialisation)");
                MessageBox.Show(
                    "The application could not open its data:\n\n" + ex.Message +
                    "\n\nYour existing data was left untouched.\nDetails were written to " +
                    ErrorLog.LogFilePath + ".",
                    "Startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var mainForm = new Form1();

            // Check for a new release only after the window is on screen, so a slow or
            // missing network connection can never hold up the user.
            mainForm.Shown += (_, _) => UpdateService.CheckForUpdatesOnStartup(mainForm);

            Application.Run(mainForm);
        }

        /// <summary>
        /// Prepares the folders that live outside the install directory (%AppData%), creates
        /// or upgrades the database and pulls any data left behind by an older release into
        /// the new location. Safe to run more than once.
        /// </summary>
        private static void InitializeApplicationData()
        {
            // %AppData%\OfflineCvGenerator - the updater never touches this folder.
            AppPaths.EnsureRoot();

            // Creates the schema when missing, runs pending migrations when the schema is
            // older, and backs the file up first. Copies a database found in the old
            // install-folder location across before doing so.
            DatabaseHelper.InitializeDatabase();

            // Moves photos out of the install folder into %AppData% (once).
            PhotoStore.MigrateLegacyFolder();
            PhotoStore.EnsureFolder();
        }
    }
}
