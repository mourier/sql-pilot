using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPilot.Core.Database
{
    public interface IDatabaseObjectProvider
    {
        Task<IReadOnlyList<DatabaseObject>> GetObjectsAsync(
            string serverName,
            string databaseName,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetDatabaseNamesAsync(
            string serverName,
            CancellationToken cancellationToken = default);
    }
}
