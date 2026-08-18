using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SqlPilot.Core.Persistence;

namespace SqlPilot.Core.Scope
{
    /// <summary>
    /// Exclusion-list implementation of <see cref="ISearchScopeStore"/>.
    ///
    /// Only exclusions are persisted — never inclusions — so a fresh install (or a
    /// deleted scope file) behaves exactly like the pre-scope build: everything is
    /// in scope. Server exclusion is kept separate from per-database exclusion so
    /// re-including a server doesn't silently resurrect databases the user had
    /// unchecked individually.
    /// </summary>
    public sealed class SearchScopeStore : ISearchScopeStore
    {
        private const string ServerPrefix = "S";
        private const string DatabasePrefix = "D";
        private const char PartSeparator = '|';

        private readonly string _filePath;

        private readonly ConcurrentDictionary<string, byte> _excludedServers =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _excludedDatabases =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler ScopeChanged;

        public SearchScopeStore(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public bool IsServerIncluded(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return true;
            return !_excludedServers.ContainsKey(serverName);
        }

        public bool IsDatabaseIncluded(string serverName, string databaseName)
        {
            if (!IsServerIncluded(serverName)) return false;
            if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(databaseName)) return true;

            return !(_excludedDatabases.TryGetValue(serverName, out var databases)
                     && databases.ContainsKey(databaseName));
        }

        public void SetServerIncluded(string serverName, bool included)
        {
            if (string.IsNullOrEmpty(serverName)) return;

            bool changed = included
                ? _excludedServers.TryRemove(serverName, out _)
                : _excludedServers.TryAdd(serverName, 0);

            if (changed) OnScopeChanged();
        }

        public void SetDatabaseIncluded(string serverName, string databaseName, bool included)
        {
            if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(databaseName)) return;

            bool changed;
            if (included)
            {
                changed = _excludedDatabases.TryGetValue(serverName, out var databases)
                          && databases.TryRemove(databaseName, out _);
            }
            else
            {
                var databases = _excludedDatabases.GetOrAdd(
                    serverName,
                    _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
                changed = databases.TryAdd(databaseName, 0);
            }

            if (changed) OnScopeChanged();
        }

        public IReadOnlyCollection<string> GetExcludedServers() => _excludedServers.Keys.ToList();

        public IReadOnlyCollection<string> GetExcludedDatabases(string serverName)
        {
            if (!string.IsNullOrEmpty(serverName) && _excludedDatabases.TryGetValue(serverName, out var databases))
                return databases.Keys.ToList();

            return Array.Empty<string>();
        }

        public void Save()
        {
            var settings = new Dictionary<string, string>();

            foreach (var server in _excludedServers.Keys)
                settings[MakeKey(ServerPrefix, server)] = "1";

            foreach (var kvp in _excludedDatabases)
            {
                foreach (var database in kvp.Value.Keys)
                    settings[MakeKey(DatabasePrefix, kvp.Key, database)] = "1";
            }

            LineStore.SaveSettings(_filePath, settings);
        }

        public void Load()
        {
            _excludedServers.Clear();
            _excludedDatabases.Clear();

            foreach (var key in LineStore.LoadSettings(_filePath).Keys)
            {
                var parts = SplitKey(key);
                if (parts.Length == 2 && string.Equals(parts[0], ServerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _excludedServers.TryAdd(parts[1], 0);
                }
                else if (parts.Length == 3 && string.Equals(parts[0], DatabasePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var databases = _excludedDatabases.GetOrAdd(
                        parts[1],
                        _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
                    databases.TryAdd(parts[2], 0);
                }
            }

            OnScopeChanged();
        }

        private void OnScopeChanged() => ScopeChanged?.Invoke(this, EventArgs.Empty);

        // Composite keys are "S|<server>" and "D|<server>|<database>". Server and
        // database names are escaped before being joined so a '|' inside a name can't
        // be mistaken for a part separator — LineStore escapes the finished key again
        // on the way to disk, which is harmless because the two passes are symmetric.
        private static string MakeKey(params string[] parts)
            => string.Join(PartSeparator.ToString(), parts.Select(EscapePart));

        private static string[] SplitKey(string key)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == '\\' && i + 1 < key.Length)
                {
                    current.Append(key[++i]);
                }
                else if (c == PartSeparator)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            parts.Add(current.ToString());
            return parts.ToArray();
        }

        private static string EscapePart(string value)
            => (value ?? "").Replace("\\", "\\\\").Replace("|", "\\|");
    }
}
