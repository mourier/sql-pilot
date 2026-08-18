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
    /// in scope.
    ///
    /// Server exclusion is stored separately from per-database exclusion, so
    /// SetServerIncluded(true) leaves individual database exclusions standing. That
    /// is the storage rule only — ScopeViewModel deliberately clears a server's
    /// database exclusions when the user re-checks the server itself, because at the
    /// UI level "check the server" means "give me the whole server back".
    /// </summary>
    public sealed class SearchScopeStore : ISearchScopeStore
    {
        private const string ServerPrefix = "S";
        private const string DatabasePrefix = "D";

        private readonly string _filePath;

        private readonly ConcurrentDictionary<string, byte> _excludedServers = NewNameSet();

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
                changed = _excludedDatabases.GetOrAdd(serverName, _ => NewNameSet()).TryAdd(databaseName, 0);
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

        // Composite keys are "S|<server>" and "D|<server>|<database>", built with
        // LineStore.Join so a '|' inside a name is escaped rather than mistaken for a
        // part separator.
        public void Save()
        {
            var settings = new Dictionary<string, string>();

            foreach (var server in _excludedServers.Keys)
                settings[LineStore.Join(ServerPrefix, server)] = "1";

            foreach (var kvp in _excludedDatabases)
            {
                foreach (var database in kvp.Value.Keys)
                    settings[LineStore.Join(DatabasePrefix, kvp.Key, database)] = "1";
            }

            LineStore.SaveSettings(_filePath, settings);
        }

        public void Load()
        {
            _excludedServers.Clear();
            _excludedDatabases.Clear();

            foreach (var key in LineStore.LoadSettings(_filePath).Keys)
            {
                var parts = LineStore.Split(key);
                if (parts.Length == 2 && string.Equals(parts[0], ServerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _excludedServers.TryAdd(parts[1], 0);
                }
                else if (parts.Length == 3 && string.Equals(parts[0], DatabasePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _excludedDatabases.GetOrAdd(parts[1], _ => NewNameSet()).TryAdd(parts[2], 0);
                }
            }

            OnScopeChanged();
        }

        private void OnScopeChanged() => ScopeChanged?.Invoke(this, EventArgs.Empty);

        private static ConcurrentDictionary<string, byte> NewNameSet()
            => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    }
}
