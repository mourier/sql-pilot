using System;
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

        public SqlPilotToolWindowControl(SqlPilotPackage package)
        {
            _package = package;

            InitializeComponent();

            ViewModel = new SearchViewModel(package.SearchEngine, package.FavoritesStore, package.RecentsStore);
            ViewModel.DebounceMs = package.SettingsProvider.GetSettings().SearchDebounceMs;
            SearchPanel.DataContext = ViewModel;

            SearchPanel.ActionRequested += OnActionRequested;
        }

        public async Task RefreshIndexAsync()
        {
            try
            {
                IndexStatus.Text = "Indexing...";
                RefreshButton.IsEnabled = false;

                var servers = _package.ObjectExplorerBridge.GetConnectedServerNames();

                if (servers.Count == 0)
                {
                    IndexStatus.Text = "No servers connected. Select a server in Object Explorer and click Refresh.";
                    RefreshButton.IsEnabled = true;
                    return;
                }

                _package.SearchEngine.ClearAll();
                int totalDatabases = 0;

                foreach (var serverName in servers)
                {
                    IndexStatus.Text = $"Connecting to {serverName}...";

                    var connInfo = _package.ObjectExplorerBridge.GetConnectionInfo(serverName);
                    var smoProvider = new SmoDatabaseObjectProvider(connInfo);

                    IndexStatus.Text = $"Loading databases from {serverName}...";
                    var databases = await smoProvider.GetDatabaseNamesAsync(serverName);

                    int completedDbs = 0;
                    IndexStatus.Text = $"Indexing {serverName} (0/{databases.Count})...";

                    // Parallelize database indexing — each SMO call creates its own connection.
                    // Cap concurrency so we don't exhaust the connection pool on servers with
                    // hundreds of databases.
                    using (var throttle = new System.Threading.SemaphoreSlim(20))
                    {
                        var dbTasks = databases.Select(async dbName =>
                        {
                            await throttle.WaitAsync();
                            try
                            {
                                await Task.Run(() => _package.SearchEngine.RefreshIndexAsync(serverName, dbName, smoProvider));
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
                    totalDatabases += databases.Count;
                }

                int objectCount = _package.SearchEngine.GetIndexedObjectCount();
                IndexStatus.Text = $"Indexed {objectCount:N0} objects in {totalDatabases} database(s) from {servers.Count} server(s).";

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
                RefreshButton.IsEnabled = true;
            }
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
