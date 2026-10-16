# Auto-updates, data safety and schema migrations

## Where data lives

Everything that must survive an application update lives under `%AppData%\OfflineCvGenerator` (your Roaming profile). The auto-updater replaces the files in the install directory when a new release is extracted, so the following are **never** stored next to the executable:

- `clients.db` — the SQLite database (moved from the old `cvdb.sqlite` next to the executable)
- `ProfilePics\` — copied images used in CVs
- `error.log` — unhandled exception log
- `update-state.json` — remembers "Remind me later" and "Skip this version" choices (kept out of the install folder too)

The first time you launch a build that includes this change, the bootstrap code:

1. Copies an existing `cvdb.sqlite` (from the old install location or the project root) into `%AppData%\OfflineCvGenerator\clients.db` if the destination does not exist yet.
2. Copies photos from `ProfilePics\` in the old locations into `%AppData%\OfflineCvGenerator\ProfilePics\` without overwriting existing files.
3. Runs schema migrations (see below) and takes a dated backup of the database before upgrading it.

These moves are best-effort and will never block startup. A failing migration rolls back the database to its pre-upgrade state.

## Schema migrations

The database is versioned with `PRAGMA user_version`.

- `CurrentSchemaVersion = 1`
- Migrations run in order, each inside its own transaction (DDL + `user_version` share the same transaction). If a migration throws, SQLite rolls back the changes and the `user_version` stays at the old value.
- Before migrating, `InitializeDatabase` copies `clients.db` to `clients.backup-v{old}-{timestamp}.db` in the same folder. Only the newest five backups are kept.
- Future releases should bump `DatabaseHelper.CurrentSchemaVersion` and add a new entry to `DatabaseHelper.Migrations`. Never edit an already released migration.

## AutoUpdater.NET

- Package: `Autoupdater.NET.Official` v1.9.3.
- Configured in `Services/UpdateService.cs`.
- Assembly version is set in `OfflineCvBioDataGenerator.csproj` (`<Version>1.1.0</Version>`, `<AssemblyVersion>1.1.0.0</AssemblyVersion>`). AutoUpdater compares the manifest's `<version>` against the installed assembly version, so bump both for every release.
- Manifest URL: `UpdateService.DefaultFeedUrl` (constant). Replace it with your own raw GitHub URL, e.g. `https://raw.githubusercontent.com/<user>/<repo>/main/update.xml`.
- Checks run once, after the main window has been shown. Failures are silent in the automatic startup check so offline machines never show nagging dialogs. There is also `UpdateService.CheckForUpdatesNow(IWin32Window?)` for an explicit check (reports errors to the user).
- `Remind me later` / `Skip this version` state is saved to `%AppData%\OfflineCvGenerator\update-state.json` (not the registry alone), so it travels with the profile and survives updates.

## Publishing an update

1. Bump the version in `OfflineCvBioDataGenerator.csproj` (`Version`, `AssemblyVersion`, `FileVersion`). `AssemblyVersion` should be `x.y.z.0` and `<version>` in the XML should match (e.g. `1.1.0.0`).
2. Publish / build a distributable ZIP of the application. AutoUpdater.NET replaces the contents of the install folder with what is in the downloaded ZIP, so it must include the `.exe`, `.dll` files and required runtimes.
3. Create a new GitHub release (e.g. `v1.1.0`), attach the ZIP and publish it.
4. Update `update.xml` (at the URL pointed to by `FeedUrl`) with `<version>` and the new `<url>` (the release asset URL).
5. Commit and push `update.xml` (and your new default feed URL, if it changed).

## Manual test notes

- Tests run sequentially (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) because static state is held by `DatabaseHelper`, `PhotoStore`, `AppPaths` and `UpdateService`.
- All 150 tests pass (`dotnet test CvGenerator.sln`).
- To force a migration locally: drop/rename `%AppData%\OfflineCvGenerator\clients.db`, put an old `cvdb.sqlite` next to the executable, run the app once — it will be copied and migrated into `%AppData%`. Backups appear as `clients.backup-v*` under `%AppData%\OfflineCvGenerator\`.
- To test the "no data loss on relocate" logic: point the test infrastructure's legacy search folders at a throwaway location (the new migration tests do this without touching your real `%AppData%`).
- `ReportErrors = false` in the automatic check means an offline check is completely silent. An explicit check sets `ReportErrors = true` so the user sees a reason if something goes wrong.

## Gotchas

- WebView2 may be required if you show a changelog loaded from a URL. Use plain `<changelog>` text in `update.xml` if the WebView2 runtime may be absent; the test suite/config here only requires the default text.
- AutoUpdater.NET replaces the install directory wholesale — this is exactly why `%AppData%` was introduced. Do **not** put anything the user can edit (DB, photos, logs, settings) in the install folder going forward.
