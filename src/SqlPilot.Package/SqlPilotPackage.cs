using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlPilot.Core.Favorites;
using SqlPilot.Core.Recents;
using SqlPilot.Core.Search;
using SqlPilot.Core.Settings;
using SqlPilot.Package.Commands;
using SqlPilot.Package.Integration;
using SqlPilot.Package.Services;
using Task = System.Threading.Tasks.Task;

namespace SqlPilot.Package
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SQL Pilot", "Quick Search Tool for SSMS", "1.0.0")]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(SqlPilotToolWindow), Style = VsDockStyle.Tabbed,
        Window = "d114938f-591c-46cf-a785-500a82d97410")]
    public sealed class SqlPilotPackage : AsyncPackage
    {
        public const string PackageGuidString = "8f4a3b2e-1c5d-4e6f-9a0b-7d8c2e3f4a5b";

        internal static string DataDirectory { get; private set; }
        internal SearchEngine SearchEngine { get; private set; }
        internal FavoritesStore FavoritesStore { get; private set; }
        internal RecentObjectsStore RecentsStore { get; private set; }
        internal FileSettingsProvider SettingsProvider { get; private set; }
        internal ObjectExplorerBridge ObjectExplorerBridge { get; private set; }
        internal ScriptingBridge ScriptingBridge { get; private set; }
        private ConnectionWatcher _connectionWatcher;

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // Initialize data stores
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SqlPilot");

            FavoritesStore = new FavoritesStore(Path.Combine(DataDirectory, "favorites.json"));
            FavoritesStore.Load();

            RecentsStore = new RecentObjectsStore(Path.Combine(DataDirectory, "recents.json"));
            RecentsStore.Load();

            SettingsProvider = new FileSettingsProvider(Path.Combine(DataDirectory, "settings.json"));
            SearchEngine = new SearchEngine(FavoritesStore, RecentsStore);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Initialize SSMS bridges
            ObjectExplorerBridge = new ObjectExplorerBridge(this);
            ScriptingBridge = new ScriptingBridge(this);

            // Register commands + Ctrl+D keybinding
            try { await OpenSearchCommand.InitializeAsync(this); }
            catch { }

            // Auto-show the tool window
            try
            {
                var window = FindToolWindow(typeof(SqlPilotToolWindow), 0, true);
                if (window?.Frame is IVsWindowFrame frame)
                {
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
                }
            }
            catch { /* Tool window will be available via View menu */ }

            // Start polling for new server connections
            _connectionWatcher = new ConnectionWatcher(this);
            _connectionWatcher.ServerConnected += OnServerConnected;

            // Check for updates: fire-and-forget; all I/O runs off the UI thread.
            // Throttled to once per 24h via LastUpdateCheckUtc; timestamp is persisted
            // BEFORE the HTTP call so a slow/failed request can't flood the API across
            // rapid SSMS restarts.
            _ = Task.Run(async () =>
            {
                var settings = SettingsProvider.GetSettings();
                if (!settings.CheckForUpdates) return;

                var now = DateTime.UtcNow;
                if (settings.LastUpdateCheckUtc != null
                    && (now - settings.LastUpdateCheckUtc.Value) < TimeSpan.FromHours(24))
                {
                    return;
                }

                settings.LastUpdateCheckUtc = now;
                SettingsProvider.SaveSettings(settings);

                var update = await UpdateChecker.CheckForUpdateAsync(settings.SkippedVersion);
                if (!update.IsUpdateAvailable) return;

                await JoinableTaskFactory.SwitchToMainThreadAsync();
                var window = FindToolWindow(typeof(SqlPilotToolWindow), 0, false);
                if (window is SqlPilotToolWindow tw && tw.Content is SqlPilotToolWindowControl ctrl)
                {
                    ctrl.ShowUpdateNotification(update.LatestVersion, update.ReleaseUrl);
                }
            });
        }

        private void OnServerConnected(string serverName)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();

                var window = FindToolWindow(typeof(SqlPilotToolWindow), 0, false);
                if (window is SqlPilotToolWindow tw && tw.Content is SqlPilotToolWindowControl ctrl)
                {
                    await ctrl.RefreshIndexAsync();
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connectionWatcher?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
