using System;
using System.Collections.Generic;

namespace SqlPilot.Core.Scope
{
    /// <summary>
    /// Which servers and databases searching and indexing are allowed to touch.
    ///
    /// The model is default-include: only explicit exclusions are tracked, so a
    /// server or database nobody has ever unchecked is in scope, and databases
    /// that appear later are picked up automatically.
    /// </summary>
    public interface ISearchScopeStore
    {
        bool IsServerIncluded(string serverName);

        /// <summary>
        /// False when the database itself is excluded or its server is.
        /// </summary>
        bool IsDatabaseIncluded(string serverName, string databaseName);

        void SetServerIncluded(string serverName, bool included);

        void SetDatabaseIncluded(string serverName, string databaseName, bool included);

        IReadOnlyCollection<string> GetExcludedServers();

        IReadOnlyCollection<string> GetExcludedDatabases(string serverName);

        void Save();

        void Load();

        event EventHandler ScopeChanged;
    }
}
