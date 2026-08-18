using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SqlPilot.Core.Scope;
using SqlPilot.Core.Search;
using SqlPilot.UI.ViewModels;

namespace SqlPilot.UI.Demo
{
    public partial class MainWindow : Window
    {
        private readonly SearchEngine _searchEngine;
        private readonly SearchScopeStore _scopeStore;
        private readonly MockDatabaseObjectProvider _provider = new MockDatabaseObjectProvider();

        public MainWindow()
        {
            InitializeComponent();

            // Scope survives demo restarts, same as it does in SSMS.
            _scopeStore = new SearchScopeStore(Path.Combine(
                Path.GetTempPath(), "SqlPilotDemo", "scope.txt"));
            _scopeStore.Load();

            _searchEngine = new SearchEngine(null, null, _scopeStore);

            ScopeViewModel = new ScopeViewModel(_scopeStore);
            ScopeViewModel.ScopeToggled += OnScopeToggled;
            ScopePanel.DataContext = ScopeViewModel;

            SearchViewModel = new SearchViewModel(_searchEngine);
            SearchPanel.DataContext = SearchViewModel;

            Loaded += async (s, e) => await RefreshIndexAsync();
        }

        public ScopeViewModel ScopeViewModel { get; }

        public SearchViewModel SearchViewModel { get; }

        private async Task RefreshIndexAsync()
        {
            ScopeViewModel.PruneServers(MockDatabaseObjectProvider.Servers);

            foreach (var serverName in MockDatabaseObjectProvider.Servers)
            {
                var databases = await _provider.GetDatabaseNamesAsync(serverName);
                ScopeViewModel.MergeServer(serverName, databases);

                foreach (var databaseName in databases.Where(db => _scopeStore.IsDatabaseIncluded(serverName, db)))
                    await _searchEngine.RefreshIndexAsync(serverName, databaseName, _provider);
            }

            ScopeViewModel.UpdateSummary();
            IndexStatus.Text = ScopeViewModel.DescribeIndexStatus(_searchEngine.GetIndexedObjectCount());
            SearchViewModel.Rerun();
        }

        private void ScopeButton_Toggled(object sender, RoutedEventArgs e)
        {
            ScopePanel.Visibility = ScopeButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnScopeToggled(object sender, ScopeToggleEventArgs e)
        {
            _ = ApplyScopeToggleAsync(e);
        }

        private async Task ApplyScopeToggleAsync(ScopeToggleEventArgs e)
        {
            if (e.DatabaseName == null)
            {
                if (e.Included)
                {
                    var databases = await _provider.GetDatabaseNamesAsync(e.ServerName);
                    foreach (var databaseName in databases.Where(db => _scopeStore.IsDatabaseIncluded(e.ServerName, db)))
                        await _searchEngine.RefreshIndexAsync(e.ServerName, databaseName, _provider);
                }
                else
                {
                    _searchEngine.ClearServer(e.ServerName);
                }
            }
            else
            {
                if (e.Included)
                    await _searchEngine.RefreshIndexAsync(e.ServerName, e.DatabaseName, _provider);
                else
                    _searchEngine.ClearDatabase(e.ServerName, e.DatabaseName);
            }

            IndexStatus.Text = ScopeViewModel.DescribeIndexStatus(_searchEngine.GetIndexedObjectCount());
            SearchViewModel.Rerun();
        }
    }
}
