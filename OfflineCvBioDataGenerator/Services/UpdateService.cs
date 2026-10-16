using System;
using System.Reflection;
using System.Windows.Forms;
using AutoUpdaterDotNET;
using OfflineCvBioDataGenerator.Data;

namespace OfflineCvBioDataGenerator.Services
{
    /// <summary>
    /// Wraps AutoUpdater.NET (AutoUpdaterDotNET) so an update check can never hurt the app.
    ///
    /// The updater only ever replaces the contents of the install folder. Everything the user
    /// has created lives under <c>%AppData%\OfflineCvGenerator</c> (see <see cref="AppPaths"/>),
    /// so an update is guaranteed not to touch the clients, the photos or the log.
    ///
    /// Behaviour:
    ///  * The check runs on a background thread started after the main window is shown, so it
    ///    never delays startup.
    ///  * When a newer version is published the user gets the standard dialog with
    ///    "Update", "Remind Me Later" and "Skip This Version".
    ///  * Failures are silent by default: this is an offline-first application and a missing
    ///    internet connection must not produce pop-ups.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>
        /// URL of the update manifest (the XML file describing the newest release).
        ///
        /// Replace the placeholder with your own raw GitHub URL, for example:
        ///   https://raw.githubusercontent.com/&lt;user&gt;/&lt;repo&gt;/main/update.xml
        ///
        /// Leave it empty (or keep the placeholder) to disable update checks entirely.
        /// </summary>
        public const string DefaultFeedUrl =
            "https://raw.githubusercontent.com/YOUR_GITHUB_USER/YOUR_REPOSITORY/main/update.xml";

        /// <summary>Shown in the update dialog.</summary>
        public const string ApplicationTitle = "Offline CV & Bio-Data Generator";

        private static readonly object Sync = new();
        private static string _feedUrl = DefaultFeedUrl;
        private static bool _startupCheckDone;

        /// <summary>The manifest URL actually used. Defaults to <see cref="DefaultFeedUrl"/>.</summary>
        public static string FeedUrl => _feedUrl;

        /// <summary>Overrides the manifest URL. Intended for tests.</summary>
        public static void UseFeedUrl(string? url)
        {
            lock (Sync)
            {
                _feedUrl = url ?? string.Empty;
                _startupCheckDone = false;
            }
        }

        /// <summary>Restores <see cref="DefaultFeedUrl"/>.</summary>
        public static void UseDefaultFeedUrl()
        {
            UseFeedUrl(DefaultFeedUrl);
        }

        /// <summary>
        /// False while <see cref="FeedUrl"/> is still the placeholder, in which case no
        /// network request is made at all.
        /// </summary>
        public static bool IsConfigured
        {
            get
            {
                string url = _feedUrl;
                return !string.IsNullOrWhiteSpace(url) &&
                       !url.Contains("YOUR_GITHUB", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Called once, after the main window is visible. Quietly does nothing when the feed
        /// has not been configured yet.
        /// </summary>
        public static void CheckForUpdatesOnStartup(IWin32Window? owner)
        {
            lock (Sync)
            {
                if (_startupCheckDone) return;
                _startupCheckDone = true;
            }

            Start(owner, userInitiated: false);
        }

        /// <summary>
        /// An explicit check (e.g. from a menu item). Unlike the startup check this one
        /// reports problems to the user instead of staying silent.
        /// </summary>
        public static void CheckForUpdatesNow(IWin32Window? owner)
        {
            Start(owner, userInitiated: true);
        }

        private static void Start(IWin32Window? owner, bool userInitiated)
        {
            string url = FeedUrl;
            if (!IsConfigured) return;

            try
            {
                // The updater persists its state under the registry by default; keep it in
                // %AppData% instead so "skip"/"remind me later" survive an update.
                AppPaths.EnsureRoot();

                AutoUpdater.AppTitle = ApplicationTitle;
                AutoUpdater.AppCastURL = url;
                AutoUpdater.InstalledVersion = Assembly.GetExecutingAssembly().GetName().Version;

                // Standard "Update / Remind Me Later / Skip This Version" dialog.
                AutoUpdater.ShowRemindLaterButton = true;
                AutoUpdater.ShowSkipButton = true;
                AutoUpdater.LetUserSelectRemindLater = false;
                AutoUpdater.RemindLaterTimeSpan = RemindLaterFormat.Days;
                AutoUpdater.RemindLaterAt = 2;

                // Never ask for elevation and never wipe files that are not part of the
                // release (the data is in %AppData% anyway).
                AutoUpdater.RunUpdateAsAdmin = false;
                AutoUpdater.ClearAppDirectory = false;

                // A user-initiated check should tell them what happened; the automatic one
                // must stay quiet so an offline machine shows nothing at all.
                AutoUpdater.ReportErrors = userInitiated;

                AutoUpdater.TopMost = true;
                AutoUpdater.UpdateFormSize = new System.Drawing.Size(760, 520);
                AutoUpdater.PersistenceProvider = new JsonFilePersistenceProvider(AppPaths.UpdaterStateFile);

                AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;

                if (owner != null)
                {
                    try { AutoUpdater.SetOwner(owner); } catch { /* not fatal */ }
                }

                AutoUpdater.Start(url, Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                // A broken update check must never take the application down.
                ErrorLog.Write(ex, "Update check");
            }
        }

        /// <summary>
        /// Fires after the manifest has been read. AutoUpdater.NET shows its own dialog when
        /// an update is available; here we only record failures for troubleshooting.
        /// </summary>
        private static void OnCheckForUpdate(UpdateInfoEventArgs args)
        {
            if (args?.Error == null) return;

            ErrorLog.Write(
                new Exception("Auto-update check failed: " + args.Error),
                "Update check");
        }
    }
}
