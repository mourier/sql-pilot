using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using SqlPilot.Core.Database;
using SqlPilot.Package.Services;
using SqlPilot.Smo;
using SqlPilot.UI.Controls;
using SqlPilot.UI.ViewModels;
using Task = System.Threading.Tasks.Task;

namespace SqlPilot.Package
{
    public partial class SqlPilotToolWindowControl : UserControl
    {
        private readonly SqlPilotPackage _package;
        private UpdateInfo _pendingUpdate;

        public SearchViewModel ViewModel { get; }

        public ScopeViewModel ScopeViewModel { get; }

        public SqlPilotToolWindowControl(SqlPilotPackage package)
        {
            _package = package;

            InitializeComponent();

            ViewModel = new SearchViewModel(package.SearchEngine, package.FavoritesStore, package.RecentsStore);
            ViewModel.DebounceMs = package.SettingsProvider.GetSettings().SearchDebounceMs;
            SearchPanel.DataContext = ViewModel;

            ScopeViewModel = new ScopeViewModel(package.ScopeStore);
            ScopeViewModel.ScopeToggled += OnScopeToggled;
            ScopePanel.DataContext = ScopeViewModel;

            SearchPanel.ActionRequested += OnActionRequested;
        }

        public async Task RefreshIndexAsync()
        {
            try
            {
                IndexStatus.Text = "Indexing...";
                SetBusy(true);

                var servers = _package.ObjectExplorerBridge.GetConnectedServerNames();

                if (servers.Count == 0)
                {
                    IndexStatus.Text = "No servers connected. Select a server in Object Explorer and click Refresh.";
                    return;
                }

                _package.SearchEngine.ClearAll();
                ScopeViewModel.PruneServers(servers);

                foreach (var serverName in servers)
                {
                    IndexStatus.Text = $"Connecting to {serverName}...";

                    var connInfo = _package.ObjectExplorerBridge.GetConnectionInfo(serverName);
                    var smoProvider = new SmoDatabaseObjectProvider(connInfo);

                    IndexStatus.Text = $"Loading databases from {serverName}...";
                    var databases = await smoProvider.GetDatabaseNamesAsync(serverName);

                    // Enumerated even for out-of-scope servers: it's a single cheap
                    // metadata query, and it's what keeps the scope tree (and the
                    // "x of y database(s)" status) honest. The expensive part —
                    // crawling each database's objects — is what scope skips.
                    ScopeViewModel.MergeServer(serverName, databases);

                    var inScope = databases
                        .Where(db => _package.ScopeStore.IsDatabaseIncluded(serverName, db))
                        .ToList();

                    if (inScope.Count == 0) continue;

                    await IndexDatabasesAsync(serverName, smoProvider, inScope);
                }

                ScopeViewModel.UpdateSummary();
                IndexStatus.Text = ScopeViewModel.DescribeIndexStatus(_package.SearchEngine.GetIndexedObjectCount());

                // Re-run any pending search now that the index is populated
                ViewModel.Rerun();
            }
            catch (Exception ex)
            {
                IndexStatus.Text = $"Indexing error: {ex.Message}";
                Debug.WriteLine($"SqlPilot index error: {ex}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>
        /// Index a set of databases on one server. Parallelized — each SMO call creates
        /// its own connection — with concurrency capped so we don't exhaust the
        /// connection pool on servers with hundreds of databases.
        /// </summary>
        private async Task IndexDatabasesAsync(
            string serverName, IDatabaseObjectProvider provider, IReadOnlyList<string> databases)
        {
            int completedDbs = 0;
            IndexStatus.Text = $"Indexing {serverName} (0/{databases.Count})...";

            using (var throttle = new System.Threading.SemaphoreSlim(20))
            {
                var dbTasks = databases.Select(async dbName =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        await Task.Run(() => _package.SearchEngine.RefreshIndexAsync(serverName, dbName, provider));
                        int done = System.Threading.Interlocked.Increment(ref completedDbs);
                        // Fire-and-forget status update — no need to await UI thread hop
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            IndexStatus.Text = $"Indexing {serverName} ({done}/{databases.Count}) — {dbName}";
                        }));
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }).ToList();

                await Task.WhenAll(dbTasks);
            }
        }

        private void ScopeButton_Toggled(object sender, RoutedEventArgs e)
        {
            ScopePanel.Visibility = ScopeButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnScopeToggled(object sender, ScopeToggleEventArgs e)
        {
            _ = ApplyScopeToggleAsync(e);
        }

        /// <summary>
        /// Bring the index in line with a scope toggle. Excluding drops the affected
        /// buckets; including spot-indexes just what came back into scope, so no full
        /// re-index is needed either way.
        /// </summary>
        private async Task ApplyScopeToggleAsync(ScopeToggleEventArgs e)
        {
            try
            {
                SetBusy(true);

                if (e.DatabaseName == null)
                {
                    if (e.Included)
                        await SpotIndexServerAsync(e.ServerName);
                    else
                        _package.SearchEngine.ClearServer(e.ServerName);
                }
                else
                {
                    if (e.Included)
                        await SpotIndexDatabaseAsync(e.ServerName, e.DatabaseName);
                    else
                        _package.SearchEngine.ClearDatabase(e.ServerName, e.DatabaseName);
                }

                IndexStatus.Text = ScopeViewModel.DescribeIndexStatus(_package.SearchEngine.GetIndexedObjectCount());
                ViewModel.Rerun();
            }
            catch (Exception ex)
            {
                IndexStatus.Text = $"Scope update error: {ex.Message}";
                Debug.WriteLine($"SqlPilot scope error: {ex}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SpotIndexDatabaseAsync(string serverName, string databaseName)
        {
            IndexStatus.Text = $"Indexing {databaseName}...";

            var connInfo = _package.ObjectExplorerBridge.GetConnectionInfo(serverName);
            var provider = new SmoDatabaseObjectProvider(connInfo);

            await Task.Run(() => _package.SearchEngine.RefreshIndexAsync(serverName, databaseName, provider));
        }

        private async Task SpotIndexServerAsync(string serverName)
        {
            IndexStatus.Text = $"Connecting to {serverName}...";

            var connInfo = _package.ObjectExplorerBridge.GetConnectionInfo(serverName);
            var provider = new SmoDatabaseObjectProvider(connInfo);

            var databases = await provider.GetDatabaseNamesAsync(serverName);
            ScopeViewModel.MergeServer(serverName, databases);

            var inScope = databases
                .Where(db => _package.ScopeStore.IsDatabaseIncluded(serverName, db))
                .ToList();

            if (inScope.Count > 0)
                await IndexDatabasesAsync(serverName, provider, inScope);
        }

        private void SetBusy(bool busy)
        {
            RefreshButton.IsEnabled = !busy;
            ScopeTree.IsEnabled = !busy;
        }

        private void OnActionRequested(DatabaseObject obj, string action)
        {
            if (obj == null) return;

            _package.RecentsStore.RecordAccess(obj);
            _package.RecentsStore.Save();

            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = _package.SettingsProvider.GetSettings();

            switch (action)
            {
                case SearchActions.SelectTop:
                    _package.ScriptingBridge.ScriptSelectTopN(obj, settings.SelectTopNCount);
                    break;
                case SearchActions.ScriptCreate:
                    _package.ScriptingBridge.ScriptCreate(obj);
                    break;
                case SearchActions.ScriptAlter:
                    _package.ScriptingBridge.ScriptAlter(obj);
                    break;
                case SearchActions.Execute:
                    _package.ScriptingBridge.ScriptExecute(obj);
                    break;
                case SearchActions.EditData:
                    _package.ScriptingBridge.EditTableData(obj, settings.SelectTopNCount);
                    break;
                case SearchActions.DesignTable:
                    _package.ScriptingBridge.DesignTable(obj);
                    break;
                case SearchActions.Secondary: // hunting-dog parity for Right-arrow
                    switch (obj.ObjectType)
                    {
                        case DatabaseObjectType.Table:
                            _package.ScriptingBridge.EditTableData(obj, settings.SelectTopNCount);
                            break;
                        case DatabaseObjectType.StoredProcedure:
                        case DatabaseObjectType.ScalarFunction:
                        case DatabaseObjectType.TableValuedFunction:
                            _package.ScriptingBridge.ScriptExecute(obj);
                            break;
                        // View / Synonym: no secondary action (matches hunting-dog)
                    }
                    break;
                default: // SearchActions.Default — matches the bold/Enter item in each context menu
                    switch (obj.ObjectType)
                    {
                        case DatabaseObjectType.Table:
                        case DatabaseObjectType.View:
                            _package.ScriptingBridge.ScriptSelectTopN(obj, settings.SelectTopNCount);
                            break;
                        case DatabaseObjectType.StoredProcedure:
                        case DatabaseObjectType.ScalarFunction:
                        case DatabaseObjectType.TableValuedFunction:
                            _package.ScriptingBridge.ScriptAlter(obj);
                            break;
                        default: // Synonym, etc. — no ALTER form, show CREATE
                            _package.ScriptingBridge.ScriptCreate(obj);
                            break;
                    }
                    break;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshIndexAsync();
        }

        public void FocusSearch()
        {
            SearchPanel.FocusSearchBox();
        }

        public void ShowUpdateNotification(string version, string releaseUrl)
        {
            _pendingUpdate = new UpdateInfo
            {
                IsUpdateAvailable = true,
                LatestVersion = version,
                ReleaseUrl = releaseUrl
            };
            UpdateText.Text = $"SQL Pilot {version} is available.";
            UpdateBar.ToolTip = "Click X to skip this version.";
            UpdateBar.Visibility = Visibility.Visible;
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pendingUpdate?.ReleaseUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _pendingUpdate.ReleaseUrl,
                    UseShellExecute = true
                });
            }
            UpdateBar.Visibility = Visibility.Collapsed;
        }

        private void DismissButton_Click(object sender, RoutedEventArgs e)
        {
            // Dismiss = "skip this version" — don't nag the user again for the same release
            if (_pendingUpdate != null)
            {
                var settings = _package.SettingsProvider.GetSettings();
                settings.SkippedVersion = _pendingUpdate.LatestVersion;
                _package.SettingsProvider.SaveSettings(settings);
            }
            UpdateBar.Visibility = Visibility.Collapsed;
        }
    }
}
