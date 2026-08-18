using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlPilot.Core.Scope;

namespace SqlPilot.UI.ViewModels
{
    /// <summary>
    /// Backs the Scope panel: a checkbox tree of connected servers and their
    /// databases. The tree is a view over <see cref="ISearchScopeStore"/> — every
    /// user toggle writes through to the store immediately and is reported via
    /// <see cref="ScopeToggled"/> so the host can clear or spot-index the index.
    /// </summary>
    public partial class ScopeViewModel : ObservableObject
    {
        private readonly ISearchScopeStore _scope;

        [ObservableProperty]
        private string _summaryText = "No databases";

        public ScopeViewModel(ISearchScopeStore scope)
        {
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public ObservableCollection<ServerScopeNode> Servers { get; } = new ObservableCollection<ServerScopeNode>();

        /// <summary>Raised after a user toggle has been written to the store.</summary>
        public event EventHandler<ScopeToggleEventArgs> ScopeToggled;

        /// <summary>
        /// Merge a server's live database list into the tree. Existing check states
        /// survive, databases that no longer exist are dropped, and new ones show up
        /// checked unless the store says otherwise.
        /// </summary>
        public void MergeServer(string serverName, IReadOnlyList<string> databaseNames)
        {
            var server = Servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                server = new ServerScopeNode(this, serverName, _scope.IsServerIncluded(serverName));
                Servers.Add(server);
            }
            else
            {
                server.IsServerIncluded = _scope.IsServerIncluded(serverName);
            }

            server.RemoveDatabasesExcept(databaseNames);

            var excluded = new HashSet<string>(_scope.GetExcludedDatabases(serverName), StringComparer.OrdinalIgnoreCase);
            foreach (var databaseName in databaseNames)
            {
                // An excluded server shows all its databases unchecked; their own
                // exclusions stay in the store and come back if it is re-included
                // through a Refresh rather than the checkbox.
                bool included = server.IsServerIncluded && !excluded.Contains(databaseName);

                var existing = server.Databases.FirstOrDefault(
                    d => string.Equals(d.Name, databaseName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                    server.AddDatabase(databaseName, included);
                else
                    existing.SetIncludedSilently(included);
            }

            SortDatabases(server);
            server.RaiseIsCheckedChanged();
        }

        /// <summary>
        /// The databases already known for a server, or empty if it isn't in the tree
        /// yet. Lets callers re-index without paying another metadata round trip.
        /// </summary>
        public IReadOnlyList<string> GetDatabaseNames(string serverName)
        {
            var server = Servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
            return server == null
                ? Array.Empty<string>()
                : server.Databases.Select(d => d.Name).ToList();
        }

        /// <summary>Drop servers that are no longer connected.</summary>
        public void PruneServers(IEnumerable<string> connectedServers)
        {
            var live = new HashSet<string>(connectedServers, StringComparer.OrdinalIgnoreCase);
            foreach (var gone in Servers.Where(s => !live.Contains(s.Name)).ToList())
                Servers.Remove(gone);
        }

        /// <summary>Databases currently in scope across all connected servers.</summary>
        public int IncludedDatabaseCount =>
            Servers.Where(s => s.IsServerIncluded).Sum(s => s.Databases.Count(d => d.IsIncluded));

        /// <summary>Databases visible on all connected servers, in scope or not.</summary>
        public int TotalDatabaseCount => Servers.Sum(s => s.Databases.Count);

        /// <summary>Servers contributing at least one in-scope database.</summary>
        public int IncludedServerCount =>
            Servers.Count(s => s.IsServerIncluded && s.Databases.Any(d => d.IsIncluded));

        /// <summary>
        /// The index status line, collapsing to the pre-scope wording
        /// ("… in 70 database(s) from 3 server(s).") when nothing is excluded.
        /// </summary>
        public string DescribeIndexStatus(int objectCount)
        {
            int includedDatabases = IncludedDatabaseCount;
            int totalDatabases = TotalDatabaseCount;
            int includedServers = IncludedServerCount;
            int totalServers = Servers.Count;

            string databasePart = includedDatabases == totalDatabases
                ? $"{totalDatabases} database(s)"
                : $"{includedDatabases} of {totalDatabases} database(s)";

            string serverPart = includedServers == totalServers
                ? $"{totalServers} server(s)"
                : $"{includedServers} of {totalServers} server(s)";

            return $"Indexed {objectCount:N0} objects in {databasePart} from {serverPart}.";
        }

        public void UpdateSummary()
        {
            int included = IncludedDatabaseCount;
            int total = TotalDatabaseCount;

            SummaryText = total == 0
                ? "No databases"
                : included == total
                    ? $"All {total} database(s) in scope"
                    : $"{included} of {total} database(s) in scope";
        }

        internal void OnDatabaseToggled(ServerScopeNode server, DatabaseScopeNode database, bool included)
        {
            _scope.SetDatabaseIncluded(server.Name, database.Name, included);

            // Checking a database on an excluded server implicitly re-includes the
            // server — otherwise the toggle would look like it did nothing. The
            // server's other databases stay unchecked.
            if (included && !server.IsServerIncluded)
            {
                foreach (var other in server.Databases.Where(d => d != database))
                    _scope.SetDatabaseIncluded(server.Name, other.Name, false);

                _scope.SetServerIncluded(server.Name, true);
                server.IsServerIncluded = true;
            }

            _scope.Save();
            server.RaiseIsCheckedChanged();
            UpdateSummary();

            ScopeToggled?.Invoke(this, new ScopeToggleEventArgs(server.Name, database.Name, included));
        }

        internal void OnServerToggled(ServerScopeNode server, bool included)
        {
            _scope.SetServerIncluded(server.Name, included);

            // Checking a server clears its individual database exclusions — the user
            // asked for the whole server back. Unchecking leaves the store's
            // per-database exclusions alone and just greys the children out.
            foreach (var database in server.Databases)
            {
                if (included)
                    _scope.SetDatabaseIncluded(server.Name, database.Name, true);

                database.SetIncludedSilently(included);
            }

            _scope.Save();
            server.IsServerIncluded = included;
            UpdateSummary();

            ScopeToggled?.Invoke(this, new ScopeToggleEventArgs(server.Name, null, included));
        }

        private static void SortDatabases(ServerScopeNode server)
        {
            var sorted = server.Databases.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int current = server.Databases.IndexOf(sorted[i]);
                if (current != i) server.Databases.Move(current, i);
            }
        }
    }

    /// <summary>
    /// A scope toggle the user just made. <see cref="DatabaseName"/> is null for a
    /// server-level toggle.
    /// </summary>
    public sealed class ScopeToggleEventArgs : EventArgs
    {
        public ScopeToggleEventArgs(string serverName, string databaseName, bool included)
        {
            ServerName = serverName;
            DatabaseName = databaseName;
            Included = included;
        }

        public string ServerName { get; }
        public string DatabaseName { get; }
        public bool Included { get; }
    }
}
