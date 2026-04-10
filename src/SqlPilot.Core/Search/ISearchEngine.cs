using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Core.Database;

namespace SqlPilot.Core.Search
{
    public interface ISearchEngine
    {
        Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            SearchFilter filter,
            CancellationToken cancellationToken = default);

        Task RefreshIndexAsync(
            string serverName,
            string databaseName,
            IDatabaseObjectProvider provider,
            CancellationToken cancellationToken = default);

        void ClearServer(string serverName);

        void ClearAll();

        int GetIndexedObjectCount();

        int GetIndexedServerCount();
    }
}
