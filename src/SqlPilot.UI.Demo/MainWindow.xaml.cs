using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
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

            InputBindings.Add(new KeyBinding(new RelayCommand(ToggleScopePanel), Key.S, ModifierKeys.Alt));

            Loaded += async (s, e) => await RefreshIndexAsync();
        }

        public ScopeViewModel ScopeViewModel { get; }

        public SearchViewModel SearchViewModel { get; }

        private async Task RefreshIndexAsync()
        {
            ScopeViewModel.PruneServers(MockDatabaseObjectProvider.Servers);

            foreach (var serverName in MockDatabaseObjectProvider.Servers)
                await IndexServerAsync(serverName);

            ScopeViewModel.UpdateSummary();
            IndexStatus.Text = ScopeViewModel.DescribeIndexStatus(_searchEngine.GetIndexedObjectCount());
            SearchViewModel.Rerun();
        }

        /// <summary>Merge a server into the scope tree and index whatever is in scope.</summary>
        private async Task IndexServerAsync(string serverName)
        {
            var databases = await _provider.GetDatabaseNamesAsync(serverName);
            ScopeViewModel.MergeServer(serverName, databases);

            // No scope filter here on purpose: the engine holds the same store and
            // skips out-of-scope databases itself, and the demo exists to prove that.
            foreach (var databaseName in databases)
                await _searchEngine.RefreshIndexAsync(serverName, databaseName, _provider);
        }

        /// <summary>Alt+S — drives the toggle button so button, panel and state stay in sync.</summary>
        private void ToggleScopePanel()
        {
            ScopeButton.IsChecked = ScopeButton.IsChecked != true;

            if (ScopeButton.IsChecked == true)
                ScopePanelContent.FocusTree();
            else
                SearchPanel.FocusSearchBox();
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
                    await IndexServerAsync(e.ServerName);
                else
                    _searchEngine.ClearServer(e.ServerName);
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
