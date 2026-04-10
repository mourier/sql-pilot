using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Core.Database;
using SqlPilot.Core.Favorites;
using SqlPilot.Core.Recents;

namespace SqlPilot.Core.Search
{
    public sealed class SearchEngine : ISearchEngine
    {
        private readonly ConcurrentDictionary<string, List<DatabaseObject>> _index =
            new ConcurrentDictionary<string, List<DatabaseObject>>(StringComparer.OrdinalIgnoreCase);
        private readonly IFavoritesStore _favorites;
        private readonly IRecentObjectsStore _recents;

        public SearchEngine(IFavoritesStore favorites = null, IRecentObjectsStore recents = null)
        {
            _favorites = favorites;
            _recents = recents;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            SearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            filter = filter ?? new SearchFilter();

            // Split query on spaces for AND logic: "user table" matches both terms
            var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());

            var scored = new List<(DatabaseObject obj, int score)>();

            foreach (var kvp in _index)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip entire buckets that don't match the filter
                if (filter.ServerName != null || filter.DatabaseName != null)
                {
                    if (TryParseKey(kvp.Key, out var keyServer, out var keyDatabase))
                    {
                        if (filter.ServerName != null && !string.Equals(keyServer, filter.ServerName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (filter.DatabaseName != null && !string.Equals(keyDatabase, filter.DatabaseName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                }

                foreach (var obj in kvp.Value)
                {
                    if (filter.ObjectTypes != null && filter.ObjectTypes.Length > 0
                        && !filter.ObjectTypes.Contains(obj.ObjectType))
                        continue;

                    // All terms must match (AND logic)
                    int totalScore = 0;
                    bool allMatch = true;

                    foreach (var term in terms)
                    {
                        int termScore = FuzzyMatcher.Score(term, obj.ObjectName);
                        if (termScore == 0)
                            termScore = FuzzyMatcher.Score(term, obj.QualifiedName);
                        if (termScore == 0)
                            termScore = FuzzyMatcher.Score(term, obj.DatabaseName);

                        if (termScore == 0)
                        {
                            allMatch = false;
                            break;
                        }
                        totalScore += termScore;
                    }

                    if (!allMatch) continue;

                    // Average score across terms
                    int score = totalScore / terms.Length;

                    if (_favorites?.IsFavorite(obj) == true) score += 50;
                    if (_recents?.IsRecent(obj) == true) score += 25;

                    scored.Add((obj, score));
                }
            }

            // Sort and take top-N, THEN compute matched indices (expensive)
            var topN = scored
                .OrderByDescending(x => x.score)
                .Take(filter.MaxResults)
                .ToList();

            // Use first term for highlighting
            var highlightTerm = terms[0];

            var results = new List<SearchResult>(topN.Count);
            foreach (var (obj, score) in topN)
            {
                results.Add(new SearchResult
                {
                    Object = obj,
                    Score = score,
                    MatchedIndices = FuzzyMatcher.GetMatchedIndices(highlightTerm, obj.ObjectName),
                    IsFavorite = _favorites?.IsFavorite(obj) == true,
                    IsRecent = _recents?.IsRecent(obj) == true
                });
            }

            return Task.FromResult<IReadOnlyList<SearchResult>>(results);
        }

        public async Task RefreshIndexAsync(
            string serverName,
            string databaseName,
            IDatabaseObjectProvider provider,
            CancellationToken cancellationToken = default)
        {
            var objects = await provider.GetObjectsAsync(serverName, databaseName, cancellationToken);
            _index[MakeKey(serverName, databaseName)] = objects.ToList();
        }

        public void ClearServer(string serverName)
        {
            var prefix = serverName + KeySeparator;
            var keysToRemove = _index.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
                _index.TryRemove(key, out _);
        }

        public void ClearAll()
        {
            _index.Clear();
        }

        public int GetIndexedObjectCount()
        {
            int count = 0;
            foreach (var kvp in _index)
                count += kvp.Value.Count;
            return count;
        }

        public int GetIndexedServerCount()
        {
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _index.Keys)
            {
                if (TryParseKey(key, out var server, out _))
                    servers.Add(server);
            }
            return servers.Count;
        }

        // Index buckets are keyed as "server<sep>database". Keep all key
        // construction and parsing routed through these helpers so the format
        // stays consistent across SearchAsync, RefreshIndexAsync, ClearServer,
        // and GetIndexedServerCount.
        private const char KeySeparator = '/';

        private static string MakeKey(string serverName, string databaseName)
            => serverName + KeySeparator + databaseName;

        private static bool TryParseKey(string key, out string serverName, out string databaseName)
        {
            int slash = key.IndexOf(KeySeparator);
            if (slash < 0)
            {
                serverName = key;
                databaseName = null;
                return false;
            }
            serverName = key.Substring(0, slash);
            databaseName = key.Substring(slash + 1);
            return true;
        }
    }
}
