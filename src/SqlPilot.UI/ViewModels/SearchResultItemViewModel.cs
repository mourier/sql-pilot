using CommunityToolkit.Mvvm.ComponentModel;
using SqlPilot.Core.Database;
using SqlPilot.Core.Search;

namespace SqlPilot.UI.ViewModels
{
    public partial class SearchResultItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private DatabaseObject _databaseObject;

        [ObservableProperty]
        private int _score;

        [ObservableProperty]
        private int[] _matchedIndices;

        [ObservableProperty]
        private bool _isFavorite;

        [ObservableProperty]
        private bool _isRecent;

        [ObservableProperty]
        private bool _showServer;

        // Cached at construction so binding reads (one per row, plus per scroll
        // recycle) don't re-run ServerNameFormatter.Shorten on a hot path.
        private string _serverName = "";
        private string _shortServerName = "";

        public string DisplayName => DatabaseObject?.QualifiedName ?? "";
        public string DatabaseName => DatabaseObject?.DatabaseName ?? "";
        public string ServerName => _serverName;
        public string ShortServerName => _shortServerName;
        public string ObjectTypeName => DatabaseObject?.ObjectType.ToString() ?? "";
        public DatabaseObjectType ObjectType => DatabaseObject?.ObjectType ?? DatabaseObjectType.Table;

        partial void OnDatabaseObjectChanged(DatabaseObject value)
        {
            _serverName = value?.ServerName ?? "";
            _shortServerName = ServerNameFormatter.Shorten(value?.ServerName);
        }

        public static SearchResultItemViewModel FromSearchResult(SearchResult result, bool showServer = false)
        {
            return new SearchResultItemViewModel
            {
                DatabaseObject = result.Object,
                Score = result.Score,
                MatchedIndices = result.MatchedIndices,
                IsFavorite = result.IsFavorite,
                IsRecent = result.IsRecent,
                ShowServer = showServer
            };
        }
    }
}
