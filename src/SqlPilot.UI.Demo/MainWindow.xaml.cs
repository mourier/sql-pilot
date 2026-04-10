using System.Windows;
using SqlPilot.Core.Search;
using SqlPilot.UI.ViewModels;

namespace SqlPilot.UI.Demo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var searchEngine = new SearchEngine();
            var provider = new MockDatabaseObjectProvider();

            // Load mock data
            searchEngine.RefreshIndexAsync("localhost", "AdventureWorks", provider).Wait();
            searchEngine.RefreshIndexAsync("localhost", "Northwind", provider).Wait();

            var vm = new SearchViewModel(searchEngine);
            SearchPanel.DataContext = vm;
        }
    }
}
