using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlPilot.Core.Favorites;
using SqlPilot.Core.Recents;
using SqlPilot.Core.Search;

namespace SqlPilot.UI.ViewModels
{
    public partial class SearchViewModel : ObservableObject
    {
        private readonly ISearchEngine _searchEngine;
        private readonly IFavoritesStore _favorites;
        private readonly IRecentObjectsStore _recents;
        private CancellationTokenSource _searchCts;
        private int _debounceMs = 100;

        [ObservableProperty]
        private string _searchText = "";

        [ObservableProperty]
        private SearchResultItemViewModel _selectedResult;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private int _resultCount;

        public BatchObservableCollection<SearchResultItemViewModel> Results { get; } = new BatchObservableCollection<SearchResultItemViewModel>();

        public SearchViewModel(ISearchEngine searchEngine, IFavoritesStore favorites = null, IRecentObjectsStore recents = null)
        {
            _searchEngine = searchEngine ?? throw new ArgumentNullException(nameof(searchEngine));
            _favorites = favorites;
            _recents = recents;
        }

        public int DebounceMs
        {
            get => _debounceMs;
            set => _debounceMs = Math.Max(0, value);
        }

        partial void OnSearchTextChanged(string value)
        {
            _ = PerformSearchAsync(value);
        }

        /// <summary>
        /// Re-run the current search. Call this after the index has been refreshed
        /// so stale empty-result views get updated.
        /// </summary>
        public void Rerun()
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
                _ = PerformSearchAsync(SearchText);
        }

        private async Task PerformSearchAsync(string query)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query))
            {
                Results.Clear();
                ResultCount = 0;
                StatusText = "Ready";
                return;
            }

            try
            {
                // Short debounce
                if (_debounceMs > 0)
                    await Task.Delay(_debounceMs, ct);

                IsSearching = true;

                // Run search on background thread
                var filter = new SearchFilter { MaxResults = 30 };
                var results = await Task.Run(() => _searchEngine.SearchAsync(query, filter, ct), ct);

                ct.ThrowIfCancellationRequested();

                bool showServer = _searchEngine.GetIndexedServerCount() > 1;

                var viewModels = new List<SearchResultItemViewModel>(results.Count);
                foreach (var result in results)
                    viewModels.Add(SearchResultItemViewModel.FromSearchResult(result, showServer));

                Results.ReplaceAll(viewModels);

                ResultCount = results.Count;
                StatusText = $"{results.Count} result{(results.Count == 1 ? "" : "s")}";

                if (Results.Count > 0)
                    SelectedResult = Results[0];
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        private void ToggleFavorite()
        {
            if (SelectedResult?.DatabaseObject == null || _favorites == null) return;

            var obj = SelectedResult.DatabaseObject;
            if (_favorites.IsFavorite(obj))
            {
                _favorites.RemoveFavorite(obj);
                SelectedResult.IsFavorite = false;
            }
            else
            {
                _favorites.AddFavorite(obj);
                SelectedResult.IsFavorite = true;
            }
            _favorites.Save();
        }

        [RelayCommand]
        private void MoveSelectionUp()
        {
            if (Results.Count == 0) return;
            var idx = Results.IndexOf(SelectedResult);
            if (idx > 0)
                SelectedResult = Results[idx - 1];
        }

        [RelayCommand]
        private void MoveSelectionDown()
        {
            if (Results.Count == 0) return;
            var idx = Results.IndexOf(SelectedResult);
            if (idx < Results.Count - 1)
                SelectedResult = Results[idx + 1];
        }
    }
}
